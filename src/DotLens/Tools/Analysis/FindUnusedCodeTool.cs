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
   [Description("""
      Find unused types, methods, properties, and fields. Entry points, module initializers and members that a framework calls count as used. Any other symbol is used when code that runs references it from outside its own declaration; a static class, also when such code uses one of its members.
      Code runs when it is used, or when it is a root, so private and internal members that only reference each other are unused. Roots: symbols visible outside their assembly (internal ones too when InternalsVisibleTo names an assembly outside the solution), and virtual, abstract and interface members. An override or interface implementation runs when its type is used or when code that runs calls the member it overrides or implements, so an implementation on a used type is not reported even when nothing calls it.
      Members that a framework calls: xUnit, NUnit and MSTest tests and setup methods; BenchmarkDotNet benchmarks; ASP.NET Core controller actions, Razor Pages handlers and SignalR hub methods; Blazor parameters and [JSInvokable] methods; serializer constructors and members of System.Text.Json, Newtonsoft.Json and DataContract; MEF exports and imports; [UnmanagedCallersOnly] methods. A type that holds such a member is used, and so are the constructors of a type that a framework creates.
      Constructors, middleware Invoke/InvokeAsync and startup Configure methods of a type count as referenced wherever code evaluates a typeof of a type that mentions it, or binds to a generic method, or a member, constructor or attribute of a generic type, with the type anywhere in the type arguments written for it, as in a DI registration or a target-typed new(). A use of the type inside an argument of a generic call, and declarations that only name the type, do not count. A member that MemberData, TestCaseSource, TestFixtureSource, ValueSource, DynamicData, ParamsSource or ArgumentsSource names in a string counts as referenced by the attributed member.
      Slow on large solutions; use projectName filter for performance.
      """)]
   public async Task<ToolResult<FindUnusedCodeResponse>> FindUnusedCodeAsync(
      [Description("Analyze specific project by name. Recommended for performance.")]
      string? projectName = null,
      [Description("Include private members (default: true).")]
      bool includePrivate = true,
      [Description("Include internal members (default: false).")]
      bool includeInternal = false,
      [Description("Filter by symbol kind. Extension blocks are never reported, so extension is an error; use method or property for their members.")]
      SymbolKindFilter? symbolKindFilter = null,
      [Description("Filter by modifiers (AND logic: symbol must have ALL specified). Examples: [Static], [Abstract].")]
      ModifierFilter[]? modifiers = null,
      [Description("More attributes that mark a member or type as called by a framework, so it counts as used. Each is a full name or a simple name, with or without the Attribute suffix.")]
      string[]? entryPointAttributes = null,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      [Description("Maximum results to return.")]
      [PositiveInteger]
      int maxResults = 50,
      CancellationToken cancellationToken = default)
   {
      if (symbolKindFilter == SymbolKindFilter.Extension)
      {
         throw new ToolException(
            ErrorCodes.InvalidParameter,
            "find_unused_code reports no extension blocks, only their unused members.",
            "Filter by method or property to find the unused members of extension blocks.");
      }

      var solution = _workspaceManager.EnsureSolutionLoaded();
      var usage = new UsageAnalysis(solution, new FrameworkEntryPoints(entryPointAttributes));
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
               compilation, solution, usage, symbolKindFilter, modifiers,
               includePrivate, includeInternal,
               unusedSymbols, countByKind, maxResults, verbosity,
               cancellationToken);
         }

         // Analyze members if no filter or filter matches member kinds
         if (ShouldAnalyzeMembers(symbolKindFilter) && unusedSymbols.Count < maxResults)
         {
            await AnalyzeMembersAsync(
               compilation, solution, usage, symbolKindFilter, modifiers,
               includePrivate, includeInternal,
               unusedSymbols, countByKind, maxResults, verbosity,
               cancellationToken);
         }

         // Analyze constructors separately (GetSymbolsWithName doesn't return constructors)
         if (ShouldAnalyzeConstructors(symbolKindFilter) && unusedSymbols.Count < maxResults)
         {
            await AnalyzeConstructorsAsync(
               compilation, solution, usage, symbolKindFilter, modifiers,
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
      UsageAnalysis usage,
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

         if (!await usage.IsUsedAsync(typeSymbol, cancellationToken))
         {
            // Use actual TypeKind, not SymbolKindDto (which incorrectly treats Record as separate kind)
            var kindName = typeSymbol.TypeKind.ToString();
            countByKind[kindName] = countByKind.GetValueOrDefault(kindName) + 1;

            unusedSymbols.Add(await CreateSymbolResponseAsync(typeSymbol, solution, verbosity, cancellationToken));
         }
      }
   }

   private async Task AnalyzeMembersAsync(
      Compilation compilation,
      Solution solution,
      UsageAnalysis usage,
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

         if (!await usage.IsUsedAsync(member, cancellationToken))
         {
            // Use SymbolKind directly for members (Method, Property, Field, Event)
            var kindName = member.Kind.ToString();
            countByKind[kindName] = countByKind.GetValueOrDefault(kindName) + 1;

            unusedSymbols.Add(await CreateSymbolResponseAsync(member, solution, verbosity, cancellationToken));
         }
      }
   }

   private async Task AnalyzeConstructorsAsync(
      Compilation compilation,
      Solution solution,
      UsageAnalysis usage,
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

            if (!await usage.IsUsedAsync(ctor, cancellationToken))
            {
               const string kindName = "Constructor";
               countByKind[kindName] = countByKind.GetValueOrDefault(kindName) + 1;

               unusedSymbols.Add(await CreateSymbolResponseAsync(ctor, solution, verbosity, cancellationToken));
            }
         }
      }
   }

   private Task<SymbolResponse> CreateSymbolResponseAsync(
      ISymbol symbol, Solution solution, ToolResultVerbosity verbosity, CancellationToken cancellationToken)
   {
      return RoslynHelpers.CreateSymbolResponseAsync(
         symbol, verbosity, solution, _workspaceManager.SolutionPath, _workspaceManager.UseAbsolutePaths, cancellationToken);
   }
}
