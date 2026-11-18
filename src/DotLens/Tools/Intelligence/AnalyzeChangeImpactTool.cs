using System.ComponentModel;
using System.Text.Json.Serialization;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
namespace DotLens.Tools.Intelligence;

[McpServerToolType]
[DotLensWorkerTool]
public class AnalyzeChangeImpactTool
{
   private readonly IReadOnlyWorkspace _workspace;
   private readonly SymbolLookupService _symbolLookup;

   public AnalyzeChangeImpactTool(IReadOnlyWorkspace workspace, SymbolLookupService symbolLookup)
   {
      _workspace = workspace;
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "analyze_change_impact", ReadOnly = true, OpenWorld = false)]
   [Description("Analyze what would break if you change a symbol. Returns impacted locations and whether the change is safe.")]
   public async Task<ToolResult<ChangeImpactResponse>> AnalyzeChangeImpactAsync(
      [Description("Symbol to analyze.")] SymbolLookup target,
      [Description("Type of change to analyze.")] ChangeType changeType,
      [Description("New value for rename or changeType operations.")]
      string? newValue = null,
      [Description("Maximum number of impacted locations to return.")]
      [PositiveInteger]
      int maxLocations = 50,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      CancellationToken cancellationToken = default)
   {
      var solution = _workspace.EnsureSolutionLoaded();

      // Find the symbol using the unified lookup service
      var lookupResult = await _symbolLookup.FindSingleAsync<ISymbol>(target, filter: null, cancellationToken);
      var symbol = lookupResult.Symbol;

      // Find all references to the symbol
      var references = await ReferenceSearch.FindReferencesAsync(symbol, solution, cancellationToken);

      // Get definition locations to check against
      var definitionSpans = symbol.DeclaringSyntaxReferences
         .Select(dsr => dsr.SyntaxTree.GetLineSpan(dsr.Span))
         .ToHashSet();

      var impactedLocations = references
         .Select(loc =>
         {
            var lineSpan = loc.Location.GetLineSpan();
            var isDefinition = definitionSpans.Any(defSpan =>
               defSpan.Path == lineSpan.Path &&
               defSpan.StartLinePosition == lineSpan.StartLinePosition);
            return new ImpactLocation
            {
               FilePath = _workspace.FormatPath(lineSpan.Path) ?? lineSpan.Path,
               Line = lineSpan.StartLinePosition.Line + 1,
               Column = lineSpan.StartLinePosition.Character + 1,
               IsDefinition = isDefinition,
               IsGenerated = loc.Document is SourceGeneratedDocument
            };
         })
         .ToList();

      var totalCount = impactedLocations.Count;
      var impactAnalysis = IntelligenceHelpers.AnalyzeImpact(symbol, changeType, newValue, totalCount);

      // Build response based on verbosity
      List<ImpactLocation>? locations = null;
      if (verbosity != ToolResultVerbosity.Summary)
      {
         locations = impactedLocations.Take(maxLocations).ToList();
      }

      // Build context-aware suggestions
      var suggestions = new List<string>();
      if (totalCount > 0)
      {
         suggestions.Add("find_references for detailed reference list");
      }
      if (totalCount > maxLocations)
      {
         suggestions.Add($"analyze_change_impact with higher maxLocations to see all {totalCount} locations");
      }

      return ToolResult.Create(
         new ChangeImpactResponse
         {
            SymbolName = RoslynHelpers.GetName(symbol),
            SymbolKind = symbol.ToSymbolKindDto(),
            ChangeType = changeType,
            NewValue = newValue,
            ReferenceCount = totalCount,
            IsSafe = impactAnalysis.IsSafe,
            BreakingChangeReason = impactAnalysis.BreakingChangeReason,
            ImpactedLocations = locations,
            Suggestions = impactAnalysis.Suggestions
         },
         suggestedNextTools: suggestions.ToArray(),
         totalCount: totalCount,
         returnedCount: locations?.Count ?? 0);
   }
}

/// <summary>
/// Result for analyze_change_impact tool.
/// </summary>
public record ChangeImpactResponse
{
   [Description("Name of the symbol being analyzed.")]
   public required string SymbolName { get; init; }

   [Description("Kind of the symbol (class, method, property, etc.).")]
   public required SymbolKindDto SymbolKind { get; init; }

   [Description("Type of change being analyzed.")]
   public required ChangeType ChangeType { get; init; }

   [Description("New value for rename or changeType operations.")]
   public string? NewValue { get; init; }

   [Description("Total number of references to the symbol.")]
   public required int ReferenceCount { get; init; }

   [Description("Whether the change is considered safe (no breaking changes detected).")]
   public required bool IsSafe { get; init; }

   [Description("Explanation of why the change may be breaking.")]
   public string? BreakingChangeReason { get; init; }

   [Description("Locations that would be impacted by the change. Null at summary verbosity.")]
   public IReadOnlyList<ImpactLocation>? ImpactedLocations { get; init; }

   [Description("Suggestions for making the change safely.")]
   public IReadOnlyList<string>? Suggestions { get; init; }
}

/// <summary>
/// A location impacted by a proposed change.
/// Extends SymbolLocation with definition indicator.
/// </summary>
public record ImpactLocation : SymbolLocation
{

   [Description("Whether this location is the symbol definition (vs a reference).")]
   public required bool IsDefinition { get; init; }

   [Description("Whether this location is in a source-generated document.")]
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
   public bool IsGenerated { get; init; }
}
