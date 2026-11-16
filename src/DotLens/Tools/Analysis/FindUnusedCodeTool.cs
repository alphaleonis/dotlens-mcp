using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
namespace DotLens.Tools.Analysis;

#region Result Types

/// <summary>
/// Result from FindUnusedCode tool.
/// </summary>
[Description("Unused symbols and counts.")]
public record FindUnusedCodeResponse
{
   [Description("Project analyzed, or 'All projects' for solution-wide analysis.")]
   public required string ProjectName { get; init; }

   [Description("Count of unused symbols grouped by kind (e.g., 'Class': 2, 'Method': 5).")]
   public required IReadOnlyDictionary<string, int> CountByKind { get; init; }

   [Description("List of unused symbols with details based on verbosity level.")]
   public required IReadOnlyList<SymbolResponse> UnusedSymbols { get; init; }
}

#endregion

/// <summary>
/// Find unused types, methods, properties, and fields.
/// </summary>
[McpServerToolType]
[DotLensWorkerTool]
public class FindUnusedCodeTool
{
   private static readonly string[] SuggestedNextToolsWithUnused = ["find_references to verify symbol is truly unused"];
   private static readonly string[] SuggestedNextToolsNoUnused = [];

   private readonly IReadOnlyWorkspace _workspaceManager;

   public FindUnusedCodeTool(IReadOnlyWorkspace workspaceManager)
   {
      _workspaceManager = workspaceManager;
   }

   [McpServerTool(Name = "find_unused_code", ReadOnly = true, OpenWorld = false)]
   [Description("Find unused types, methods, properties, and fields (zero references). Slow on large solutions; use projectName filter for performance.")]
   public async Task<ToolResult<FindUnusedCodeResponse>> FindUnusedCodeAsync(
      [Description("Analyze specific project by name. Recommended for performance.")]
      string? projectName = null,
      [Description("Include private members (default: true).")]
      bool includePrivate = true,
      [Description("Include internal members (default: false).")]
      bool includeInternal = false,
      [Description("Filter by symbol kind.")]
      SymbolKindFilter? symbolKindFilter = null,
      [Description("Filter by modifiers (AND logic: symbol must have ALL specified). Examples: [Static], [Abstract].")]
      ModifierFilter[]? modifiers = null,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      [Description("Maximum results to return.")]
      [PositiveInteger]
      int maxResults = 50,
      CancellationToken cancellationToken = default)
   {
      var solution = _workspaceManager.EnsureSolutionLoaded();
      var unusedSymbols = new List<SymbolResponse>();
      var countByKind = new Dictionary<string, int>();

      var projectsToAnalyze = string.IsNullOrEmpty(projectName)
         ? solution.Projects
         : solution.Projects.Where(p => p.Name == projectName);

      foreach (var project in projectsToAnalyze)
      {
         cancellationToken.ThrowIfCancellationRequested();
         if (unusedSymbols.Count >= maxResults)
            break;

         var compilation = await project.GetCompilationAsync(cancellationToken);
         if (compilation == null)
            continue;

         // Analyze types if no filter or filter matches type kinds
         if (ShouldAnalyzeTypes(symbolKindFilter))
         {
            await AnalyzeTypesAsync(
               compilation, solution, symbolKindFilter, modifiers,
               includePrivate, includeInternal,
               unusedSymbols, countByKind, maxResults, verbosity,
               cancellationToken);
         }

         // Analyze members if no filter or filter matches member kinds
         if (ShouldAnalyzeMembers(symbolKindFilter) && unusedSymbols.Count < maxResults)
         {
            await AnalyzeMembersAsync(
               compilation, solution, symbolKindFilter, modifiers,
               includePrivate, includeInternal,
               unusedSymbols, countByKind, maxResults, verbosity,
               cancellationToken);
         }

         // Analyze constructors separately (GetSymbolsWithName doesn't return constructors)
         if (ShouldAnalyzeConstructors(symbolKindFilter) && unusedSymbols.Count < maxResults)
         {
            await AnalyzeConstructorsAsync(
               compilation, solution, symbolKindFilter, modifiers,
               includePrivate, includeInternal,
               unusedSymbols, countByKind, maxResults, verbosity,
               cancellationToken);
         }
      }

      return ToolResult.Create(
         new FindUnusedCodeResponse
         {
            ProjectName = projectName ?? "All projects",
            CountByKind = countByKind,
            UnusedSymbols = unusedSymbols
         },
         unusedSymbols.Count > 0
            ? SuggestedNextToolsWithUnused
            : SuggestedNextToolsNoUnused);
   }

