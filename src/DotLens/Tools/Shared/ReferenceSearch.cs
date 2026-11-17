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
/// The code that refers to a symbol from inside one method, property or field.
/// </summary>
/// <param name="CallingSymbol">The method, property or field that holds the references.</param>
/// <param name="Locations">The references in source.</param>
internal sealed record SymbolCallers(ISymbol CallingSymbol, List<Location> Locations);

/// <summary>
/// Finds references and callers with <see cref="SymbolFinder"/>, with its indexer references in null-conditional
/// accesses replaced by a scan of the element bindings.
/// </summary>
/// <remarks>
/// <see cref="SymbolFinder"/> matches an indexer against the whole access after the <c>?</c> and reports it at that
/// access's start. So it misses the indexer in <c>a?[0] = v</c> and <c>a?[0].M()</c>, and reports the indexer of
/// <c>[1]</c> at the <c>[0]</c> of <c>a?[0][1]</c>. See <see href="https://github.com/dotnet/roslyn/blob/1324e1d34a0490454837a22370e75bbaeecffb87/src/Workspaces/Core/Portable/FindSymbols/FindReferences/Finders/PropertySymbolReferenceFinder.cs#L269-L286">ComputeConditionalAccessInformation</see>.
/// </remarks>
internal static class ReferenceSearch
{
   /// <summary>
   /// Returns the references in source to <paramref name="symbol"/> and to the symbols that find-references cascades to.
   /// </summary>
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
         return references;

      foreach (var binding in await FindIndexerBindingsAsync(solution, indexers, cancellationToken))
      {
         var definition = indexers.FirstOrDefault(indexer => binding.Indexers.Any(bound => IsSameSymbol(bound, indexer)));
         if (definition is not null)
            references.Add(new SourceReference(definition, binding.Document, binding.Location));
      }

      return references;
   }

   /// <summary>
   /// Returns the methods, properties and fields whose code refers to <paramref name="symbol"/>, with the references
   /// inside each.
   /// </summary>
   /// <remarks>
   /// The search follows the inheritance hierarchy one way from <paramref name="symbol"/>, to the members it overrides
   /// or implements and to the members that override or implement it.
   /// For an accessor, a reference counts only when it gets or sets the value with that accessor.
   /// </remarks>
   public static async Task<IReadOnlyList<SymbolCallers>> FindCallersAsync(
      ISymbol symbol, Solution solution, CancellationToken cancellationToken)
   {
      var callerInfos = await SymbolFinder.FindCallersAsync(symbol, solution, cancellationToken);

      var callers = new List<SymbolCallers>();
      foreach (var callerInfo in callerInfos)
      {
         var isIndexer = GetIndexer(callerInfo.CalledSymbol) is not null;
         var locations = new List<Location>();
         foreach (var location in callerInfo.Locations)
         {
            if (location.IsInSource && !(isIndexer && await IsAtBindingAsync(location, cancellationToken)))
               locations.Add(location);
         }

         if (locations.Count > 0)
            callers.Add(new SymbolCallers(callerInfo.CallingSymbol, locations));
      }

      if (GetIndexer(symbol.OriginalDefinition) is not { } target)
         return callers;

      var baseMembers = new Dictionary<ISymbol, List<ISymbol>>(SymbolEqualityComparer.Default);
      var targetBases = await GetBaseMembersAsync(target, baseMembers, solution, cancellationToken);
      foreach (var binding in await FindIndexerBindingsAsync(solution, [target, .. targetBases], cancellationToken))
      {
         var bound = await FindCalledIndexerAsync(binding, target, baseMembers, solution, cancellationToken);
         if (bound is null || (symbol is IMethodSymbol accessor && !UsesAccessor(binding.Syntax, bound, accessor)))
            continue;

         if (GetCallingSymbol(binding.Model, binding.Syntax.SpanStart, cancellationToken) is not { } callingSymbol)
            continue;

         var existing = callers.FirstOrDefault(caller => SymbolEqualityComparer.Default.Equals(caller.CallingSymbol, callingSymbol));
         if (existing is not null)
            existing.Locations.Add(binding.Location);
         else
            callers.Add(new SymbolCallers(callingSymbol, [binding.Location]));
      }

      return callers;
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
   /// assembly or within one declaration in a file that several projects compile. A linked copy of the file can be
   /// out of date, so the position alone never decides.
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
   /// Returns the method, property or field whose code holds <paramref name="position"/>, as find-callers groups
   /// references, or null when the position is outside all of them.
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
