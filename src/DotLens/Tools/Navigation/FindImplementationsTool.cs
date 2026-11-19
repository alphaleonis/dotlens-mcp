using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;
namespace DotLens.Tools.Navigation;

#region Response Types

/// <summary>
/// Result from FindImplementations.
/// </summary>
public record FindImplementationsResponse
{
   /// <summary>The interface or abstract class being queried.</summary>
   public required SymbolResponse BaseType { get; init; }

   /// <summary>Total number of implementations found.</summary>
   public required int TotalImplementations { get; init; }

   /// <summary>List of implementing types (null at summary verbosity).</summary>
   public IReadOnlyList<SymbolResponse>? Implementations { get; init; }
}

#endregion

[McpServerToolType]
[DotLensWorkerTool]
public class FindImplementationsTool
{
   private readonly IReadOnlyWorkspace _workspace;
   private readonly SymbolLookupService _symbolLookup;

   public FindImplementationsTool(IReadOnlyWorkspace workspace, SymbolLookupService symbolLookup)
   {
      _workspace = workspace;
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "find_implementations", ReadOnly = true, OpenWorld = false)]
   [Description("Find all implementations of an interface or abstract class.")]
   public async Task<ToolResult<FindImplementationsResponse>> FindImplementationsAsync(
      [Description("Interface or abstract class to find implementations for.")] SymbolLookup target,
      [Description("Maximum number of implementations to return.")]
      [PositiveInteger]
      int maxResults = 50,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      CancellationToken cancellationToken = default)
   {
      var solution = _workspace.EnsureSolutionLoaded();

      // Find the symbol - must be a named type (interface or class)
      var lookupResult = await _symbolLookup.FindSingleAsync<INamedTypeSymbol>(
         target,
         cancellationToken: cancellationToken);

      var typeSymbol = lookupResult.Symbol;

      // Find all implementations
      var implementations = await SymbolFinder.FindImplementationsAsync(typeSymbol, solution, cancellationToken: cancellationToken);
      var allImplementations = implementations.ToList();
      var totalImplementations = allImplementations.Count;

      // Build implementation list (respecting maxResults)
      var implementationResponses = new List<SymbolResponse>();
      if (verbosity != ToolResultVerbosity.Summary)
      {
         foreach (var impl in allImplementations.Take(maxResults))
         {
            implementationResponses.Add(await CreateSymbolResponseAsync(impl, solution, verbosity, cancellationToken));
         }
      }

      // Build context-aware suggestions
      var suggestions = new List<string>
      {
         "get_type_members for each implementation to see their members",
         $"get_type_hierarchy for {RoslynHelpers.GetName(typeSymbol)} to see full inheritance tree"
      };

      if (totalImplementations > maxResults)
         suggestions.Add($"find_implementations with higher maxResults to see all {totalImplementations} implementations");

      return ToolResult.Create(
         new FindImplementationsResponse
         {
            BaseType = await CreateSymbolResponseAsync(typeSymbol, solution, verbosity, cancellationToken),
            TotalImplementations = totalImplementations,
            Implementations = verbosity == ToolResultVerbosity.Summary ? null : implementationResponses
         },
         suggestedNextTools: suggestions.ToArray(),
         totalCount: totalImplementations,
         returnedCount: implementationResponses.Count);
   }

   private Task<SymbolResponse> CreateSymbolResponseAsync(
      ISymbol symbol, Solution solution, ToolResultVerbosity verbosity, CancellationToken cancellationToken)
   {
      return RoslynHelpers.CreateSymbolResponseAsync(
         symbol, verbosity, solution, _workspace.SolutionPath, _workspace.UseAbsolutePaths, cancellationToken);
   }
}
