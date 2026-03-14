using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
namespace DotLens.Tools.Search;

/// <summary>
/// Unified symbol search: find by name, semantic properties, or both.
/// Consolidates search_symbols and semantic_query into one tool.
/// </summary>
[McpServerToolType]
[DotLensWorkerTool]
public class FindSymbolsTool
{
   private static readonly string[] SuggestedNextTools =
   [
      "get_symbol_info for detailed info",
      "get_type_members for type structure",
      "find_references to see usages"
   ];

   private readonly IReadOnlyWorkspace _workspace;

   public FindSymbolsTool(IReadOnlyWorkspace workspace)
   {
      _workspace = workspace;
   }

   [McpServerTool(Name = "find_symbols", ReadOnly = true, OpenWorld = false, Idempotent = true)]
   [Description("Find symbols by name and/or semantic filters (AND logic). Name supports glob wildcards (* = any, ? = single char); without wildcards, exact match.")]
   public async Task<ToolResult<PagedResponse<SymbolResponse>>> FindSymbolsAsync(
      [Description("Name pattern - glob wildcards (* any chars, ? single char). Without wildcards, exact match. Case-insensitive.")]
      string? name = null,
      [Description("Filter by symbol kinds (OR within array).")]
      SymbolKindFilter[]? kinds = null,
      [Description("Filter by accessibility level.")]
      AccessibilityDto? accessibility = null,
      [Description("Filter by modifiers (AND logic). E.g. [Static], [Abstract].")]
      ModifierFilter[]? modifiers = null,
      [Description("Filter by namespace (supports * wildcard).")]
      string? namespaceFilter = null,
      [Description("Limit search to specific project by name.")]
      string? project = null,
      [Description("Filter fields/properties by type (partial match).")]
      string? type = null,
      [Description("Filter methods by return type (partial match).")]
      string? returnType = null,
      [Description("Filter by attributes (must have ALL specified).")]
      string[]? attributes = null,
      [Description("Filter methods that MUST have these parameter types.")]
      string[]? parameterIncludes = null,
      [Description("Filter methods that must NOT have these parameter types.")]
      string[]? parameterExcludes = null,
      [Description("Sort by name, kind, or default (file order). Sorting requires collecting results first.")]
      SymbolSortBy sortBy = SymbolSortBy.Default,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      [PositiveInteger]
      [Description("Maximum results to return.")]
      int maxResults = 50,
      [Description("Skip this many results (for pagination).")]
      [NonNegative]
      int offset = 0,
      CancellationToken cancellationToken = default)
   {
      var solution = _workspace.EnsureSolutionLoaded();

      // Build filter criteria
      var criteria = new FilterCriteria
      {
         Name = name,
         Kinds = kinds,
         Accessibility = accessibility,
         Modifiers = modifiers,
         NamespaceFilter = namespaceFilter,
         Type = type,
         ReturnType = returnType,
         Attributes = attributes,
         ParameterIncludes = parameterIncludes,
         ParameterExcludes = parameterExcludes
      };

      // Require at least one filter to avoid enumerating entire solution
      if (!criteria.HasAnyFilter())
      {
         throw new ToolException(ErrorCodes.InvalidParameter, "At least one filter must be specified (name, kinds, accessibility, modifiers, namespaceFilter, type, returnType, attributes, parameterIncludes, or parameterExcludes).");
      }

      // Sorting requires collecting all results first
      var requiresSorting = sortBy is SymbolSortBy.Name or SymbolSortBy.Kind;

      if (requiresSorting)
      {
         return await SearchWithSortingAsync(solution, project, criteria, sortBy, verbosity, maxResults, offset, cancellationToken);
      }

      // Streaming approach: collect up to maxResults, check for hasMore
      var results = new List<SymbolResponse>();
      var skipped = 0;
      var hasMore = false;

      await foreach (var symbol in EnumerateMatchingSymbolsAsync(solution, project, criteria, cancellationToken))
      {
         if (skipped < offset)
         {
            skipped++;
            continue;
         }

         if (results.Count >= maxResults)
         {
            hasMore = true;
            break;
         }

         results.Add(RoslynHelpers.CreateSymbolResponse(symbol, verbosity, _workspace.SolutionPath, _workspace.UseAbsolutePaths));
      }

      return ToolResult.Create(
         new PagedResponse<SymbolResponse>
         {
            Items = results,
            HasMore = hasMore,
            NextOffset = hasMore ? offset + results.Count : null
         },
         SuggestedNextTools,
         returnedCount: results.Count);
   }

