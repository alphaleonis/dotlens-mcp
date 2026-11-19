using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.FindSymbols;

namespace DotLens.Tools.Shared;

/// <summary>
/// A reference that find-references reports in source: the symbol it refers to and where.
/// </summary>
/// <param name="Definition">The searched symbol, or a symbol that find-references cascades to.</param>
/// <param name="Document">The document that holds the reference.</param>
/// <param name="Location">The reference's location in <paramref name="Document"/>.</param>
internal sealed record SourceReference(ISymbol Definition, Document Document, Location Location);

/// <summary>
/// The code that refers to a symbol from inside one method, property, field or event.
/// </summary>
/// <param name="CallingSymbol">The method, property, field or event that holds the references, or null for top-level
/// statements.</param>
/// <param name="Locations">The references in source, in source order.</param>
internal sealed record SymbolCallers(ISymbol? CallingSymbol, List<Location> Locations);

/// <summary>
/// Finds references and callers with <see cref="SymbolFinder"/>, with its indexer references in null-conditional
/// accesses replaced by a scan of the element bindings.
/// </summary>
/// <remarks>
/// <see cref="SymbolFinder"/> reports a use in a file that several projects compile once for each project and each
/// linked copy of the symbol. Both searches list such a use once, matched by <see cref="SiteComparer"/>.
/// <para>
/// <see cref="SymbolFinder"/> matches an indexer against the whole access after the <c>?</c> and reports it at that
/// access's start. So it misses the indexer in <c>a?[0] = v</c> and <c>a?[0].M()</c>, and reports the indexer of
/// <c>[1]</c> at the <c>[0]</c> of <c>a?[0][1]</c>. See <see href="https://github.com/dotnet/roslyn/blob/1324e1d34a0490454837a22370e75bbaeecffb87/src/Workspaces/Core/Portable/FindSymbols/FindReferences/Finders/PropertySymbolReferenceFinder.cs#L269-L286">ComputeConditionalAccessInformation</see>.
/// </para>
/// </remarks>
internal static class ReferenceSearch
{
   /// <summary>
   /// Returns the references in source to <paramref name="symbol"/> and to the symbols that find-references cascades to.
   /// </summary>
   /// <remarks>
   /// A use in a file that several projects compile is listed once for each symbol that it refers to, with the
   /// document and definition of the first project that reports it.
   /// </remarks>
   public static async Task<IReadOnlyList<SourceReference>> FindReferencesAsync(
      ISymbol symbol, Solution solution, CancellationToken cancellationToken)
   {
      var referencedSymbols = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken);

      var references = new List<SourceReference>();
      var indexers = new List<IPropertySymbol>();
      foreach (var referenced in referencedSymbols)
      {
         var indexer = referenced.Definition is IPropertySymbol { IsIndexer: true } property ? property : null;
         if (indexer is not null)
            indexers.Add(indexer);

         foreach (var location in referenced.Locations)
         {
            if (!location.Location.IsInSource || (indexer is not null && await IsAtBindingAsync(location.Location, cancellationToken)))
               continue;

            references.Add(new SourceReference(referenced.Definition, location.Document, location.Location));
         }
      }

      if (indexers.Count == 0)
         return DistinctReferences(references);

      foreach (var binding in await FindIndexerBindingsAsync(solution, indexers, cancellationToken))
      {
         var definition = indexers.FirstOrDefault(indexer => binding.Indexers.Any(bound => IsSameSymbol(bound, indexer)));
         if (definition is not null)
            references.Add(new SourceReference(definition, binding.Document, binding.Location));
      }

