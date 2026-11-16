using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
namespace DotLens.Tools.Search;

#region Response Types

/// <summary>
/// Result for get_type_members tool.
/// </summary>
public record GetTypeMembersResponse
{
   public required string TypeName { get; init; }
   public required string TypeKind { get; init; }
   public required int TotalMembers { get; init; }
   public required IReadOnlyDictionary<string, int> MemberCounts { get; init; }
   public required IReadOnlyList<MemberInfo> Members { get; init; }
}

#endregion

/// <summary>
/// Get all members of a type.
/// </summary>
[McpServerToolType]
[DotLensWorkerTool]
public class GetTypeMembersTool
{
   private readonly IReadOnlyWorkspace _workspace;
   private readonly SymbolLookupService _symbolLookup;

   public GetTypeMembersTool(IReadOnlyWorkspace workspace, SymbolLookupService symbolLookup)
   {
      _workspace = workspace;
      _symbolLookup = symbolLookup;
   }

   /// <summary>
   /// Get all members of a type.
   /// </summary>
   [McpServerTool(Name = "get_type_members", ReadOnly = true, OpenWorld = false)]
   [Description("Get all members (methods, properties, fields, events) of a type with optional filtering by kind and inheritance.")]
   public async Task<ToolResult<GetTypeMembersResponse>> GetTypeMembersAsync(
      [Description("Type to get members for")] SymbolLookup target,
      [Description("Also list members inherited from base classes other than object, static ones included. Private members, constructors, destructors and members that a more derived class overrides or hides are left out.")]
      bool includeInherited = false,
      [Description("Filter by member kind")]
      MemberKindFilter? memberKind = null,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      [Description("Maximum members to return")]
      [PositiveInteger]
      int maxResults = 100,
      CancellationToken cancellationToken = default)
   {
      var lookupResult = await _symbolLookup.FindSingleAsync<INamedTypeSymbol>(target, cancellationToken: cancellationToken);
      var type = lookupResult.Symbol;

      var allMembers = RoslynHelpers.GetTypeMembers(type, includeInherited).ToList();

      // Filter by member kind
      if (memberKind.HasValue)
      {
         allMembers = allMembers.Where(m => MatchesMemberKind(m, memberKind.Value)).ToList();
      }

      var uniqueMembers = allMembers.Take(maxResults).ToList();
      var totalCount = allMembers.Count;
      var formattedMembers = uniqueMembers
         .Select(m => RoslynHelpers.FormatMember(m, verbosity, _workspace.SolutionPath, _workspace.UseAbsolutePaths))
         .ToList();

      var countByKind = uniqueMembers
         .GroupBy(m => RoslynHelpers.GetMemberKindString(m))
         .ToDictionary(g => g.Key, g => g.Count());

      return ToolResult.Create(
         new GetTypeMembersResponse
         {
            TypeName = type.ToDisplayString(),
            TypeKind = type.TypeKind.ToString(),
            TotalMembers = totalCount,
            MemberCounts = countByKind,
            Members = formattedMembers
         },
         suggestedNextTools: new[]
         {
            "get_method_signature to get detailed parameter info for a specific method",
            $"get_base_types for {RoslynHelpers.GetName(type)} to see inheritance chain",
            "get_attributes to find decorated members"
         },
         totalCount: totalCount,
         returnedCount: uniqueMembers.Count,
         verbosity: verbosity);
   }

   private static bool MatchesMemberKind(ISymbol member, MemberKindFilter kind)
   {
      return kind switch
      {
         MemberKindFilter.Method => member is IMethodSymbol ms && ms.MethodKind == MethodKind.Ordinary,
         MemberKindFilter.Property => member is IPropertySymbol,
         MemberKindFilter.Field => member is IFieldSymbol,
         MemberKindFilter.Event => member is IEventSymbol,
         _ => true
      };
   }
}
