using DotLens.Infrastructure;
using Microsoft.CodeAnalysis.FindSymbols;

namespace DotLens.Tools.Analysis;

/// <summary>
/// Decides which source symbols of a solution are used, and caches what it finds for one find_unused_code call.
/// </summary>
/// <remarks>
/// A symbol is used when code that runs references it from outside the symbol's own declaration. Code runs when it is
/// a root, or when a reference from code that runs reaches it. So members that only reference each other are unused.
/// A root is code that the runtime or a framework calls without a reference in source, see
/// <see cref="IsCalledWithoutReferenceAsync"/>, or code that callers outside the analysis can reach, see
/// <see cref="IsOpen"/>.
/// </remarks>
internal sealed class UsageAnalysis
{
   private const string ModuleInitializerAttributeName = "System.Runtime.CompilerServices.ModuleInitializerAttribute";
   private const string InternalsVisibleToAttributeName = "System.Runtime.CompilerServices.InternalsVisibleToAttribute";

   private readonly Solution _solution;
   private readonly FrameworkEntryPoints _frameworkEntryPoints;
   private readonly Dictionary<ISymbol, bool> _used = new(SymbolEqualityComparer.Default);
   private readonly Dictionary<ISymbol, bool> _open = new(SymbolEqualityComparer.Default);
   private readonly Dictionary<ISymbol, bool> _calledWithoutReference = new(SymbolEqualityComparer.Default);
   private readonly Dictionary<ISymbol, IReadOnlyList<ISymbol?>> _referrers = new(SymbolEqualityComparer.Default);
   private readonly Dictionary<IAssemblySymbol, IMethodSymbol?> _entryPoints = new(SymbolEqualityComparer.Default);
   private readonly Dictionary<IAssemblySymbol, bool> _hasFriendsOutsideSolution = new(SymbolEqualityComparer.Default);
   private readonly Dictionary<INamedTypeSymbol, IReadOnlyList<ISymbol?>> _typeUseReferrers = new(SymbolEqualityComparer.Default);
   private readonly Dictionary<ProjectId, Dictionary<string, List<ISymbol>>> _dataSourceIndexes = [];
   private HashSet<string>? _solutionAssemblyNames;

   public UsageAnalysis(Solution solution, FrameworkEntryPoints frameworkEntryPoints)
   {
      _solution = solution;
      _frameworkEntryPoints = frameworkEntryPoints;
   }

   /// <summary>
   /// Returns whether the symbol is used.
   /// </summary>
   /// <remarks>
   /// A type is used also when it holds a member that the runtime or a framework calls, and a static class when code
   /// that runs uses one of its members. A static constructor is used when its type is used.
   /// The search never counts the symbol itself as code that runs, so a public member that only its own callees call is
   /// unused.
   /// </remarks>
   public async Task<bool> IsUsedAsync(ISymbol symbol, CancellationToken cancellationToken)
   {
      symbol = Normalize(symbol);
      if (_used.TryGetValue(symbol, out var known))
         return known;

      if (await IsCalledWithoutReferenceAsync(symbol, cancellationToken))
         return _used[symbol] = true;

      // Search back from the symbol through the code that references it, until code that runs.
      var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { symbol };
      var pending = new Queue<ISymbol>([symbol]);
      while (pending.TryDequeue(out var current))
      {
         foreach (var referrer in await GetReferrersAsync(current, cancellationToken))
         {
            if (referrer is not null && visited.Contains(referrer))
               continue;

            if (referrer is null || await RunsAsync(referrer, cancellationToken))
               return _used[symbol] = true;

            if (_used.TryGetValue(referrer, out var referrerUsed) && !referrerUsed)
               continue;

            visited.Add(referrer);
            pending.Enqueue(referrer);
         }
      }

      // The search skipped the symbol itself as a referrer, so the other visited symbols are unused only when the
      // symbol cannot run as a root.
      if (IsOpen(symbol))
         visited = [symbol];

      foreach (var unused in visited)
         _used[unused] = false;

      return false;
   }

