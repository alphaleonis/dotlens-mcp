using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
namespace DotLens.Tools.Search;

/// <summary>
/// Search for types, methods, properties, etc. by name across the solution.
/// </summary>
[McpServerToolType]
[DotLensWorkerTool]
public class SearchSymbolsTool
{
   private static readonly string[] SuggestedNextTools =
   [
      "get_symbol_info at a result location for detailed info",
      "get_type_members for type results",
      "find_references to see all usages"
   ];

   private readonly IReadOnlyWorkspace _workspace;

   public SearchSymbolsTool(IReadOnlyWorkspace workspace)
   {
      _workspace = workspace;
   }

   /// <summary>
   /// Search for types, methods, properties, etc. by name across the solution.
   /// </summary>
   [McpServerTool(Name = "search_symbols", ReadOnly = true, OpenWorld = false)]
   [Description("Search for symbols by name. Supports glob wildcards (* = any, ? = single char); without wildcards, substring match. Use offset for pagination.")]
   public async Task<ToolResult<PagedResponse<SymbolResponse>>> SearchSymbolsAsync(
      [Description("Search query - glob wildcards (* any chars, ? single char) or substring match. Case-insensitive.")] string query,
      [Description("Filter by symbol kind.")]
      SymbolKindFilter? kind = null,
      [Description("Filter by accessibility level.")]
      AccessibilityDto? accessibility = null,
      [Description("Filter by modifiers (AND logic). E.g. [Static], [Abstract].")]
      ModifierFilter[]? modifiers = null,
      [Description("Filter by namespace (supports wildcards). Examples: 'MyApp.Core.*', '*.Services'.")]
      string? namespaceFilter = null,
      [Description("Limit search to specific project by name.")]
      string? project = null,
      [Description("Maximum results per page.")]
      [PositiveInteger]
      int maxResults = 50,
      [Description("Pagination offset. Use nextOffset from previous response.")]
      [NonNegative]
      int offset = 0,
      [Description("Sort results by name, kind, or file (default). Sorting by name/kind requires collecting all results first.")]
      SymbolSortBy sortBy = SymbolSortBy.Default,
      [Description("Output detail: Summary (name/kind only), Compact (+ location/accessibility), Full (+ FQN/docs).")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      CancellationToken cancellationToken = default)
   {
      var solution = _workspace.EnsureSolutionLoaded();

      // Sorting requires collecting all results first; streaming only works for default/file order
      var requiresSorting = sortBy is SymbolSortBy.Name or SymbolSortBy.Kind;

      if (requiresSorting)
      {
         return await SearchWithSortingAsync(
            solution, query, kind, accessibility, modifiers, namespaceFilter, project,
            maxResults, offset, sortBy, verbosity, cancellationToken);
      }

      // Streaming approach: skip offset, take maxResults + 1 (to determine hasMore)
      // Only format symbols we actually return, avoiding unnecessary allocations
      var results = new List<SymbolResponse>();
      var skipped = 0;
      var hasMore = false;

      await foreach (var symbol in EnumerateMatchingSymbolsAsync(
         solution, query, kind, accessibility, modifiers, namespaceFilter, project, cancellationToken))
      {
         // Skip items before offset
         if (skipped < offset)
         {
            skipped++;
            continue;
         }

         // Check if we've collected enough (maxResults + 1 to detect hasMore)
         if (results.Count >= maxResults)
         {
            hasMore = true;
            break;
         }

         // Format and collect this symbol
         results.Add(RoslynHelpers.CreateSymbolResponse(
            symbol,
            verbosity,
            _workspace.SolutionPath,
            _workspace.UseAbsolutePaths));
      }

      return ToolResult.Create(
         new PagedResponse<SymbolResponse>
         {
            Items = results,
            HasMore = hasMore,
            NextOffset = hasMore ? offset + results.Count : null
         },
         suggestedNextTools: SuggestedNextTools,
         returnedCount: results.Count);
   }

   /// <summary>
   /// Collects matching symbols up to MaxSortableResults, sorts them, then paginates.
   /// Used when sortBy is Name or Kind.
   /// </summary>
   private async Task<ToolResult<PagedResponse<SymbolResponse>>> SearchWithSortingAsync(
      Solution solution,
      string query,
      SymbolKindFilter? kind,
      AccessibilityDto? accessibility,
      ModifierFilter[]? modifiers,
      string? namespaceFilter,
      string? project,
      int maxResults,
      int offset,
      SymbolSortBy sortBy,
      ToolResultVerbosity verbosity,
      CancellationToken cancellationToken)
   {
      // Collect matching symbols up to the configured limit
      var maxSortable = _workspace.MaxSortableResults;
      var allSymbols = new List<ISymbol>();
      var limitExceeded = false;

      await foreach (var symbol in EnumerateMatchingSymbolsAsync(
         solution, query, kind, accessibility, modifiers, namespaceFilter, project, cancellationToken))
      {
         if (allSymbols.Count >= maxSortable)
         {
            limitExceeded = true;
            break;
         }
         allSymbols.Add(symbol);
      }

      // Sort
      var sorted = sortBy switch
      {
         SymbolSortBy.Name => allSymbols.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase),
         SymbolSortBy.Kind => allSymbols.OrderBy(SymbolFilterHelper.GetKindSortOrder).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase),
         _ => allSymbols.AsEnumerable()
      };

