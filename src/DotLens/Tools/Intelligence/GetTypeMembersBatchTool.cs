using System.ComponentModel;

using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using Microsoft.CodeAnalysis;

using ModelContextProtocol.Server;

namespace DotLens.Tools.Intelligence;

[McpServerToolType]
[DotLensWorkerTool]
public class GetTypeMembersBatchTool
{
   private readonly IReadOnlyWorkspace _workspaceManager;
   private readonly SymbolLookupService _symbolLookup;

   public GetTypeMembersBatchTool(IReadOnlyWorkspace workspaceManager, SymbolLookupService symbolLookup)
   {
      _workspaceManager = workspaceManager;
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "get_type_members_batch", ReadOnly = true, OpenWorld = false)]
   [Description("Get members for multiple types in a single call. Batch alternative to multiple get_type_members calls.")]
   public async Task<ToolResult<TypeMembersBatchResponse>> GetTypeMembersBatchAsync(
      [Description("Array of type names to look up")] List<string> typeNames,
      [Description("Filter by member kind")]
      MemberKindFilter? memberKind = null,
      [Description("Also list members inherited from base classes other than object, static ones included. Members the type cannot access, constructors, destructors and members that a more derived class overrides or hides are left out.")]
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

         SymbolLookupResult<INamedTypeSymbol> lookupResult;
         try
         {
            lookupResult = await _symbolLookup.FindSingleAsync<INamedTypeSymbol>(new LookupByName { Name = typeName }, cancellationToken: cancellationToken);
         }
         catch (ToolException ex)
         {
            var error = ex.Code == ErrorCodes.SymbolNotFound ? $"Type not found: {typeName}" : ex.Message;
            errors.Add(new TypeMembersError { TypeName = typeName, Error = error });
            continue;
         }
         var typeSymbol = lookupResult.Symbol;

         var members = RoslynHelpers.GetTypeMembers(typeSymbol, lookupResult.Compilation, includeInherited);
         if (memberKind is { } kind)
            members = members.Where(m => RoslynHelpers.MatchesMemberKind(m, kind));

         var memberList = members
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