   /// <summary>
   /// Returns whether the code of a referrer runs: it is a root, or a known used symbol.
   /// </summary>
   private async Task<bool> RunsAsync(ISymbol referrer, CancellationToken cancellationToken) =>
      (_used.TryGetValue(referrer, out var used) && used)
      || IsOpen(referrer)
      || await IsCalledWithoutReferenceAsync(referrer, cancellationToken);

   /// <summary>
   /// Returns whether the runtime or a framework calls the symbol without a reference in source: an entry point, a
   /// module initializer, or a member that <see cref="FrameworkEntryPoints"/> recognizes.
   /// </summary>
   /// <remarks>
   /// A type counts when a framework creates its instances or when it holds such a member, a nested type's included.
   /// An instance constructor counts when a framework creates instances of its type.
   /// </remarks>
   private async Task<bool> IsCalledWithoutReferenceAsync(ISymbol symbol, CancellationToken cancellationToken)
   {
      if (_calledWithoutReference.TryGetValue(symbol, out var known))
         return known;

      var called = symbol switch
      {
         INamedTypeSymbol type => _frameworkEntryPoints.CreatesInstances(type) || await HoldsCalledMemberAsync(type, cancellationToken),
         IMethodSymbol { MethodKind: MethodKind.StaticConstructor, ContainingType: { } type } =>
            await IsCalledWithoutReferenceAsync(type, cancellationToken),
         IMethodSymbol { MethodKind: MethodKind.Constructor, ContainingType: { } type } constructor =>
            _frameworkEntryPoints.IsCalled(constructor) || _frameworkEntryPoints.CreatesInstances(type),
         IMethodSymbol method =>
            SymbolEqualityComparer.Default.Equals(method, await GetEntryPointAsync(method.ContainingAssembly, cancellationToken))
            || method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == ModuleInitializerAttributeName)
            || _frameworkEntryPoints.IsCalled(method),
         _ => _frameworkEntryPoints.IsCalled(symbol),
      };