   private async Task<ToolResult<PagedResponse<SymbolResponse>>> SearchWithSortingAsync(
      Solution solution,
      string? project,
      FilterCriteria criteria,
      SymbolSortBy sortBy,
      ToolResultVerbosity verbosity,
      int maxResults,
      int offset,
      CancellationToken cancellationToken)
   {
      var maxSortable = _workspace.MaxSortableResults;
      var allSymbols = new List<ISymbol>();
      var limitExceeded = false;

      await foreach (var symbol in EnumerateMatchingSymbolsAsync(solution, project, criteria, cancellationToken))
      {
         if (allSymbols.Count >= maxSortable)
         {
            limitExceeded = true;
            break;
         }
         allSymbols.Add(symbol);
      }

      var sorted = sortBy switch
      {
         SymbolSortBy.Name => allSymbols.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase),
         SymbolSortBy.Kind => allSymbols.OrderBy(SymbolFilterHelper.GetKindSortOrder).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase),
         _ => allSymbols.AsEnumerable()
      };

      var results = sorted
         .Skip(offset)
         .Take(maxResults)
         .Select(s => RoslynHelpers.CreateSymbolResponse(s, verbosity, _workspace.SolutionPath, _workspace.UseAbsolutePaths))
         .ToList();

      var totalCount = allSymbols.Count;
      var hasMore = offset + results.Count < totalCount;

      string? warning = limitExceeded
         ? $"Results limited to {maxSortable} symbols for sorting. Use a more specific query or filter."
         : null;

      return ToolResult.Create(
         new PagedResponse<SymbolResponse>
         {
            Items = results,
            HasMore = hasMore,
            NextOffset = hasMore ? offset + results.Count : null
         },
         SuggestedNextTools,
         totalCount: totalCount,
         returnedCount: results.Count,
         warning: warning);
   }

   private static async IAsyncEnumerable<ISymbol> EnumerateMatchingSymbolsAsync(
      Solution solution,
      string? projectFilter,
      FilterCriteria criteria,
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
   {
      var nameMatcher = !string.IsNullOrEmpty(criteria.Name) ? new GlobMatcher(criteria.Name) : null;
      var namespaceMatcher = new GlobMatcher(criteria.NamespaceFilter);
      var needsConstructors = criteria.Kinds is null || criteria.Kinds.Contains(SymbolKindFilter.Constructor);

      // Filter projects if specified
      var projects = string.IsNullOrEmpty(projectFilter)
         ? solution.Projects
         : solution.Projects.Where(p => p.Name.Equals(projectFilter, StringComparison.OrdinalIgnoreCase));

      foreach (var project in projects)
      {
         cancellationToken.ThrowIfCancellationRequested();

         var compilation = await project.GetCompilationAsync(cancellationToken);
         if (compilation == null)
            continue;

         // Use efficient name predicate when name filter is provided
         Func<string, bool> namePredicate = nameMatcher != null
            ? nameMatcher.IsMatch
            : _ => true;

         var symbols = compilation.GetSymbolsWithName(namePredicate, SymbolFilter.All, cancellationToken);

         foreach (var symbol in symbols)
         {
            // For types, also enumerate constructors (they aren't returned by GetSymbolsWithName)
            if (needsConstructors && symbol is INamedTypeSymbol namedType)
            {
               foreach (var ctor in namedType.Constructors)
               {
                  if (MatchesAllFilters(ctor, criteria, namespaceMatcher))
                     yield return ctor;
               }
            }

            if (MatchesAllFilters(symbol, criteria, namespaceMatcher))
               yield return symbol;
         }
      }
   }

   private static bool MatchesAllFilters(ISymbol symbol, FilterCriteria criteria, GlobMatcher namespaceMatcher)
   {
      // Skip compiler-generated and non-source symbols
      if (symbol.IsImplicitlyDeclared)
         return false;

      var sourceLocation = symbol.Locations.FirstOrDefault(loc => loc.IsInSource);
      if (sourceLocation == null)
         return false;

      // Filter by kind
      if (criteria.Kinds is { Length: > 0 } && !SymbolFilterHelper.MatchesAnyKind(symbol, criteria.Kinds))
         return false;

      // Filter by namespace
      if (!string.IsNullOrEmpty(criteria.NamespaceFilter))
      {
         var symbolNamespace = symbol.ContainingNamespace?.ToDisplayString() ?? "";
         if (!namespaceMatcher.IsMatch(symbolNamespace))
            return false;
      }

      // Filter by accessibility
      if (criteria.Accessibility.HasValue && !SymbolFilterHelper.MatchesAccessibility(symbol.DeclaredAccessibility, criteria.Accessibility.Value))
         return false;

      // Filter by modifiers
      if (criteria.Modifiers is { Length: > 0 } && !SymbolFilterHelper.MatchesModifiers(symbol, criteria.Modifiers))
         return false;

      // Filter by attributes
      if (criteria.Attributes is { Length: > 0 })
      {
         var symbolAttributes = symbol.GetAttributes();
         bool hasAllAttributes = criteria.Attributes.All(attrName =>
            symbolAttributes.Any(attr =>
               attr.AttributeClass?.Name.Equals(attrName, StringComparison.OrdinalIgnoreCase) == true ||
               attr.AttributeClass?.Name.Equals(attrName + "Attribute", StringComparison.OrdinalIgnoreCase) == true));

         if (!hasAllAttributes)
            return false;
      }

      // Method-specific filters
      if (symbol is IMethodSymbol methodSymbol)
      {
         if (!string.IsNullOrEmpty(criteria.ReturnType) &&
            !methodSymbol.ReturnType.ToDisplayString().Contains(criteria.ReturnType, StringComparison.OrdinalIgnoreCase))
            return false;

         if ((criteria.ParameterIncludes is { Length: > 0 }) || (criteria.ParameterExcludes is { Length: > 0 }))
         {
            var paramTypes = methodSymbol.Parameters.Select(p => p.Type.ToDisplayString()).ToArray();

            if (criteria.ParameterIncludes is { Length: > 0 })
            {
               bool hasAllIncludes = criteria.ParameterIncludes.All(include =>
                  paramTypes.Any(pt => pt.Contains(include, StringComparison.OrdinalIgnoreCase)));

               if (!hasAllIncludes)
                  return false;
            }

            if (criteria.ParameterExcludes is { Length: > 0 })
            {
               bool hasAnyExclude = criteria.ParameterExcludes.Any(exclude =>
                  paramTypes.Any(pt => pt.Contains(exclude, StringComparison.OrdinalIgnoreCase)));

               if (hasAnyExclude)
                  return false;
            }
         }
      }
      else if (symbol is IPropertySymbol propertySymbol)
      {
         if (!string.IsNullOrEmpty(criteria.Type) &&
            !propertySymbol.Type.ToDisplayString().Contains(criteria.Type, StringComparison.OrdinalIgnoreCase))
            return false;
      }
      else if (symbol is IFieldSymbol fieldSymbol)
      {
         if (!string.IsNullOrEmpty(criteria.Type) &&
            !fieldSymbol.Type.ToDisplayString().Contains(criteria.Type, StringComparison.OrdinalIgnoreCase))
            return false;
      }

      return true;
   }

   /// <summary>
   /// Encapsulates all filter criteria for symbol matching.
   /// </summary>
   private sealed record FilterCriteria
   {
      public string? Name { get; init; }
      public SymbolKindFilter[]? Kinds { get; init; }
      public AccessibilityDto? Accessibility { get; init; }
      public ModifierFilter[]? Modifiers { get; init; }
      public string? NamespaceFilter { get; init; }
      public string? Type { get; init; }
      public string? ReturnType { get; init; }
      public string[]? Attributes { get; init; }
      public string[]? ParameterIncludes { get; init; }
      public string[]? ParameterExcludes { get; init; }

      public bool HasAnyFilter() =>
         !string.IsNullOrEmpty(Name) ||
         Kinds is { Length: > 0 } ||
         Accessibility.HasValue ||
         Modifiers is { Length: > 0 } ||
         !string.IsNullOrEmpty(NamespaceFilter) ||
         !string.IsNullOrEmpty(Type) ||
         !string.IsNullOrEmpty(ReturnType) ||
         Attributes is { Length: > 0 } ||
         ParameterIncludes is { Length: > 0 } ||
         ParameterExcludes is { Length: > 0 };
   }
}
