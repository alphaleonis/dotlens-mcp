using System.ComponentModel;

using DotLens.Infrastructure;
using Microsoft.CodeAnalysis;

using ModelContextProtocol.Server;

namespace DotLens.Tools.Intelligence;

[McpServerToolType]
[DotLensWorkerTool]
public class GetTypeMembersBatchTool
{
   private readonly IReadOnlyWorkspace _workspaceManager;

   public GetTypeMembersBatchTool(IReadOnlyWorkspace workspaceManager)
   {
      _workspaceManager = workspaceManager;
   }

   [McpServerTool(Name = "get_type_members_batch", ReadOnly = true, OpenWorld = false)]
   [Description("Get members for multiple types in a single call. Batch alternative to multiple get_type_members calls.")]
   public async Task<ToolResult<TypeMembersBatchResponse>> GetTypeMembersBatchAsync(
      [Description("Array of type names to look up")] List<string> typeNames,
      [Description("Filter by member kind")]
      MemberKindFilter? memberKind = null,
      [Description("Include inherited members")]
      bool includeInherited = false,
      [Description("Max members per type")]
      [PositiveInteger]
      int maxResultsPerType = 50,
      [Description("Response detail level")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      CancellationToken cancellationToken = default)
   {
      var results = new List<TypeMembersInfo>();
      var errors = new List<TypeMembersError>();

      foreach (var typeName in typeNames)
      {
         cancellationToken.ThrowIfCancellationRequested();
         // Use FindTypeWithCompilationAsync for ambiguity detection
         // Note: Using direct lookup instead of SymbolLookupService because batch semantics
         // require continue-on-failure, not throw-on-not-found
         var lookupResult = await _workspaceManager.FindTypeWithCompilationAsync(typeName, cancellationToken: cancellationToken);
         if (lookupResult == null)
         {
            errors.Add(new TypeMembersError { TypeName = typeName, Error = $"Type not found: {typeName}" });
            continue;
         }
         var typeSymbol = lookupResult.Type;

         var members = includeInherited
            ? typeSymbol.GetMembers().Concat(IntelligenceHelpers.GetInheritedMembers(typeSymbol))
            : typeSymbol.GetMembers();

         if (memberKind.HasValue)
         {
            members = memberKind.Value switch
            {
               MemberKindFilter.Method => members.OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary).Cast<ISymbol>(),
               MemberKindFilter.Property => members.OfType<IPropertySymbol>().Cast<ISymbol>(),
               MemberKindFilter.Field => members.OfType<IFieldSymbol>().Cast<ISymbol>(),
               MemberKindFilter.Event => members.OfType<IEventSymbol>().Cast<ISymbol>(),
               _ => throw new ArgumentOutOfRangeException(nameof(memberKind), memberKind.Value, "Unexpected member kind filter"),
            };
         }

         var memberList = members
            .Where(m => !m.IsImplicitlyDeclared)
            .Take(maxResultsPerType)
            .Select(m => RoslynHelpers.FormatMember(m, verbosity, _workspaceManager.SolutionPath, _workspaceManager.UseAbsolutePaths))
            .ToList();

         results.Add(new TypeMembersInfo
         {
            TypeName = typeSymbol.ToDisplayString(),
            MemberCount = memberList.Count,
            Members = memberList
         });
      }

      return ToolResult.Create(
         new TypeMembersBatchResponse
         {
            Requested = typeNames.Count,
            Succeeded = results.Count,
            Failed = errors.Count,
            Results = results,
            Errors = errors
         },
         suggestedNextTools: null,
         totalCount: typeNames.Count,
         returnedCount: results.Count);
   }
}

#region Result Types

/// <summary>
/// Result for GetTypeMembersBatchTool.
/// </summary>
public record TypeMembersBatchResponse
{
   [Description("Number of types requested")]
   public required int Requested { get; init; }

   [Description("Number of types successfully processed")]
   public required int Succeeded { get; init; }

   [Description("Number of types that failed (not found or error)")]
   public required int Failed { get; init; }

   [Description("Results for successfully processed types")]
   public required IReadOnlyList<TypeMembersInfo> Results { get; init; }

   [Description("Errors for types that could not be processed")]
   public required IReadOnlyList<TypeMembersError> Errors { get; init; }
}

/// <summary>
/// Member information for a single type in batch results.
/// </summary>
public record TypeMembersInfo
{
   [Description("Fully qualified type name")]
   public required string TypeName { get; init; }

   [Description("Number of members returned for this type")]
   public required int MemberCount { get; init; }

   [Description("List of members")]
   public required IReadOnlyList<MemberInfo> Members { get; init; }
}

/// <summary>
/// Error information for a type that could not be processed.
/// </summary>
public record TypeMembersError
{
   [Description("Type name that was requested")]
   public required string TypeName { get; init; }

   [Description("Error message describing why the type could not be processed")]
   public required string Error { get; init; }
}

#endregion