   private static bool ShouldAnalyzeTypes(SymbolKindFilter? filter)
   {
      return filter is null or
         SymbolKindFilter.Class or
         SymbolKindFilter.Interface or
         SymbolKindFilter.Struct or
         SymbolKindFilter.Enum or
         SymbolKindFilter.Delegate;
   }

   private static bool ShouldAnalyzeMembers(SymbolKindFilter? filter)
   {
      // Note: Constructor is handled separately in AnalyzeConstructorsAsync
      // because GetSymbolsWithName doesn't return constructors
      return filter is null or
         SymbolKindFilter.Method or
         SymbolKindFilter.Property or
         SymbolKindFilter.Field or
         SymbolKindFilter.Event;
   }

   private static bool ShouldAnalyzeConstructors(SymbolKindFilter? filter)
   {
      return filter is null or SymbolKindFilter.Constructor;
   }

   private async Task AnalyzeTypesAsync(
      Compilation compilation,
      Solution solution,
      SymbolKindFilter? kindFilter,
      ModifierFilter[]? modifiers,
      bool includePrivate,
      bool includeInternal,
      List<SymbolResponse> unusedSymbols,
      Dictionary<string, int> countByKind,
      int maxResults,
      ToolResultVerbosity verbosity,
      CancellationToken cancellationToken)
   {
      var allTypes = compilation.GetSymbolsWithName(_ => true, SymbolFilter.Type, cancellationToken)
         .OfType<INamedTypeSymbol>();

      foreach (var typeSymbol in allTypes)
      {
         if (unusedSymbols.Count >= maxResults)
            break;

         if (!RoslynHelpers.IsExplicitSourceSymbol(typeSymbol))
            continue;

         // An extension block only groups members, so AnalyzeMembersAsync reports its unused members instead.
         if (typeSymbol.IsExtension)
            continue;

         // Filter by specific kind if requested
         if (kindFilter != null && !SymbolFilterHelper.MatchesKind(typeSymbol, kindFilter.Value))
            continue;

         // Filter by modifiers
         if (modifiers is { Length: > 0 } && !SymbolFilterHelper.MatchesModifiers(typeSymbol, modifiers))
            continue;

         if (!includePrivate && typeSymbol.DeclaredAccessibility == Accessibility.Private)
            continue;
         if (!includeInternal && typeSymbol.DeclaredAccessibility == Accessibility.Internal)
            continue;

         var references = await Microsoft.CodeAnalysis.FindSymbols.SymbolFinder
            .FindReferencesAsync(typeSymbol, solution, cancellationToken);
         var referenceCount = references.SelectMany(r => r.Locations).Count();

         if (referenceCount == 0)
         {
            // Use actual TypeKind, not SymbolKindDto (which incorrectly treats Record as separate kind)
            var kindName = typeSymbol.TypeKind.ToString();
            countByKind[kindName] = countByKind.GetValueOrDefault(kindName) + 1;

            unusedSymbols.Add(CreateSymbolResponse(typeSymbol, verbosity));
         }
      }
   }

   private async Task AnalyzeMembersAsync(
      Compilation compilation,
      Solution solution,
      SymbolKindFilter? kindFilter,
      ModifierFilter[]? modifiers,
      bool includePrivate,
      bool includeInternal,
      List<SymbolResponse> unusedSymbols,
      Dictionary<string, int> countByKind,
      int maxResults,
      ToolResultVerbosity verbosity,
      CancellationToken cancellationToken)
   {
      var allMembers = compilation.GetSymbolsWithName(_ => true, SymbolFilter.Member, cancellationToken);

      foreach (var member in allMembers)
      {
         if (unusedSymbols.Count >= maxResults)
            break;

         if (member is not (IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol))
            continue;

         if (!RoslynHelpers.IsExplicitSourceSymbol(member))
            continue;

         // Filter by specific kind if requested
         if (kindFilter != null && !SymbolFilterHelper.MatchesKind(member, kindFilter.Value))
            continue;

         // Filter by modifiers
         if (modifiers is { Length: > 0 } && !SymbolFilterHelper.MatchesModifiers(member, modifiers))
            continue;

         // Skip non-ordinary methods (constructors, operators, etc.) except explicit interface implementations.
         // Constructors are handled separately in AnalyzeConstructorsAsync.
         // Override/virtual methods are excluded because they are semantically required by
         // inheritance contracts and cannot be considered "unused".
         if (member is IMethodSymbol method &&
            (method.MethodKind is not (MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation)
             || method.IsOverride || method.IsVirtual))
            continue;

         if (!includePrivate && member.DeclaredAccessibility == Accessibility.Private)
            continue;
         if (!includeInternal && member.DeclaredAccessibility == Accessibility.Internal)
            continue;

         var references = await Microsoft.CodeAnalysis.FindSymbols.SymbolFinder
            .FindReferencesAsync(member, solution, cancellationToken);
         var referenceCount = references.SelectMany(r => r.Locations).Count();

         if (referenceCount == 0)
         {
            // Use SymbolKind directly for members (Method, Property, Field, Event)
            var kindName = member.Kind.ToString();
            countByKind[kindName] = countByKind.GetValueOrDefault(kindName) + 1;

            unusedSymbols.Add(CreateSymbolResponse(member, verbosity));
         }
      }
   }