      // Paginate and format
      var results = sorted
         .Skip(offset)
         .Take(maxResults)
         .Select(s => RoslynHelpers.CreateSymbolResponse(s, verbosity, _workspace.SolutionPath, _workspace.UseAbsolutePaths))
         .ToList();

      var totalCount = allSymbols.Count;
      var hasMore = offset + results.Count < totalCount;

      // Build warning message if limit was exceeded
      string? warning = limitExceeded
         ? $"Results limited to {maxSortable} symbols for sorting. Use a more specific query or filter to get complete results."
         : null;

      return ToolResult.Create(
         new PagedResponse<SymbolResponse>
         {
            Items = results,
            HasMore = hasMore,
            NextOffset = hasMore ? offset + results.Count : null
         },
         suggestedNextTools: SuggestedNextTools,
         totalCount: totalCount,
         returnedCount: results.Count,
         warning: warning);
   }

   /// <summary>
   /// Yields matching symbols lazily across all projects in the solution.
   /// </summary>
   private static async IAsyncEnumerable<ISymbol> EnumerateMatchingSymbolsAsync(
      Solution solution,
      string query,
      SymbolKindFilter? kind,
      AccessibilityDto? accessibility,
      ModifierFilter[]? modifiers,
      string? namespaceFilter,
      string? project,
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
   {
      bool isGlobPattern = query.Contains('*') || query.Contains('?');
      var queryMatcher = isGlobPattern ? new GlobMatcher(query) : null;
      var namespaceMatcher = new GlobMatcher(namespaceFilter);

      // Filter projects if project parameter specified
      var projects = string.IsNullOrEmpty(project)
         ? solution.Projects
         : solution.Projects.Where(p => p.Name.Equals(project, StringComparison.OrdinalIgnoreCase));

      foreach (var proj in projects)
      {
         cancellationToken.ThrowIfCancellationRequested();
         var compilation = await proj.GetCompilationAsync(cancellationToken);
         if (compilation == null)
            continue;

         var symbols = compilation.GetSymbolsWithName(
            name => queryMatcher != null
               ? queryMatcher.IsMatch(name)
               : name.Contains(query, StringComparison.OrdinalIgnoreCase),
            SymbolFilter.All,
            cancellationToken);

         foreach (var symbol in symbols)
         {
            // Filter by kind
            if (kind.HasValue && !SymbolFilterHelper.MatchesKind(symbol, kind.Value))
               continue;

            // Filter by accessibility
            if (accessibility.HasValue && !SymbolFilterHelper.MatchesAccessibility(symbol.DeclaredAccessibility, accessibility.Value))
               continue;

            // Filter by modifiers
            if (modifiers is { Length: > 0 } && !SymbolFilterHelper.MatchesModifiers(symbol, modifiers))
               continue;

            // Filter by namespace
            if (!string.IsNullOrEmpty(namespaceFilter))
            {
               var symbolNamespace = symbol.ContainingNamespace?.ToDisplayString() ?? "";
               if (!namespaceMatcher.IsMatch(symbolNamespace))
                  continue;
            }

            // Only yield symbols with source locations
            var location = symbol.Locations.FirstOrDefault(loc => loc.IsInSource);
            if (location == null)
               continue;

            yield return symbol;
         }
      }
   }
}
