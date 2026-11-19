using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Navigation;

#region Response Types

/// <summary>
/// Response from GoToDefinition navigation.
/// </summary>
public record GoToDefinitionResponse
{
   /// <summary>The symbol at the target location.</summary>
   public required SymbolResponse Symbol { get; init; }

   /// <summary>
   /// Definition locations in source code. Always contains at least one item.
   /// When allLocations=false (default), contains only the first definition.
   /// When allLocations=true, contains all locations for partial types/methods.
   /// </summary>
   public required IReadOnlyList<SourceSpan> Definitions { get; init; }

   /// <summary>
   /// Source code for each definition location (when includeSource=true and symbol is a method/property/event).
   /// Parallel to Definitions array - SourceCodes[i] corresponds to Definitions[i].
   /// Null when includeSource=false or symbol doesn't support source extraction.
   /// </summary>
   public IReadOnlyList<string>? SourceCodes { get; init; }
}

#endregion

[McpServerToolType]
[DotLensWorkerTool]
public class GoToDefinitionTool
{
   private readonly IReadOnlyWorkspace _workspace;
   private readonly SymbolLookupService _symbolLookup;

   public GoToDefinitionTool(IReadOnlyWorkspace workspace, SymbolLookupService symbolLookup)
   {
      _workspace = workspace;
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "go_to_definition", ReadOnly = true, Idempotent = true, OpenWorld = false)]
   [Description("Navigate to a symbol's definition location.")]
   public async Task<ToolResult<GoToDefinitionResponse>> GoToDefinitionAsync(
      [Description("Symbol to navigate to.")]
      SymbolLookup target,
      [Description("Include source code in response (only for methods/properties/events).")]
      bool includeSource = false,
      [Description("Return all locations for partial types/methods.")]
      bool allLocations = false,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      CancellationToken cancellationToken = default)
   {
      // Find the symbol using SymbolLookupService (validates solution is loaded internally)
      var lookupResult = await _symbolLookup.FindSingleAsync<ISymbol>(target, filter: null, cancellationToken);
      var symbol = lookupResult.Symbol;
      var solution = _workspace.EnsureSolutionLoaded();

      // Get source locations with deterministic ordering (by file path, then line number)
      var sourceLocations = symbol.Locations
         .Where(loc => loc.IsInSource)
         .OrderBy(l => l.GetLineSpan().Path)
         .ThenBy(l => l.GetLineSpan().StartLinePosition.Line)
         .ToList();
      if (sourceLocations.Count == 0)
      {
         throw new ToolException(
            ErrorCodes.SymbolInMetadata,
            "Symbol definition not in source",
            "Symbol is defined in metadata (external library). Use get_type_members to explore its API.");
      }

      // Check if symbol supports source extraction
      var supportsSource = symbol is IMethodSymbol or IPropertySymbol or IEventSymbol;

      // Determine which locations to include
      var locationsToInclude = allLocations ? sourceLocations : sourceLocations.Take(1).ToList();

      // Build definitions from source locations
      var definitions = locationsToInclude
         .Select(loc => loc.GetLineSpan().ToSourceSpan(_workspace.FormatPath))
         .ToList();

      // Build source codes if requested (from DeclaringSyntaxReferences)
      List<string>? sourceCodes = null;
      if (includeSource && supportsSource)
      {
         var syntaxRefs = allLocations
            ? symbol.DeclaringSyntaxReferences
            : symbol.DeclaringSyntaxReferences.Take(1);

         sourceCodes = new List<string>();
         foreach (var syntaxRef in syntaxRefs)
         {
            var syntax = await syntaxRef.GetSyntaxAsync(cancellationToken);
            var code = syntax.ToFullString();
            if (verbosity != ToolResultVerbosity.Full)
            {
               code = RoslynHelpers.CompactSourceOutput(code);
            }
            sourceCodes.Add(code);
         }
      }

      // Build suggested next tools
      var symbolName = RoslynHelpers.GetName(symbol);
      var suggestions = new List<string>
      {
         $"find_references to see all usages of {symbolName}",
         $"get_symbol_info for more details about {symbolName}"
      };
      if (symbol is INamedTypeSymbol)
         suggestions.Add($"get_type_members for {symbolName}");
      if ((symbol is IMethodSymbol or IPropertySymbol or IEventSymbol) && !includeSource)
         suggestions.Add($"get_method_source to see the implementation");

      return ToolResult.Create(
         new GoToDefinitionResponse
         {
            Symbol = await CreateSymbolResponseAsync(symbol, solution, verbosity, cancellationToken),
            Definitions = definitions,
            SourceCodes = sourceCodes
         },
         suggestedNextTools: suggestions.ToArray());
   }

   private Task<SymbolResponse> CreateSymbolResponseAsync(
      ISymbol symbol, Solution solution, ToolResultVerbosity verbosity, CancellationToken cancellationToken)
   {
      return RoslynHelpers.CreateSymbolResponseAsync(
         symbol, verbosity, solution, _workspace.SolutionPath, _workspace.UseAbsolutePaths, cancellationToken);
   }
}