   private async Task AnalyzeConstructorsAsync(
      Compilation compilation,
      Solution solution,
      SymbolKindFilter? kindFilter,
      ModifierFilter[]? modifiers,
      bool includePrivate,
      bool includeInternal,
      List<SymbolResponse> unusedSymbols,
      Dictionary<string, int> countByKind,
      int maxResults,
      ToolResultVerbosity verbosity,
      CancellationToken cancellationToken)
   {
      // GetSymbolsWithName doesn't return constructors, so we need to iterate over types
      var allTypes = compilation.GetSymbolsWithName(_ => true, SymbolFilter.Type, cancellationToken);

      foreach (var type in allTypes.OfType<INamedTypeSymbol>())
      {
         if (unusedSymbols.Count >= maxResults)
            break;

         // Get constructors from this type
         // Constructors property contains instance constructors; StaticConstructors contains static ones.
         var allCtors = type.Constructors.Concat(type.StaticConstructors).Distinct(SymbolEqualityComparer.Default);
         foreach (var ctor in allCtors.Cast<IMethodSymbol>())
         {
            if (unusedSymbols.Count >= maxResults)
               break;

            if (!RoslynHelpers.IsExplicitSourceSymbol(ctor))
               continue;

            // Filter by kind if specified (this handles Constructor vs StaticConstructor)
            if (kindFilter != null && !SymbolFilterHelper.MatchesKind(ctor, kindFilter.Value))
               continue;

            // Filter by modifiers
            if (modifiers is { Length: > 0 } && !SymbolFilterHelper.MatchesModifiers(ctor, modifiers))
               continue;

            if (!includePrivate && ctor.DeclaredAccessibility == Accessibility.Private)
               continue;
            if (!includeInternal && ctor.DeclaredAccessibility == Accessibility.Internal)
               continue;

            // Static constructors are implicitly invoked when the type is first accessed.
            // They are never explicitly called, so FindReferencesAsync won't find direct references.
            // A static constructor is "unused" only if its containing type is unused.
            bool isUnused;
            if (ctor.MethodKind == MethodKind.StaticConstructor)
            {
               var typeReferences = await Microsoft.CodeAnalysis.FindSymbols.SymbolFinder
                  .FindReferencesAsync(type, solution, cancellationToken);
               var typeReferenceCount = typeReferences.SelectMany(r => r.Locations).Count();
               isUnused = typeReferenceCount == 0;
            }
            else
            {
               var references = await Microsoft.CodeAnalysis.FindSymbols.SymbolFinder
                  .FindReferencesAsync(ctor, solution, cancellationToken);
               var referenceCount = references.SelectMany(r => r.Locations).Count();
               isUnused = referenceCount == 0;
            }

            if (isUnused)
            {
               const string kindName = "Constructor";
               countByKind[kindName] = countByKind.GetValueOrDefault(kindName) + 1;

               unusedSymbols.Add(CreateSymbolResponse(ctor, verbosity));
            }
         }
      }
   }

   private SymbolResponse CreateSymbolResponse(ISymbol symbol, ToolResultVerbosity verbosity)
   {
      return RoslynHelpers.CreateSymbolResponse(
         symbol, verbosity, _workspaceManager.SolutionPath, _workspaceManager.UseAbsolutePaths);
   }
}
