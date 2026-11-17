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

   private const string ModuleInitializerAttributeName = "System.Runtime.CompilerServices.ModuleInitializerAttribute";

   private readonly IReadOnlyWorkspace _workspaceManager;

   public FindUnusedCodeTool(IReadOnlyWorkspace workspaceManager)
   {
      _workspaceManager = workspaceManager;
   }

   [McpServerTool(Name = "find_unused_code", ReadOnly = true, OpenWorld = false)]
   [Description("Find unused types, methods, properties, and fields. A member is unused when no code outside its own declaration references it; a type, when no code outside it names it or, for a static class, uses its members. Entry points and module initializers count as used. Slow on large solutions; use projectName filter for performance.")]
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
      var entryPoint = compilation.GetEntryPoint(cancellationToken);

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

         if (!await IsTypeUsedAsync(typeSymbol, entryPoint, solution, cancellationToken))
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
      var entryPoint = compilation.GetEntryPoint(cancellationToken);

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

         if (member is IMethodSymbol runtimeCalled && IsEntryPoint(runtimeCalled, entryPoint))
            continue;

         if (!includePrivate && member.DeclaredAccessibility == Accessibility.Private)
            continue;
         if (!includeInternal && member.DeclaredAccessibility == Accessibility.Internal)
            continue;

         if (!await IsUsedOutsideAsync(member, member, solution, cancellationToken))
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
      var entryPoint = compilation.GetEntryPoint(cancellationToken);

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
               isUnused = !await IsTypeUsedAsync(type, entryPoint, solution, cancellationToken);
            }
            else
            {
               isUnused = !await IsUsedOutsideAsync(ctor, ctor, solution, cancellationToken);
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

   /// <summary>
   /// Returns whether the type is used: it holds an entry point, code outside it names it, or it is a static class and
   /// code outside it uses one of its members.
   /// </summary>
   /// <remarks>
   /// Code can call an extension member without naming its static class.
   /// </remarks>
   private static async Task<bool> IsTypeUsedAsync(
      INamedTypeSymbol type, IMethodSymbol? entryPoint, Solution solution, CancellationToken cancellationToken)
   {
      if (type.GetMembers().OfType<IMethodSymbol>().Any(method => IsEntryPoint(method, entryPoint)))
         return true;

      if (await IsUsedOutsideAsync(type, type, solution, cancellationToken))
         return true;

      if (!type.IsStatic)
         return false;

      var members = type.GetMembers()
         .Where(member => member is not INamedTypeSymbol)
         .Concat(type.GetTypeMembers().Where(nested => nested.IsExtension).SelectMany(block => block.GetMembers()))
         .Where(RoslynHelpers.IsExplicitSourceSymbol);
      foreach (var member in members)
      {
         if (await IsUsedOutsideAsync(member, type, solution, cancellationToken))
            return true;
      }

      return false;
   }

   /// <summary>
   /// Returns whether the runtime calls the method without a reference in code: the compilation's entry point, or a
   /// module initializer.
   /// </summary>
   private static bool IsEntryPoint(IMethodSymbol method, IMethodSymbol? compilationEntryPoint) =>
      SymbolEqualityComparer.Default.Equals(method, compilationEntryPoint)
      || method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == ModuleInitializerAttributeName);

   /// <summary>
   /// Returns whether a reference to <paramref name="symbol"/> lies outside the declarations of <paramref name="owner"/>.
   /// </summary>
   private static async Task<bool> IsUsedOutsideAsync(
      ISymbol symbol, ISymbol owner, Solution solution, CancellationToken cancellationToken)
   {
      var references = await ReferenceSearch.FindReferencesAsync(symbol, solution, cancellationToken);
      return references.Any(reference => !IsWithin(owner, reference.Location, cancellationToken));
   }

   /// <summary>
   /// Returns whether the location lies in one of the symbol's own declarations, its doc comment included.
   /// </summary>
   /// <remarks>
   /// A file compiled into several projects, as in a multi-targeted project, has one syntax tree in each, and
   /// find-references returns locations in all of them. So the files match by path.
   /// </remarks>
   private static bool IsWithin(ISymbol symbol, Location location, CancellationToken cancellationToken) =>
      location.SourceTree is { } tree
      && symbol.DeclaringSyntaxReferences.Any(declaration =>
         IsSameFile(declaration.SyntaxTree, tree)
         && OwnSpans(symbol, declaration.GetSyntax(cancellationToken)).Any(span => span.Contains(location.SourceSpan)));

   /// <summary>
   /// Returns the spans of a declaration that hold the symbol's own text.
   /// </summary>
   /// <remarks>
   /// One field declaration can declare several fields, so each field owns its declarator and the shared doc comment,
   /// attributes and modifiers.
   /// A type declares its primary constructor, which owns only the parameter list.
   /// </remarks>
   private static TextSpan[] OwnSpans(ISymbol symbol, SyntaxNode declaration) => declaration switch
   {
      VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax field } =>
         [declaration.FullSpan, TextSpan.FromBounds(field.FullSpan.Start, field.Declaration.SpanStart)],
      TypeDeclarationSyntax { ParameterList: { } parameters } when symbol is IMethodSymbol => [parameters.FullSpan],
      _ => [declaration.FullSpan],
   };

   private static bool IsSameFile(SyntaxTree first, SyntaxTree second) =>
      first == second
      || (first.FilePath.Length > 0 && string.Equals(first.FilePath, second.FilePath, PathUtils.PathComparison));

   private SymbolResponse CreateSymbolResponse(ISymbol symbol, ToolResultVerbosity verbosity)
   {
      return RoslynHelpers.CreateSymbolResponse(
         symbol, verbosity, _workspaceManager.SolutionPath, _workspaceManager.UseAbsolutePaths);
   }
}