      return DistinctReferences(references);
   }

   private static List<SourceReference> DistinctReferences(List<SourceReference> references)
   {
      var definitionsBySite = new Dictionary<Location, List<ISymbol>>(SiteComparer.Instance);
      var distinct = new List<SourceReference>();
      foreach (var reference in references)
      {
         if (!definitionsBySite.TryGetValue(reference.Location, out var definitions))
            definitionsBySite[reference.Location] = definitions = [];
         else if (definitions.Any(definition => IsLinkedCopy(definition, reference.Definition)))
            continue;

         definitions.Add(reference.Definition);
         distinct.Add(reference);
      }

      return distinct;
   }

   /// <summary>
   /// Returns whether two symbols are one declaration: the same symbol, or copies of it in projects that compile its file.
   /// </summary>
   /// <remarks>
   /// A local or a parameter has no documentation ID, so its copies match by name and declaration.
   /// </remarks>
   private static bool IsLinkedCopy(ISymbol first, ISymbol second) =>
      IsSameSymbol(first, second)
      || (first.Kind == second.Kind && first.Name == second.Name && HaveSameDeclaration(first, second));

   /// <summary>
   /// Returns the methods, properties, fields and events whose code refers to <paramref name="symbol"/>, with the
   /// references inside each.
   /// </summary>
   /// <remarks>
   /// The search follows the inheritance hierarchy one way from <paramref name="symbol"/>, to the members it overrides
   /// or implements and to the members that override or implement it.
   /// For an accessor, a reference counts only when it gets or sets the value with that accessor.
   /// A use in a file that several projects compile is listed once, under the calling symbol of the first project
   /// that reports it. A use in an initializer or in top-level statements counts under the member that declares it,
   /// see <see cref="GetDeclaredMember"/>.
   /// </remarks>
   public static async Task<IReadOnlyList<SymbolCallers>> FindCallersAsync(
      ISymbol symbol, Solution solution, CancellationToken cancellationToken)
   {
      var callerInfos = await SymbolFinder.FindCallersAsync(symbol, solution, cancellationToken);

      var callers = new CallerList();
      var sites = new HashSet<Location>(SiteComparer.Instance);
      foreach (var callerInfo in callerInfos)
      {
         var isIndexer = GetIndexer(callerInfo.CalledSymbol) is not null;
         var locations = new List<Location>();
         foreach (var location in callerInfo.Locations)
         {
            if (location.IsInSource && !(isIndexer && await IsAtBindingAsync(location, cancellationToken)) && sites.Add(location))
               locations.Add(location);
         }

         if (locations.Count > 0)
            callers.Add(callerInfo.CallingSymbol, locations);
      }

      if (GetIndexer(symbol.OriginalDefinition) is not { } target)
         return callers.ToSourceOrderedList();

      var baseMembers = new Dictionary<ISymbol, List<ISymbol>>(SymbolEqualityComparer.Default);
      var targetBases = await GetBaseMembersAsync(target, baseMembers, solution, cancellationToken);
      foreach (var binding in await FindIndexerBindingsAsync(solution, [target, .. targetBases], cancellationToken))
      {
         var bound = await FindCalledIndexerAsync(binding, target, baseMembers, solution, cancellationToken);
         if (bound is null || (symbol is IMethodSymbol accessor && !UsesAccessor(binding.Syntax, bound, accessor)) || !sites.Add(binding.Location))
            continue;

         if (GetCallingSymbol(binding.Model, binding.Syntax.SpanStart, cancellationToken) is { } callingSymbol)
            callers.Add(callingSymbol, [binding.Location]);
      }

      return callers.ToSourceOrderedList();
   }

   /// <summary>
   /// Groups references under the member that declares their calling symbol, one entry for each member in the order
   /// the members are first added. See <see cref="GetDeclaredMember"/>.
   /// </summary>
   /// <remarks>
   /// The copies of a member in projects that compile its file share one entry, see <see cref="DeclarationComparer"/>.
   /// </remarks>
   private sealed class CallerList
   {
      private readonly List<SymbolCallers> _callers = [];
      private readonly Dictionary<ISymbol, SymbolCallers> _byMember = new(DeclarationComparer.Instance);
      private SymbolCallers? _topLevelStatements;

      public void Add(ISymbol callingSymbol, IEnumerable<Location> locations)
      {
         var member = GetDeclaredMember(callingSymbol);
         var caller = member is null ? _topLevelStatements : _byMember.GetValueOrDefault(member);
         if (caller is null)
         {
            caller = new SymbolCallers(member, []);
            _callers.Add(caller);
            if (member is null)
               _topLevelStatements = caller;
            else
               _byMember[member] = caller;
         }

         caller.Locations.AddRange(locations);
      }

      /// <summary>
      /// Returns the callers, with the references of each sorted by file path and then by position in the file.
      /// </summary>
      public List<SymbolCallers> ToSourceOrderedList()
      {
         foreach (var caller in _callers)
         {
            caller.Locations.Sort((first, second) =>
               string.Compare(first.SourceTree?.FilePath, second.SourceTree?.FilePath, StringComparison.Ordinal) is var byPath and not 0
                  ? byPath
                  : first.SourceSpan.Start.CompareTo(second.SourceSpan.Start));
         }

         return _callers;
      }
   }

   /// <summary>
   /// Compares symbols as declarations in source: the same symbol, or symbols of one kind and name declared at the same
   /// span of one file path.
   /// </summary>
   /// <remarks>
   /// The copies of a declaration in each project that compiles its file match, whatever the projects' assembly names.
   /// Documentation IDs do not decide, because local functions with one name in one type share an ID, and so do the
   /// members of projects with one assembly name.
   /// </remarks>
   internal sealed class DeclarationComparer : IEqualityComparer<ISymbol>
   {
      public static readonly DeclarationComparer Instance = new();

      public bool Equals(ISymbol? first, ISymbol? second) =>
         ReferenceEquals(first, second)
         || (first is not null
            && second is not null
            && (SymbolEqualityComparer.Default.Equals(first, second)
               || (first.Kind == second.Kind && first.Name == second.Name && HaveSameDeclaration(first, second))));

      public int GetHashCode(ISymbol symbol) => HashCode.Combine(symbol.Kind, symbol.Name);
   }

   /// <summary>
   /// Returns the member that declares the code of <paramref name="symbol"/>, a calling symbol that find-callers reports.
   /// </summary>
   /// <returns>
   /// The property or event of a backing field that the compiler generates, null for the method that the compiler
   /// generates for top-level statements, or the symbol itself.
   /// </returns>
   /// <remarks>
   /// The compiler holds the initializer of an auto-property or a field-like event in its backing field, and
   /// top-level statements in a method of their own. Neither has a declaration in source.
   /// </remarks>
   private static ISymbol? GetDeclaredMember(ISymbol symbol) => symbol switch
   {
      IFieldSymbol { AssociatedSymbol: { } member } => member,
      _ when symbol.IsTopLevelStatementsEntryPoint() => null,
      _ => symbol,
   };

   /// <summary>
   /// Compares source locations as uses: the same span of one file, in any project that compiles the file.
   /// </summary>
   /// <remarks>
   /// Paths compare ignoring letter case, as in <see cref="HaveSameDeclaration"/>. A location in a document without a
   /// path matches only locations in the same syntax tree.
   /// </remarks>
   private sealed class SiteComparer : IEqualityComparer<Location>
   {
      public static readonly SiteComparer Instance = new();

      public bool Equals(Location? first, Location? second) =>
         ReferenceEquals(first, second)
         || (first is not null
            && second is not null
            && first.SourceSpan == second.SourceSpan
            && (first.SourceTree == second.SourceTree
               || (first.SourceTree is { FilePath.Length: > 0 } tree
                  && string.Equals(tree.FilePath, second.SourceTree?.FilePath, StringComparison.OrdinalIgnoreCase))));

      public int GetHashCode(Location location) => HashCode.Combine(
         location.SourceSpan,
         location.SourceTree is { FilePath.Length: > 0 } tree
            ? StringComparer.OrdinalIgnoreCase.GetHashCode(tree.FilePath)
            : RuntimeHelpers.GetHashCode(location.SourceTree));
   }

   private sealed record IndexerBinding(
      Document Document, SemanticModel Model, ElementBindingExpressionSyntax Syntax, Location Location, IReadOnlyList<IPropertySymbol> Indexers);

   /// <summary>
   /// Returns each element binding that binds to an indexer, with the indexers it binds to, in the projects whose code
   /// can refer to <paramref name="definitions"/>.
   /// </summary>
   /// <remarks>
   /// A binding that does not resolve lists the candidate indexers, as find-references matches candidates too.
   /// </remarks>
   private static async Task<List<IndexerBinding>> FindIndexerBindingsAsync(
      Solution solution, IEnumerable<ISymbol> definitions, CancellationToken cancellationToken)
   {
      var scope = GetSearchScope(solution, definitions, cancellationToken);
      var bindings = new List<IndexerBinding>();
      foreach (var project in solution.Projects.Where(project => project.SupportsCompilation && (scope is null || scope.Contains(project.Id))))
      {
         IEnumerable<Document> documents = [.. project.Documents, .. await project.GetSourceGeneratedDocumentsAsync(cancellationToken)];
         foreach (var document in documents)
         {
            if (await document.GetSyntaxRootAsync(cancellationToken) is not { } root)
               continue;

            var syntaxes = root.DescendantNodes().OfType<ElementBindingExpressionSyntax>().ToList();
            if (syntaxes.Count == 0 || await document.GetSemanticModelAsync(cancellationToken) is not { } model)
               continue;

            foreach (var syntax in syntaxes)
            {
               var symbolInfo = model.GetSymbolInfo(syntax, cancellationToken);
               var indexers = (symbolInfo.Symbol is { } resolved ? [resolved] : symbolInfo.CandidateSymbols)
                  .OfType<IPropertySymbol>()
                  .Where(property => property.IsIndexer)
                  .ToList();
               if (indexers.Count > 0)
               {
                  // Find-references reports an indexer as an empty span at its opening bracket.
                  var location = root.SyntaxTree.GetLocation(new TextSpan(syntax.SpanStart, 0));
                  bindings.Add(new IndexerBinding(document, model, syntax, location, indexers));
               }
            }
         }
      }

      return bindings;
   }

   /// <summary>
   /// Returns the projects that declare <paramref name="symbols"/> and the projects that depend on them, or null when
   /// a symbol comes from metadata, which every project can refer to.
   /// </summary>
   /// <remarks>
   /// A file compiled into several projects, as for each target framework, declares the symbol in each of them. A
   /// project that compiles against a built copy of a declaring project refers to the copy, not to the symbol.
   /// </remarks>
   private static HashSet<ProjectId>? GetSearchScope(Solution solution, IEnumerable<ISymbol> symbols, CancellationToken cancellationToken)
   {
      var dependencyGraph = solution.GetProjectDependencyGraph();
      var scope = new HashSet<ProjectId>();
      foreach (var symbol in symbols)
      {
         if (symbol.ContainingAssembly is not { } assembly || solution.GetProject(assembly, cancellationToken) is not { } project)
            return null;

         var declaringProjects = symbol.Locations
            .Select(location => location.SourceTree?.FilePath)
            .OfType<string>()
            .Where(filePath => filePath.Length > 0)
            .SelectMany(filePath => solution.GetDocumentIdsWithFilePath(filePath))
            .Select(documentId => documentId.ProjectId)
            .Append(project.Id);
         foreach (var projectId in declaringProjects)
         {
            scope.Add(projectId);
            scope.UnionWith(dependencyGraph.GetProjectsThatTransitivelyDependOnThisProject(projectId));
         }
      }

      return scope;
   }

   /// <summary>
   /// Returns whether <paramref name="location"/> starts a member binding or an element binding, the access after a
   /// <c>?</c>.
   /// </summary>
   private static async Task<bool> IsAtBindingAsync(Location location, CancellationToken cancellationToken)
   {
      if (location.SourceTree is not { } tree)
         return false;

      var token = (await tree.GetRootAsync(cancellationToken)).FindToken(location.SourceSpan.Start);
      return token.Parent is MemberBindingExpressionSyntax
         or BracketedArgumentListSyntax { Parent: ElementBindingExpressionSyntax };
   }

   /// <summary>
   /// Returns whether two symbols are the same declaration, ignoring type arguments.
   /// </summary>
   /// <remarks>
   /// Two compilations can each hold their own instance of one symbol, so the documentation IDs decide, within one
   /// assembly or within one declaration in a file that several projects compile.
   /// </remarks>
   private static bool IsSameSymbol(ISymbol first, ISymbol second)
   {
      var firstDefinition = first.OriginalDefinition;
      var secondDefinition = second.OriginalDefinition;
      return SymbolEqualityComparer.Default.Equals(firstDefinition, secondDefinition)
         || (firstDefinition.GetDocumentationCommentId() is { } id
            && id == secondDefinition.GetDocumentationCommentId()
            && (firstDefinition.ContainingAssembly?.Identity.Name == secondDefinition.ContainingAssembly?.Identity.Name
               || HaveSameDeclaration(firstDefinition, secondDefinition)));
   }

   /// <summary>
   /// Returns whether the two symbols are declared at the same span of one file path.
   /// </summary>
   /// <remarks>
   /// Paths compare ignoring letter case, as <see cref="Solution.GetDocumentIdsWithFilePath"/> links documents; see
   /// <see href="https://github.com/dotnet/roslyn/blob/1324e1d34a0490454837a22370e75bbaeecffb87/src/Workspaces/Core/Portable/Workspace/Solution/SolutionState.CachingFilePathComparer.cs#L32">CachingFilePathComparer</see>.
   /// </remarks>
   private static bool HaveSameDeclaration(ISymbol first, ISymbol second) =>
      first.Locations.Any(location =>
         location.SourceTree is { FilePath.Length: > 0 } tree
         && second.Locations.Any(other =>
            other.SourceSpan == location.SourceSpan
            && string.Equals(other.SourceTree?.FilePath, tree.FilePath, StringComparison.OrdinalIgnoreCase)));

   /// <summary>
   /// Returns the indexer that <paramref name="symbol"/> is or that it is an accessor of, or null when it is neither.
   /// </summary>
   private static IPropertySymbol? GetIndexer(ISymbol symbol) => symbol switch
   {
      IPropertySymbol { IsIndexer: true } indexer => indexer,
      IMethodSymbol { AssociatedSymbol: IPropertySymbol { IsIndexer: true } indexer } => indexer,
      _ => null,
   };

   /// <summary>
   /// Returns the indexer of <paramref name="binding"/> that find-callers on <paramref name="target"/> follows: the
   /// target itself, a member it overrides or implements, or a member that overrides or implements it.
   /// </summary>
   /// <returns>The indexer, or null when the binding refers to none of them.</returns>
   private static async Task<IPropertySymbol?> FindCalledIndexerAsync(
      IndexerBinding binding,
      IPropertySymbol target,
      Dictionary<ISymbol, List<ISymbol>> baseMembers,
      Solution solution,
      CancellationToken cancellationToken)
   {
      var targetBases = await GetBaseMembersAsync(target, baseMembers, solution, cancellationToken);
      foreach (var indexer in binding.Indexers)
      {
         if (IsSameSymbol(indexer, target) || targetBases.Any(baseMember => IsSameSymbol(indexer, baseMember)))
            return indexer;

         var indexerBases = await GetBaseMembersAsync(indexer, baseMembers, solution, cancellationToken);
         if (indexerBases.Any(baseMember => IsSameSymbol(baseMember, target)))
            return indexer;
      }

      return null;
   }

   /// <summary>
   /// Returns the members that <paramref name="property"/> overrides or implements, and theirs in turn, with each
   /// result kept in <paramref name="cache"/> by original definition.
   /// </summary>
   private static async Task<List<ISymbol>> GetBaseMembersAsync(
      IPropertySymbol property, Dictionary<ISymbol, List<ISymbol>> cache, Solution solution, CancellationToken cancellationToken)
   {
      var key = property.OriginalDefinition;
      if (cache.TryGetValue(key, out var cached))
         return cached;

      var bases = new List<ISymbol>();
      var pending = new Queue<ISymbol>([key]);
      while (pending.TryDequeue(out var current))
      {
         var direct = (await SymbolFinder.FindImplementedInterfaceMembersAsync(current, solution, cancellationToken: cancellationToken)).ToList();
         if (current is IPropertySymbol { OverriddenProperty: { } overridden })
            direct.Add(overridden);

         foreach (var baseMember in direct.Where(baseMember => !bases.Any(known => IsSameSymbol(known, baseMember))))
         {
            bases.Add(baseMember);
            pending.Enqueue(baseMember);
         }
      }

      cache[key] = bases;
      return bases;
   }

   /// <summary>
   /// Returns whether the element binding gets or sets the value of <paramref name="indexer"/> with an accessor of the
   /// kind of <paramref name="accessor"/>.
   /// </summary>
   /// <remarks>
   /// The binding sets the value as an assignment's target, and gets it unless the assignment is a simple one. An
   /// indexer that returns by reference has only a getter.
   /// </remarks>
   private static bool UsesAccessor(ElementBindingExpressionSyntax binding, IPropertySymbol indexer, IMethodSymbol accessor)
   {
      var isAssigned = binding.Parent is AssignmentExpressionSyntax assignment && assignment.Left == binding && !indexer.ReturnsByRef;
      var isSimplyAssigned = isAssigned && binding.Parent.IsKind(SyntaxKind.SimpleAssignmentExpression);
      return accessor.MethodKind switch
      {
         MethodKind.PropertyGet => !isSimplyAssigned,
         MethodKind.PropertySet => isAssigned,
         _ => throw new InvalidOperationException($"Unexpected accessor kind: {accessor.MethodKind}"),
      };
   }

   /// <summary>
   /// Returns the calling symbol of the code at <paramref name="position"/>, as find-callers reports it, or null when
   /// the position is outside all members.
   /// </summary>
   /// <remarks>
   /// An accessor counts as its property or event, and a lambda as the member that holds it.
   /// </remarks>
   private static ISymbol? GetCallingSymbol(SemanticModel model, int position, CancellationToken cancellationToken)
   {
      for (var current = model.GetEnclosingSymbol(position, cancellationToken); current is not null; current = current.ContainingSymbol)
      {
         switch (current)
         {
            case IFieldSymbol or IPropertySymbol:
               return current;
            case IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise } accessor:
               return accessor.AssociatedSymbol;
            case IMethodSymbol { MethodKind: not MethodKind.AnonymousFunction }:
               return current;
         }
      }

      return null;
   }
}