      _calledWithoutReference[symbol] = called;
      return called;
   }

   private async Task<bool> HoldsCalledMemberAsync(INamedTypeSymbol type, CancellationToken cancellationToken)
   {
      foreach (var member in type.GetMembers())
      {
         // A static constructor counts only through its type.
         if (member is IMethodSymbol { MethodKind: MethodKind.StaticConstructor })
            continue;

         if (await IsCalledWithoutReferenceAsync(member, cancellationToken))
            return true;
      }

      return false;
   }

   private async Task<IMethodSymbol?> GetEntryPointAsync(IAssemblySymbol? assembly, CancellationToken cancellationToken)
   {
      if (assembly is null)
         return null;

      if (_entryPoints.TryGetValue(assembly, out var known))
         return known;

      IMethodSymbol? entryPoint = null;
      if (_solution.GetProject(assembly, cancellationToken) is { } project
         && await project.GetCompilationAsync(cancellationToken) is { } compilation)
      {
         entryPoint = compilation.GetEntryPoint(cancellationToken);
      }

      _entryPoints[assembly] = entryPoint;
      return entryPoint;
   }

   /// <summary>
   /// Returns whether code outside the analysis can reach the symbol, so it runs as a root.
   /// </summary>
   /// <remarks>
   /// Code in another assembly can call a symbol that is visible outside its own assembly, internal symbols included
   /// when the assembly has <c>InternalsVisibleTo</c> for an assembly outside the solution. Dispatch can call a virtual,
   /// abstract or interface member without a reference to it. The runtime calls a finalizer without a reference, and
   /// operators and conversions count as roots too. An override or an interface implementation also runs when its type
   /// is used, see <see cref="GetReferrersAsync"/>.
   /// </remarks>
   private bool IsOpen(ISymbol symbol)
   {
      if (_open.TryGetValue(symbol, out var known))
         return known;

      var open = symbol switch
      {
         INamedTypeSymbol => IsVisibleOutsideAssembly(symbol),
         IMethodSymbol { MethodKind: MethodKind.StaticConstructor } => false,
         IMethodSymbol { MethodKind: not (MethodKind.Ordinary or MethodKind.Constructor or MethodKind.ExplicitInterfaceImplementation) } => true,
         _ when symbol.IsVirtual || symbol.IsAbstract => true,
         _ when symbol.ContainingType is { TypeKind: TypeKind.Interface } => true,
         _ => IsVisibleOutsideAssembly(symbol),
      };

      _open[symbol] = open;
      return open;
   }

   private bool IsVisibleOutsideAssembly(ISymbol symbol)
   {
      var friendsSeeInternals = HasFriendsOutsideSolution(symbol.ContainingAssembly);
      for (ISymbol? current = symbol; current is not null and not INamespaceSymbol; current = current.ContainingSymbol)
      {
         switch (current.DeclaredAccessibility)
         {
            case Accessibility.Private:
            case Accessibility.Internal or Accessibility.ProtectedAndInternal when !friendsSeeInternals:
               return false;
         }
      }

      return true;
   }

   /// <summary>
   /// Returns whether the assembly's internals are visible to an assembly outside the solution.
   /// </summary>
   /// <remarks>
   /// Find-references sees the code of a friend inside the solution, so such a friend needs no roots.
   /// </remarks>
   private bool HasFriendsOutsideSolution(IAssemblySymbol? assembly)
   {
      if (assembly is null)
         return false;

      if (!_hasFriendsOutsideSolution.TryGetValue(assembly, out var hasFriends))
      {
         _solutionAssemblyNames ??= _solution.Projects
            .Select(project => project.AssemblyName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
         hasFriends = assembly.GetAttributes()
            .Where(attribute => attribute.AttributeClass?.ToDisplayString() == InternalsVisibleToAttributeName)
            .Select(attribute => attribute.ConstructorArguments.FirstOrDefault().Value as string)
            .Any(friend => friend is null || !_solutionAssemblyNames.Contains(friend.Split(',')[0].Trim()));
         _hasFriendsOutsideSolution[assembly] = hasFriends;
      }

      return hasFriends;
   }

   private async Task<bool> ImplementsInterfaceMemberAsync(ISymbol symbol, CancellationToken cancellationToken)
   {
      if (symbol is IMethodSymbol { ExplicitInterfaceImplementations.Length: > 0 }
         or IPropertySymbol { ExplicitInterfaceImplementations.Length: > 0 }
         or IEventSymbol { ExplicitInterfaceImplementations.Length: > 0 })
      {
         return true;
      }

      if (symbol is not (IMethodSymbol or IPropertySymbol or IEventSymbol) || symbol.DeclaredAccessibility == Accessibility.Private)
         return false;

      var implemented = await SymbolFinder.FindImplementedInterfaceMembersAsync(symbol, _solution, cancellationToken: cancellationToken);
      return implemented.Any();
   }

   /// <summary>
   /// Returns the symbols whose code references the symbol from outside its own declaration, with null for code that
   /// belongs to no symbol, such as top-level statements and assembly attributes.
   /// </summary>
   /// <remarks>
   /// A type's referrers are those of references from outside the type, and for a static class also those of
   /// references to its members. A static constructor runs when its type is used, so it has its type's referrers.
   /// An override or an interface implementation also has its type as a referrer, because code outside the source can
   /// call it through the base or interface member once the type is used. Calls in source through that member stay
   /// referrers, because a type created by reflection may have no other use.
   /// A member that a framework calls once code hands it the type, see
   /// <see cref="FrameworkEntryPoints.IsCalledThroughType"/>, also has the referrers of the type's uses in
   /// <c>typeof</c> and as a type argument.
   /// A member that a data source attribute names in a string also has the attribute's owner.
   /// </remarks>
   private async Task<IReadOnlyList<ISymbol?>> GetReferrersAsync(ISymbol symbol, CancellationToken cancellationToken)
   {
      if (symbol is IMethodSymbol { MethodKind: MethodKind.StaticConstructor, ContainingType: { } staticType })
         symbol = staticType;

      if (_referrers.TryGetValue(symbol, out var known))
         return known;

      IEnumerable<ISymbol> targets = symbol is INamedTypeSymbol { IsStatic: true } type
         ? [type, .. StaticClassMembers(type)]
         : [symbol];

      var referrers = new List<ISymbol?>();
      var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
      var hasRootReferrer = false;
      var isDispatched = symbol is { ContainingType: not null }
         && (symbol.IsOverride || await ImplementsInterfaceMemberAsync(symbol, cancellationToken));
      foreach (var target in targets)
      {
         foreach (var reference in await ReferenceSearch.FindReferencesAsync(target, _solution, cancellationToken))
         {
            if (IsWithin(symbol, reference.Location, cancellationToken))
               continue;

            foreach (var referrer in await GetReferrersAtAsync(reference, cancellationToken))
               Add(referrer);
         }
      }

      if (symbol is { ContainingType: { } containingType })
      {
         if (isDispatched)
            Add(containingType);

         if (FrameworkEntryPoints.IsCalledThroughType(symbol))
         {
            foreach (var referrer in await GetTypeUseReferrersAsync(containingType, cancellationToken))
               Add(referrer);
         }
      }

      foreach (var owner in await GetDataSourceOwnersAsync(symbol, cancellationToken))
         Add(owner);

      _referrers[symbol] = referrers;
      return referrers;

      void Add(ISymbol? referrer)
      {
         if (referrer is null ? !hasRootReferrer : seen.Add(referrer))
            referrers.Add(referrer);

         hasRootReferrer |= referrer is null;
      }
   }

   /// <summary>
   /// Returns the referrers of the references to the type that hand it to other code, which can then create it, such
   /// as a DI registration or <c>Activator.CreateInstance</c>. See <see cref="IsHandedOver"/>.
   /// </summary>
   private async Task<IReadOnlyList<ISymbol?>> GetTypeUseReferrersAsync(INamedTypeSymbol type, CancellationToken cancellationToken)
   {
      if (_typeUseReferrers.TryGetValue(type, out var known))
         return known;

      var referrers = new List<ISymbol?>();
      foreach (var reference in await ReferenceSearch.FindReferencesAsync(type, _solution, cancellationToken))
      {
         if (await reference.Document.GetSemanticModelAsync(cancellationToken) is { } model
            && IsHandedOver(model, reference.Location, type, cancellationToken))
         {
            referrers.AddRange(await GetReferrersAtAsync(reference, cancellationToken));
         }
      }

      _typeUseReferrers[type] = referrers;
      return referrers;
   }

   /// <summary>
   /// Returns whether the type name that holds a reference to the type hands the type over: the type of a
   /// <c>typeof</c> that mentions it, or a type argument list of a generic method, generic type member, generic type
   /// creation or generic attribute that mentions it.
   /// </summary>
   /// <remarks>
   /// The type may sit at any depth of the type name, as in an array, a tuple or a nested generic. A target-typed
   /// <c>new()</c> counts through the declared type it creates. Only the expression or declaration that owns the type
   /// name counts, so a use of the type inside an argument of a generic call does not, and neither does a declaration
   /// that only names the type, such as a base list or a field, parameter, local or return type.
   /// </remarks>
   private static bool IsHandedOver(SemanticModel model, Location location, INamedTypeSymbol type, CancellationToken cancellationToken)
   {
      // Climb the type name that holds the reference; its first other ancestor owns it.
      SyntaxNode name = model.SyntaxTree.GetRoot(cancellationToken).FindNode(location.SourceSpan, getInnermostNodeForTie: true);
      while (name.Parent is TypeSyntax or TypeArgumentListSyntax or TupleElementSyntax)
         name = name.Parent;

      return name.Parent switch
      {
         TypeOfExpressionSyntax typeOf =>
            model.GetTypeInfo(typeOf.Type, cancellationToken).Type is { } evaluated && Mentions(evaluated, type),
         MemberAccessExpressionSyntax or MemberBindingExpressionSyntax or ObjectCreationExpressionSyntax or AttributeSyntax =>
            BindsToHandover(model, name.Parent, type, cancellationToken),
         InvocationExpressionSyntax invocation when invocation.Expression == name =>
            BindsToHandover(model, name, type, cancellationToken),
         VariableDeclarationSyntax declaration when declaration.Type == name =>
            declaration.Variables.Any(variable =>
               variable.Initializer?.Value is ImplicitObjectCreationExpressionSyntax creation
               && BindsToHandover(model, creation, type, cancellationToken)),
         PropertyDeclarationSyntax { Initializer.Value: ImplicitObjectCreationExpressionSyntax creation } property when property.Type == name =>
            BindsToHandover(model, creation, type, cancellationToken),
         _ => false,
      };
   }

   private static bool BindsToHandover(SemanticModel model, SyntaxNode node, INamedTypeSymbol type, CancellationToken cancellationToken)
   {
      var info = model.GetSymbolInfo(node, cancellationToken);
      var symbols = info.Symbol is { } symbol ? [symbol] : info.CandidateSymbols;
      return symbols.Any(bound => bound switch
      {
         IMethodSymbol method => method.TypeArguments.Any(argument => Mentions(argument, type)) || MentionsInTypeArguments(method.ContainingType, type),
         IPropertySymbol or IFieldSymbol or IEventSymbol => MentionsInTypeArguments(bound.ContainingType, type),
         _ => false,
      });
   }

   private static bool Mentions(ITypeSymbol candidate, INamedTypeSymbol type) => candidate switch
   {
      _ when IsSameDefinition(candidate.OriginalDefinition, type) => true,
      INamedTypeSymbol named => MentionsInTypeArguments(named, type),
      IArrayTypeSymbol array => Mentions(array.ElementType, type),
      IPointerTypeSymbol pointer => Mentions(pointer.PointedAtType, type),
      _ => false,
   };

   private static bool MentionsInTypeArguments(INamedTypeSymbol? named, INamedTypeSymbol type) =>
      named is not null
      && (named.TypeArguments.Any(argument => Mentions(argument, type)) || MentionsInTypeArguments(named.ContainingType, type));

   /// <summary>
   /// Returns whether two symbols are one declaration, or copies of it in projects that compile its file.
   /// </summary>
   private static bool IsSameDefinition(ISymbol first, ISymbol second) =>
      SymbolEqualityComparer.Default.Equals(first, second)
      || (first.GetDocumentationCommentId() is { } id && id == second.GetDocumentationCommentId());

   /// <summary>
   /// Returns the symbols that hold a data source attribute naming the member in a string.
   /// </summary>
   /// <remarks>
   /// Only code that can see the member can name it, so the search covers the member's project and the projects that
   /// depend on it. An attribute on a parameter counts as on the parameter's method.
   /// </remarks>
   private async Task<IReadOnlyList<ISymbol>> GetDataSourceOwnersAsync(ISymbol member, CancellationToken cancellationToken)
   {
      if (DataSourceKey(member) is not { } key
         || member.ContainingAssembly is not { } assembly
         || _solution.GetProject(assembly, cancellationToken) is not { } project)
      {
         return [];
      }

      var owners = new List<ISymbol>();
      var dependents = _solution.GetProjectDependencyGraph().GetProjectsThatTransitivelyDependOnThisProject(project.Id);
      foreach (var projectId in dependents.Prepend(project.Id))
      {
         if ((await GetDataSourceIndexAsync(projectId, cancellationToken)).TryGetValue(key, out var found))
            owners.AddRange(found);
      }

      return owners;
   }

   /// <summary>
   /// Returns, for each member that a data source attribute in the project names in a string, the symbols that hold
   /// such an attribute, keyed by <see cref="DataSourceKey"/>.
   /// </summary>
   private async Task<Dictionary<string, List<ISymbol>>> GetDataSourceIndexAsync(ProjectId projectId, CancellationToken cancellationToken)
   {
      if (_dataSourceIndexes.TryGetValue(projectId, out var known))
         return known;

      var index = new Dictionary<string, List<ISymbol>>(StringComparer.Ordinal);
      if (_solution.GetProject(projectId) is { SupportsCompilation: true } project
         && await project.GetCompilationAsync(cancellationToken) is { } compilation)
      {
         foreach (var owner in compilation.GetSymbolsWithName(_ => true, SymbolFilter.TypeAndMember, cancellationToken))
         {
            var attributes = owner is IMethodSymbol method
               ? owner.GetAttributes().Concat(method.Parameters.SelectMany(parameter => parameter.GetAttributes()))
               : owner.GetAttributes();
            foreach (var attribute in attributes)
            {
               foreach (var source in FrameworkEntryPoints.GetNamedDataSources(attribute, owner))
               {
                  if (DataSourceKey(source) is not { } key)
                     continue;

                  if (!index.TryGetValue(key, out var owners))
                     index[key] = owners = [];
                  owners.Add(Normalize(owner));
               }
            }
         }
      }

      _dataSourceIndexes[projectId] = index;
      return index;
   }

   /// <summary>
   /// Returns a key that matches a member in every compilation that sees it: its assembly's name and its documentation
   /// ID, or null when it has no documentation ID.
   /// </summary>
   /// <remarks>
   /// A project that targets another framework than a project it references sees retargeted copies of its symbols.
   /// </remarks>
   private static string? DataSourceKey(ISymbol member) =>
      Normalize(member).OriginalDefinition is var definition
      && definition.GetDocumentationCommentId() is { } id
      && definition.ContainingAssembly is { } assembly
         ? $"{assembly.Name}|{id}"
         : null;

   /// <summary>
   /// Returns the members of a static class that code outside it can use without naming the class, extension block
   /// members included.
   /// </summary>
   private static IEnumerable<ISymbol> StaticClassMembers(INamedTypeSymbol type) =>
      type.GetMembers()
         .Where(member => member is not INamedTypeSymbol)
         .Concat(type.GetTypeMembers().Where(nested => nested.IsExtension).SelectMany(block => block.GetMembers()))
         .Where(RoslynHelpers.IsExplicitSourceSymbol);

   /// <summary>
   /// Returns the symbols whose code holds the reference: the member, the type when the reference is in a type's
   /// header, or each field of a field declaration whose shared part holds it.
   /// </summary>
   /// <returns>The symbols, or null for code that belongs to none.</returns>
   /// <remarks>
   /// Code in a lambda, a local function or an accessor belongs to the member that holds it. The header of an extension
   /// block belongs to the static class that holds the block.
   /// </remarks>
   private static async Task<IEnumerable<ISymbol?>> GetReferrersAtAsync(SourceReference reference, CancellationToken cancellationToken)
   {
      if (await reference.Document.GetSemanticModelAsync(cancellationToken) is not { } model)
         return [null];

      var root = await model.SyntaxTree.GetRootAsync(cancellationToken);
      var node = root.FindNode(reference.Location.SourceSpan, findInsideTrivia: true, getInnermostNodeForTie: true);
      foreach (var ancestor in node.AncestorsAndSelf(ascendOutOfTrivia: true))
      {
         switch (ancestor)
         {
            case VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax }:
               return [Declared(ancestor)];
            case BaseFieldDeclarationSyntax field:
               return field.Declaration.Variables.Select(Declared).ToList();
            case BaseTypeDeclarationSyntax or DelegateDeclarationSyntax:
               var declared = Declared(ancestor);
               if (declared is INamedTypeSymbol { IsExtension: true })
                  continue;
               return [declared];
            case BaseMethodDeclarationSyntax or BasePropertyDeclarationSyntax or EnumMemberDeclarationSyntax:
               return [Declared(ancestor)];
            case GlobalStatementSyntax:
               return [null];
         }
      }

      return [null];

      ISymbol? Declared(SyntaxNode declaration) =>
         model.GetDeclaredSymbol(declaration, cancellationToken) is { } symbol ? Normalize(symbol) : null;
   }

   /// <summary>
   /// Returns the defining part of a partial method or property, so both parts share one entry in the caches.
   /// </summary>
   private static ISymbol Normalize(ISymbol symbol) => symbol switch
   {
      IMethodSymbol { PartialDefinitionPart: { } definition } => definition,
      IPropertySymbol { PartialDefinitionPart: { } definition } => definition,
      _ => symbol,
   };

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
}
