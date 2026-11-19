using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;
namespace DotLens.Tools.Search;

/// <summary>
/// Find all types that inherit from or implement a base type.
/// </summary>
[McpServerToolType]
[DotLensWorkerTool]
public class GetDerivedTypesTool
{
   private readonly IReadOnlyWorkspace _workspace;
   private readonly SymbolLookupService _symbolLookup;

   public GetDerivedTypesTool(IReadOnlyWorkspace workspace, SymbolLookupService symbolLookup)
   {
      _workspace = workspace;
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "get_derived_types", ReadOnly = true, OpenWorld = false)]
   [Description("Find all types that inherit from a base class or implement an interface. Set includeTransitive=false for direct subclasses only.")]
   public async Task<ToolResult<GetDerivedTypesResponse>> GetDerivedTypesAsync(
      [Description("Base type to find derived types for.")] SymbolLookup target,
      [Description("Include indirect descendants (grandchildren, etc). Set to false for direct subclasses only.")]
      bool includeTransitive = true,
      [Description("Maximum number of derived types to return.")]
      [PositiveInteger]
      int maxResults = 100,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      CancellationToken cancellationToken = default)
   {
      var solution = _workspace.EnsureSolutionLoaded();

      // Find the base type symbol
      var lookupResult = await _symbolLookup.FindSingleAsync<INamedTypeSymbol>(target, filter: null, cancellationToken);
      var baseType = lookupResult.Symbol;

      // Find derived types - use appropriate Roslyn API based on type kind
      IEnumerable<INamedTypeSymbol> derivedTypes;
      if (baseType.TypeKind == TypeKind.Struct)
      {
         // Structs are sealed by design and cannot be inherited
         derivedTypes = [];
      }
      else if (baseType.TypeKind == TypeKind.Interface)
      {
         var implementations = await SymbolFinder.FindImplementationsAsync(
            baseType, solution, transitive: includeTransitive, cancellationToken: cancellationToken);
         derivedTypes = implementations.OfType<INamedTypeSymbol>();
      }
      else
      {
         derivedTypes = await SymbolFinder.FindDerivedClassesAsync(
            baseType, solution, transitive: includeTransitive, cancellationToken: cancellationToken);
      }

      var derivedList = derivedTypes.ToList();
      var totalCount = derivedList.Count;

      // Build response based on verbosity
      List<SymbolResponse>? results = null;
      if (verbosity != ToolResultVerbosity.Summary)
      {
         results = [];
         foreach (var dt in derivedList.Take(maxResults))
            results.Add(await CreateSymbolResponseAsync(dt, solution, verbosity, cancellationToken));
      }

      // Build context-aware suggestions
      var suggestions = new List<string>();
      if (totalCount > 0)
      {
         suggestions.Add("get_type_members to see members of a derived type");
      }
      suggestions.Add("get_type_hierarchy to see the full inheritance context");
      if (totalCount > maxResults)
      {
         suggestions.Add($"get_derived_types with higher maxResults to see all {totalCount} types");
      }

      return ToolResult.Create(
         new GetDerivedTypesResponse
         {
            BaseType = baseType.ToDisplayString(),
            BaseTypeKind = baseType.TypeKind.ToTypeKindDto(),
            TotalDerived = totalCount,
            DerivedTypes = results
         },
         suggestedNextTools: suggestions.ToArray(),
         totalCount: totalCount,
         returnedCount: results?.Count ?? 0);
   }

   private Task<SymbolResponse> CreateSymbolResponseAsync(
      INamedTypeSymbol symbol, Solution solution, ToolResultVerbosity verbosity, CancellationToken cancellationToken)
   {
      return RoslynHelpers.CreateSymbolResponseAsync(
         symbol, verbosity, solution, _workspace.SolutionPath, _workspace.UseAbsolutePaths, cancellationToken);
   }
}

/// <summary>
/// Result for get_derived_types tool.
/// </summary>
public record GetDerivedTypesResponse
{
   [Description("Fully qualified name of the base type queried.")]
   public required string BaseType { get; init; }

   [Description("Kind of the base type.")]
   public required TypeKindDto BaseTypeKind { get; init; }

   [Description("Total number of derived types found.")]
   public required int TotalDerived { get; init; }

   [Description("List of derived types (null at summary verbosity).")]
   public IReadOnlyList<SymbolResponse>? DerivedTypes { get; init; }
}
