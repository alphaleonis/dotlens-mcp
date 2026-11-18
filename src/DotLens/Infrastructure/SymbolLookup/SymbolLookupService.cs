using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotLens.Infrastructure.SymbolLookup;

/// <summary>
/// Unified service for looking up symbols across the solution.
/// Supports position-based and name-based lookups.
/// </summary>
public class SymbolLookupService
{
   /// <summary>
   /// The recovery hint of the error for a lookup that matches more than one symbol.
   /// </summary>
   public const string AmbiguityHint =
      "Look up one candidate by the name listed, without its parameter list (an indexer's name is this[]). Pass the listed parameter types as parameterTypes and a listed return type as returnType. Where a location is listed, use position mode with it instead. Where a project is listed, pass it as project";

   private readonly IReadOnlyWorkspace _workspaceManager;

   /// <summary>
   /// Display format used for deduplication - includes enough detail to distinguish overloads.
   /// Must include parameters with types to differentiate method overloads.
   /// </summary>
   private static readonly SymbolDisplayFormat DeduplicationFormat = SymbolDisplayFormat.FullyQualifiedFormat
      .WithMemberOptions(
         SymbolDisplayMemberOptions.IncludeParameters |
         SymbolDisplayMemberOptions.IncludeType |
         SymbolDisplayMemberOptions.IncludeRef |
         SymbolDisplayMemberOptions.IncludeContainingType)
      .WithParameterOptions(
         SymbolDisplayParameterOptions.IncludeType |
         SymbolDisplayParameterOptions.IncludeParamsRefOut);

   /// <summary>
   /// Format for type matching with C# keywords and namespace path.
   /// E.g., "System.Collections.Generic.List&lt;string&gt;"
   /// </summary>
   private static readonly SymbolDisplayFormat NamespacedKeywordFormat = new(
      typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
      miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes,
      genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

   /// <summary>
   /// Format for type matching with CLR names and namespace path.
   /// E.g., "System.String", "System.Collections.Generic.List&lt;System.String&gt;"
   /// </summary>
   private static readonly SymbolDisplayFormat NamespacedClrFormat = new(
      typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
      genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

   public SymbolLookupService(IReadOnlyWorkspace workspaceManager)
   {
      _workspaceManager = workspaceManager;
   }

   /// <summary>
   /// Find all symbols matching the lookup criteria.
   /// Returns both source and metadata symbols.
   /// </summary>
   /// <param name="filter">Optional predicate to filter results after lookup.</param>
   public async Task<IReadOnlyList<SymbolLookupResult<TSymbol>>> FindAsync<TSymbol>(
      SymbolLookup lookup,
      Func<TSymbol, bool>? filter = null,
      CancellationToken cancellationToken = default) where TSymbol : ISymbol
   {
      var solution = _workspaceManager.EnsureSolutionLoaded();

      var results = lookup switch
      {
         LookupByPosition pos => await FindByPositionAsync<TSymbol>(pos, cancellationToken),
         LookupByName name => await FindByNameAsync<TSymbol>(name, cancellationToken),
         _ => throw new ToolException(
            ErrorCodes.InvalidParameter,
            $"Unknown lookup type: {lookup.GetType().Name}")
      };

      if (filter != null)
      {
         results = results.Where(r => filter(r.Symbol)).ToList();
      }

      if (lookup is LookupByName byName)
      {
         results = PreferNonGeneric(results, SplitQualifiedName(byName.Name), GetProjectAssemblies(solution));
      }

      return results;
   }

   /// <summary>
   /// The identity of a symbol across the compilations of the solution's projects, which hold different instances of
   /// one symbol. A name lookup lists each identity once, and the name lookup for a listed candidate finds its identity.
   /// </summary>
   /// <param name="Display">The full name of the symbol with its parameter types.</param>
   /// <param name="Declaration">
   /// The file path, in upper case, and the offset of the first declaration of a symbol in source, or null for any other
   /// symbol.
   /// </param>
   /// <param name="ProjectAssembly">
   /// The assembly name of the solution project that holds a symbol not in source, or null for a symbol in source, a
   /// namespace, or a symbol of any other assembly.
   /// </param>
   private readonly record struct SymbolIdentity(string Display, string? Declaration, string? ProjectAssembly)
   {
      /// <remarks>
      /// Every project that compiles a source file declares the same symbols, whatever its assembly name, and two
      /// declarations are two symbols. A symbol of an assembly outside the solution is known by its name alone, because
      /// the framework assemblies of a project's builds for several frameworks differ in version. A namespace spans
      /// projects.
      /// </remarks>
      public static SymbolIdentity Of(ISymbol symbol, HashSet<string> projectAssemblies)
      {
         var display = symbol.ToDisplayString(DeduplicationFormat);
         if (symbol is INamespaceSymbol)
            return new SymbolIdentity(display, null, null);

         var declaration = GetFirstDeclaration(symbol);
         return declaration is not null
            ? new SymbolIdentity(display, declaration, null)
            : new SymbolIdentity(display, null, GetProjectAssembly(symbol, projectAssemblies));
      }
   }

   /// <returns>
   /// The file path, in upper case, and the offset of the symbol's declaration that comes first by path and then by
   /// offset, or null for a symbol not in source.
   /// </returns>
   internal static string? GetFirstDeclaration(ISymbol symbol) =>
      symbol.Locations
         .Where(l => l.IsInSource && !string.IsNullOrEmpty(l.SourceTree?.FilePath))
         .Select(l => (Path: l.SourceTree?.FilePath.ToUpperInvariant() ?? "", l.SourceSpan.Start))
         .OrderBy(d => d.Path, StringComparer.Ordinal)
         .ThenBy(d => d.Start)
         .Select(d => $"{d.Path}:{d.Start}")
         .FirstOrDefault();

   /// <summary>
   /// Returns the assembly names of the solution's projects, in any letter case as assembly names compare.
   /// </summary>
   private static HashSet<string> GetProjectAssemblies(Solution solution) =>
      solution.Projects.Select(p => p.AssemblyName).ToHashSet(StringComparer.OrdinalIgnoreCase);

   /// <returns>The assembly name of the solution project that holds the symbol, or null for any other symbol.</returns>
   private static string? GetProjectAssembly(ISymbol symbol, HashSet<string> projectAssemblies) =>
      symbol is not INamespaceSymbol
      && symbol.ContainingAssembly is { } assembly
      && projectAssemblies.TryGetValue(assembly.Name, out var name)
         ? name
         : null;

   /// <summary>
   /// Removes each result from a project's assembly reference when the same project's source declares the same symbol.
   /// </summary>
   private static List<SymbolLookupResult<TSymbol>> PreferSourceOfProjectAssemblies<TSymbol>(
      List<SymbolLookupResult<TSymbol>> results,
      HashSet<string> projectAssemblies) where TSymbol : ISymbol
   {
      var declared = results
         .OfType<SourceSymbolResult<TSymbol>>()
         .Select(r => (GetProjectAssembly(r.Symbol, projectAssemblies), r.Symbol.ToDisplayString(DeduplicationFormat)))
         .ToHashSet();
      return results
         .Where(r => r is not MetadataSymbolResult<TSymbol>
            || !declared.Contains((GetProjectAssembly(r.Symbol, projectAssemblies), r.Symbol.ToDisplayString(DeduplicationFormat))))
         .ToList();
   }

   /// <summary>
   /// Drops each result that has a variant among the results without type parameters where the name gives no count, as
   /// C# binds <c>Task</c> to the non-generic type.
   /// </summary>
   /// <remarks>
   /// A variant has the same name, parameters, kind and project assembly, and the same type-parameter count at every
   /// other level. So <c>List</c> still finds <c>List&lt;T&gt;</c>, and a bare name never chooses between unrelated symbols.
   /// </remarks>
   private static IReadOnlyList<SymbolLookupResult<TSymbol>> PreferNonGeneric<TSymbol>(
      IReadOnlyList<SymbolLookupResult<TSymbol>> results,
      string[] segments,
      HashSet<string> projectAssemblies) where TSymbol : ISymbol
   {
      var dropped = results
         .Select(r => (Result: r, Levels: GetTypeParameterLevels(r.Symbol, segments)))
         .GroupBy(c => (
            GetProjectAssembly(c.Result.Symbol, projectAssemblies),
            c.Result.Symbol.Kind,
            DescribeCandidate(c.Result.Symbol, typeParameters: false).Text))
         .SelectMany(variants => variants.Where(c => variants.Any(other => IsPreferredVariant(other.Levels, c.Levels))))
         .Select(c => c.Result)
         .ToHashSet(ReferenceEqualityComparer.Instance);
      return results.Where(r => !dropped.Contains(r)).ToList();
   }

   /// <summary>
   /// A type-parameter count at one level of a symbol's full name.
   /// </summary>
   /// <param name="Count">The number of type parameters at the level.</param>
   /// <param name="Unspecified">Whether the looked-up name gives the level without a type-parameter count.</param>
   private readonly record struct TypeParameterLevel(int Count, bool Unspecified);

   /// <summary>
   /// Returns the type-parameter levels of the symbol and its containers, innermost first.
   /// </summary>
   /// <remarks>
   /// The segments match the levels from the last segment on. An extension block's header names its own type
   /// parameters, and a member's name may leave out its block, as <see cref="MatchesContainer"/> allows.
   /// </remarks>
   private static List<TypeParameterLevel> GetTypeParameterLevels(ISymbol symbol, string[] segments)
   {
      var levels = new List<TypeParameterLevel>();
      var segment = segments.Length - 1;
      for (var current = symbol; current is not (null or INamespaceSymbol { IsGlobalNamespace: true }); current = current.ContainingSymbol)
      {
         if (current is INamedTypeSymbol { IsExtension: true } block)
         {
            if (segment >= 0 && RoslynHelpers.IsExtensionBlockNamed(block, segments[segment]))
               segment--;
            levels.Add(new TypeParameterLevel(block.Arity, Unspecified: false));
            continue;
         }

         levels.Add(new TypeParameterLevel(GetTypeParameterCount(current), segment >= 0 && ParseSegment(segments[segment]).Arity is null));
         segment--;
      }

      return levels;
   }

   /// <summary>
   /// Returns whether a variant is preferred over another: it has no type parameters at each level where their counts
   /// differ, and the name gives each of those levels without a count.
   /// </summary>
   private static bool IsPreferredVariant(List<TypeParameterLevel> preferred, List<TypeParameterLevel> other)
   {
      if (preferred.Count != other.Count)
         return false;

      var differs = false;
      for (var i = 0; i < preferred.Count; i++)
      {
         if (preferred[i].Count == other[i].Count)
            continue;
         if (preferred[i].Count != 0 || !other[i].Unspecified)
            return false;
         differs = true;
      }

      return differs;
   }

   private static int GetTypeParameterCount(ISymbol symbol) => symbol switch
   {
      INamedTypeSymbol type => type.Arity,
      IMethodSymbol method => method.Arity,
      _ => 0
   };

   /// <summary>
   /// Find exactly one symbol. Throws if zero or multiple matches.
   /// </summary>
   /// <param name="filter">Optional predicate to filter results after lookup.</param>
   public async Task<SymbolLookupResult<TSymbol>> FindSingleAsync<TSymbol>(
      SymbolLookup lookup,
      Func<TSymbol, bool>? filter = null,
      CancellationToken cancellationToken = default) where TSymbol : ISymbol
   {
      var results = await FindAsync<TSymbol>(lookup, filter, cancellationToken);

      if (results.Count == 0)
      {
         throw new ToolException(
            ErrorCodes.SymbolNotFound,
            $"No {typeof(TSymbol).Name} found {FormatLookupForError(lookup)}");
      }

      if (results.Count > 1)
      {
         throw await AmbiguousSymbolErrorAsync("symbols", results.Select(r => (ISymbol)r.Symbol).ToList(), cancellationToken);
      }

      return results[0];
   }

   /// <summary>
   /// Find all source symbols matching the lookup criteria.
   /// Metadata symbols are silently filtered out.
   /// </summary>
   public async Task<IReadOnlyList<SourceSymbolResult<TSymbol>>> FindSourceSymbolsAsync<TSymbol>(
      SymbolLookup lookup,
      CancellationToken cancellationToken) where TSymbol : ISymbol
   {
      var results = await FindAsync<TSymbol>(lookup, filter: null, cancellationToken);
      return results
         .OfType<SourceSymbolResult<TSymbol>>()
         .ToList();
   }

   /// <summary>
   /// Find all source symbols matching the lookup criteria and filter.
   /// Metadata symbols are silently filtered out.
   /// </summary>
   public async Task<IReadOnlyList<SourceSymbolResult<TSymbol>>> FindSourceSymbolsAsync<TSymbol>(
      SymbolLookup lookup,
      Func<TSymbol, bool> filter,
      CancellationToken cancellationToken) where TSymbol : ISymbol
   {
      var results = await FindAsync(lookup, filter, cancellationToken);
      return results
         .OfType<SourceSymbolResult<TSymbol>>()
         .ToList();
   }

   /// <summary>
   /// Find exactly one source symbol. Throws if zero, multiple, or only metadata matches.
   /// </summary>
   public Task<SourceSymbolResult<TSymbol>> FindSingleSourceSymbolAsync<TSymbol>(
      SymbolLookup lookup,
      CancellationToken cancellationToken) where TSymbol : ISymbol
      => FindSingleSourceSymbolAsyncCore<TSymbol>(lookup, filter: null, cancellationToken);

   /// <summary>
   /// Find exactly one source symbol matching the filter. Throws if zero, multiple, or only metadata matches.
   /// </summary>
   public Task<SourceSymbolResult<TSymbol>> FindSingleSourceSymbolAsync<TSymbol>(
      SymbolLookup lookup,
      Func<TSymbol, bool> filter,
      CancellationToken cancellationToken) where TSymbol : ISymbol
      => FindSingleSourceSymbolAsyncCore(lookup, filter, cancellationToken);

   private async Task<SourceSymbolResult<TSymbol>> FindSingleSourceSymbolAsyncCore<TSymbol>(
      SymbolLookup lookup,
      Func<TSymbol, bool>? filter,
      CancellationToken cancellationToken) where TSymbol : ISymbol
   {
      var allResults = await FindAsync(lookup, filter, cancellationToken);

      if (allResults.Count == 0)
      {
         var description = FormatLookupForError(lookup);
         throw new ToolException(
            ErrorCodes.SymbolNotFound,
            $"No {typeof(TSymbol).Name} found {description}");
      }

      var sourceResults = allResults.OfType<SourceSymbolResult<TSymbol>>().ToList();

      if (sourceResults.Count == 0)
      {
         // All matches are metadata symbols
         var assemblies = allResults
            .OfType<MetadataSymbolResult<TSymbol>>()
            .Select(r => r.AssemblyName)
            .Distinct()
            .ToList();

         throw new ToolException(
            ErrorCodes.SymbolInMetadata,
            $"Symbol is defined in metadata, not source code",
            assemblies.Count == 1
               ? $"Symbol is from assembly '{assemblies[0]}'"
               : $"Symbol is from assemblies: {string.Join(", ", assemblies)}");
      }

      if (sourceResults.Count > 1)
      {
         throw await AmbiguousSymbolErrorAsync("source symbols", sourceResults.Select(r => (ISymbol)r.Symbol).ToList(), cancellationToken);
      }

      return sourceResults[0];
   }

   /// <summary>
   /// Find the enclosing symbol of the specified type at a position.
   /// Walks up the syntax tree to find a containing symbol (e.g., find the method containing a position).
   /// </summary>
   public Task<SourceSymbolResult<TSymbol>> FindEnclosingSymbolAsync<TSymbol>(
      LookupByPosition lookup,
      CancellationToken cancellationToken) where TSymbol : ISymbol
   {
      return FindEnclosingSymbolAsync<TSymbol>(lookup, filter: null, cancellationToken);
   }

   /// <summary>
   /// Find the enclosing symbol at a position with an optional filter predicate.
   /// Walks up the syntax tree to find a containing symbol that matches both the type and filter.
   /// </summary>
   /// <param name="filter">Optional predicate to further filter which symbols are acceptable.</param>
   public async Task<SourceSymbolResult<TSymbol>> FindEnclosingSymbolAsync<TSymbol>(
      LookupByPosition lookup,
      Func<TSymbol, bool>? filter,
      CancellationToken cancellationToken) where TSymbol : ISymbol
   {
      _workspaceManager.EnsureSolutionLoaded();

      var document = await _workspaceManager.GetDocumentAsync(lookup.FilePath, cancellationToken);

      var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
      var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

      if (semanticModel == null || syntaxTree == null)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            "Could not get semantic model or syntax tree");
      }

      var position = RoslynHelpers.GetPosition(syntaxTree, lookup.Line, lookup.Column);
      var root = await syntaxTree.GetRootAsync(cancellationToken);
      var token = root.FindToken(position);
      var node = token.Parent;

      // Walk up the tree to find an enclosing symbol of the requested type
      TSymbol? symbol = default;
      if (node != null)
      {
         foreach (var n in node.AncestorsAndSelf())
         {
            var declared = semanticModel.GetDeclaredSymbol(n, cancellationToken);
            if (declared is TSymbol typedSymbol && (filter == null || filter(typedSymbol)))
            {
               symbol = typedSymbol;
               break;
            }
         }
      }

      if (symbol == null)
      {
         throw new ToolException(
            ErrorCodes.SymbolNotFound,
            $"No enclosing {typeof(TSymbol).Name} found at {lookup.FilePath}:{lookup.Line}:{lookup.Column}",
            "Position must be within a symbol declaration of the requested type");
      }

      return new SourceSymbolResult<TSymbol>
      {
         Symbol = symbol,
         Document = document,
         Syntax = await GetDeclaringSyntaxAsync(symbol, cancellationToken)
      };
   }

   private async Task<IReadOnlyList<SymbolLookupResult<TSymbol>>> FindByPositionAsync<TSymbol>(
      LookupByPosition lookup,
      CancellationToken cancellationToken) where TSymbol : ISymbol
   {
      if (string.IsNullOrWhiteSpace(lookup.FilePath))
      {
         throw new ToolException(
            ErrorCodes.InvalidParameter,
            "File path cannot be empty",
            "Provide a valid file path");
      }

      var document = await _workspaceManager.GetDocumentAsync(lookup.FilePath, cancellationToken);

      var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
      var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

      if (semanticModel == null || syntaxTree == null)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            "Could not get semantic model or syntax tree");
      }

      var position = RoslynHelpers.GetPosition(syntaxTree, lookup.Line, lookup.Column);
      var root = await syntaxTree.GetRootAsync(cancellationToken);
      var token = root.FindToken(position);
      var node = token.Parent;

      // Try to find symbol at this position
      var symbol = RoslynHelpers.FindSymbolAtPosition(semanticModel, node);

      if (symbol == null && node != null)
      {
         symbol = FindDeclarationOwningPosition(semanticModel, node, cancellationToken);
      }

      if (symbol == null)
      {
         return [];
      }

      // Filter by requested type
      if (symbol is not TSymbol typedSymbol)
      {
         return [];
      }

      // Position-based lookup always returns source symbols
      var result = new SourceSymbolResult<TSymbol>
      {
         Symbol = typedSymbol,
         Document = document,
         Syntax = await GetDeclaringSyntaxAsync(symbol, cancellationToken)
      };

      return [result];
   }

   /// <summary>
   /// Returns the nearest declaration whose own syntax holds <paramref name="node"/>, such as its name, a modifier or
   /// its parameter list.
   /// </summary>
   /// <returns>
   /// The declared symbol, or null when the walk meets a value expression first. A declaration above an expression
   /// owns the expression, such as a local that owns its initializer, so it is not the symbol at the position.
   /// </returns>
   private static ISymbol? FindDeclarationOwningPosition(
      SemanticModel semanticModel,
      SyntaxNode node,
      CancellationToken cancellationToken)
   {
      foreach (var ancestor in node.AncestorsAndSelf())
      {
         if (semanticModel.GetDeclaredSymbol(ancestor, cancellationToken) is { } declared)
            return declared;

         if (ancestor is ExpressionSyntax expression && IsValueExpression(expression))
            return null;
      }

      return null;
   }

   /// <summary>
   /// Returns whether an expression computes a value, as opposed to naming a type or namespace, or naming the
   /// element in <c>name:</c> or <c>Name =</c>.
   /// </summary>
   private static bool IsValueExpression(ExpressionSyntax expression) =>
      !SyntaxFacts.IsInNamespaceOrTypeContext(expression)
      && expression.Parent is not (NameColonSyntax or NameEqualsSyntax);

   private async Task<IReadOnlyList<SymbolLookupResult<TSymbol>>> FindByNameAsync<TSymbol>(
      LookupByName lookup,
      CancellationToken cancellationToken) where TSymbol : ISymbol
   {
      var solution = _workspaceManager.EnsureSolutionLoaded();
      var name = lookup.Name;

      // Validate name is not empty or whitespace
      if (string.IsNullOrWhiteSpace(name))
      {
         throw new ToolException(
            ErrorCodes.InvalidParameter,
            "Symbol name cannot be empty or whitespace",
            "Provide a valid symbol name");
      }

      var segments = SplitQualifiedName(name);
      if (segments.Any(string.IsNullOrEmpty))
      {
         throw new ToolException(
            ErrorCodes.InvalidParameter,
            $"Invalid symbol name '{name}': contains empty segments (leading, trailing, or consecutive dots)",
            "Provide a valid symbol name without leading, trailing, or consecutive dots");
      }

      var results = new List<SymbolLookupResult<TSymbol>>();

      // Determine which projects to search
      var projects = lookup.Project != null
         ? solution.Projects.Where(p => string.Equals(p.Name, lookup.Project, StringComparison.OrdinalIgnoreCase))
         : solution.Projects;

      var projectAssemblies = GetProjectAssemblies(solution);
      var seen = new HashSet<SymbolIdentity>();

      foreach (var project in projects)
      {
         cancellationToken.ThrowIfCancellationRequested();

         var compilation = await project.GetCompilationAsync(cancellationToken);
         if (compilation == null)
            continue;

         var symbols = segments.Length == 1
            ? FindSymbolsBySimpleName<TSymbol>(compilation, segments[0])
            : ResolveProgressively<TSymbol>(compilation, segments);

         foreach (var typedSymbol in symbols)
         {
            if (!seen.Add(SymbolIdentity.Of(typedSymbol, projectAssemblies)))
               continue;

            results.Add(await CreateResultAsync(typedSymbol, solution, cancellationToken));
         }
      }

      results = PreferSourceOfProjectAssemblies(results, projectAssemblies);

      // Apply parameter/return type disambiguation filters
      if (lookup.ParameterTypes != null || lookup.ReturnType != null)
      {
         results = results
            .Where(r => MatchesSignatureFilter(r.Symbol, lookup.ParameterTypes, lookup.ReturnType))
            .ToList();
      }

      return results;
   }

   /// <summary>
   /// Find symbols by simple name using Compilation.GetSymbolsWithName.
   /// Much more efficient than walking the entire namespace tree.
   /// </summary>
   /// <remarks>
   /// Symbol filter mapping:
   /// - INamedTypeSymbol/ITypeSymbol → SymbolFilter.Type
   /// - INamespaceSymbol → SymbolFilter.Namespace
   /// - IMethodSymbol/IPropertySymbol/IFieldSymbol/IEventSymbol → SymbolFilter.Member
   /// - Other types (ILocalSymbol, IParameterSymbol, etc.) → SymbolFilter.All (deliberate fallback)
   /// </remarks>
   private static IEnumerable<TSymbol> FindSymbolsBySimpleName<TSymbol>(
      Compilation compilation,
      string simpleName) where TSymbol : ISymbol
   {
      var filter = typeof(TSymbol) switch
      {
         var t when t == typeof(INamedTypeSymbol) || t == typeof(ITypeSymbol) => SymbolFilter.Type,
         var t when t == typeof(INamespaceSymbol) => SymbolFilter.Namespace,
         var t when t == typeof(IMethodSymbol) || t == typeof(IPropertySymbol) ||
                    t == typeof(IFieldSymbol) || t == typeof(IEventSymbol) => SymbolFilter.Member,
         _ => SymbolFilter.All
      };

      return FindSourceSymbolsByName(compilation, ParseSegment(simpleName).Name, filter)
         .Where(symbol => SegmentMatches(symbol, simpleName))
         .OfType<TSymbol>();
   }

   private static readonly string[] AccessorPrefixes = ["get_", "set_", "add_", "remove_"];

   /// <summary>
   /// Returns the source symbols in the compilation with the name, other than extension implementation methods.
   /// </summary>
   /// <remarks>
   /// <see cref="Compilation.GetSymbolsWithName(Func{string, bool}, SymbolFilter, CancellationToken)"/> does not return
   /// accessors. So for an accessor's name, such as <c>get_Item</c>, this also searches for the members that can own
   /// the accessor: the property or event with the name after the prefix, and every indexer, because an indexer's
   /// accessors take its metadata name.
   ///
   /// An extension block has no name, so for a block header such as <c>extension(Grid)</c> this searches the blocks
   /// of every type.
   /// </remarks>
   private static IEnumerable<ISymbol> FindSourceSymbolsByName(Compilation compilation, string name, SymbolFilter filter)
   {
      var symbols = compilation.GetSymbolsWithName(candidate => string.Equals(candidate, name, StringComparison.Ordinal), filter);
      var prefix = AccessorPrefixes.FirstOrDefault(prefix => name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal));
      if (prefix is not null && filter.HasFlag(SymbolFilter.Member))
      {
         var ownerName = name[prefix.Length..];
         var accessors = compilation.GetSymbolsWithName(
               candidate => string.Equals(candidate, ownerName, StringComparison.Ordinal)
                  || string.Equals(candidate, WellKnownMemberNames.Indexer, StringComparison.Ordinal),
               SymbolFilter.Member)
            .SelectMany(GetAccessors)
            .Where(accessor => string.Equals(accessor.Name, name, StringComparison.Ordinal));
         symbols = symbols.Concat(accessors);
      }

      if (IsExtensionBlockHeader(name) && filter.HasFlag(SymbolFilter.Type))
      {
         var blocks = compilation.GetSymbolsWithName(_ => true, SymbolFilter.Type)
            .OfType<INamedTypeSymbol>()
            .SelectMany(type => type.GetTypeMembers())
            .Where(type => type.IsExtension && RoslynHelpers.IsExtensionBlockNamed(type, name));
         symbols = symbols.Concat(blocks);
      }

      return symbols.Where(symbol => !RoslynHelpers.IsExtensionImplementation(symbol));
   }

   private static bool IsExtensionBlockHeader(string name) =>
      name.StartsWith("extension(", StringComparison.Ordinal) || name.StartsWith("extension<", StringComparison.Ordinal);

   private static IEnumerable<IMethodSymbol> GetAccessors(ISymbol member) => member switch
   {
      IPropertySymbol property => new[] { property.GetMethod, property.SetMethod }.OfType<IMethodSymbol>(),
      IEventSymbol @event => new[] { @event.AddMethod, @event.RemoveMethod, @event.RaiseMethod }.OfType<IMethodSymbol>(),
      _ => []
   };

   /// <summary>
   /// Resolve a dotted name progressively through namespaces and types.
   /// Handles the ambiguity between namespace.type and type.nestedtype correctly.
   /// </summary>
   private static IEnumerable<TSymbol> ResolveProgressively<TSymbol>(
      Compilation compilation,
      string[] segments) where TSymbol : ISymbol
   {
      // Start resolution from global namespace
      var results = new List<ISymbol>();
      ResolveSegments(compilation.GlobalNamespace, segments, 0, results);

      // If progressive resolution found nothing, fall back to simple name search
      // with container filtering (handles cases like searching for "System.Console")
      if (results.Count == 0)
      {
         var simpleName = ParseSegment(segments[^1]).Name;
         var containerSegments = segments[..^1];

         foreach (var symbol in FindSourceSymbolsByName(compilation, simpleName, SymbolFilter.All))
         {
            if (SegmentMatches(symbol, segments[^1]) && MatchesContainer(symbol, containerSegments))
            {
               results.Add(symbol);
            }
         }
      }

      return results.OfType<TSymbol>();
   }

   /// <summary>
   /// Recursively resolve segments through namespaces and types.
   /// At each level, tries both namespace and type matches to handle ambiguity.
   /// </summary>
   private const int MaxResolutionDepth = 50;

   private static void ResolveSegments(
      INamespaceOrTypeSymbol container,
      string[] segments,
      int index,
      List<ISymbol> results)
   {
      if (index >= segments.Length)
         return;
      if (index > MaxResolutionDepth)
      {
         // Prevent stack overflow from excessively deep symbol paths
         return;
      }

      var segment = segments[index];
      var isLastSegment = index == segments.Length - 1;

      foreach (var member in GetMembersMatching(container, segment))
      {
         if (isLastSegment)
         {
            // Final segment - this is our result
            results.Add(member);
         }
         else if (member is INamespaceOrTypeSymbol namespaceOrType)
         {
            // Not final - continue resolution into namespaces or types
            ResolveSegments(namespaceOrType, segments, index + 1, results);
         }
         // If member is not a namespace/type and we're not at the last segment,
         // this path doesn't lead anywhere useful - skip it
      }
   }

   /// <remarks>
   /// The segment also matches the members of each extension block in a type, as <see cref="MatchesContainer"/> does.
   /// </remarks>
   private static IEnumerable<ISymbol> GetMembersMatching(INamespaceOrTypeSymbol container, string segment)
   {
      var name = ParseSegment(segment).Name;
      if (container is not INamedTypeSymbol type)
         return container.GetMembers(name).Where(member => SegmentMatches(member, segment));

      var blocks = type.GetTypeMembers().Where(member => member.IsExtension).ToList();
      var members = type.GetMembers(name)
         .Where(member => !RoslynHelpers.IsExtensionImplementation(member))
         .Concat(blocks.SelectMany(block => block.GetMembers(name)))
         .Where(member => SegmentMatches(member, segment));
      return members.Concat(blocks.Where(block => RoslynHelpers.IsExtensionBlockNamed(block, segment)));
   }

   /// <summary>
   /// Check if a symbol's containers end with the expected container segments.
   /// Used as fallback when progressive resolution doesn't find results.
   /// </summary>
   /// <remarks>
   /// An extension member matches with or without its extension block among the segments, so
   /// <c>Holder.Member</c> matches the member of every block in <c>Holder</c>.
   /// </remarks>
   private static bool MatchesContainer(ISymbol symbol, string[] containerSegments)
   {
      if (symbol.ContainingSymbol is INamedTypeSymbol { IsExtension: true } block)
      {
         var holders = GetNamedContainers(block.ContainingSymbol);
         return ContainersEndWith(holders, containerSegments)
            || (containerSegments.Length > 0
               && RoslynHelpers.IsExtensionBlockNamed(block, containerSegments[^1])
               && ContainersEndWith(holders, containerSegments[..^1]));
      }

      return ContainersEndWith(GetNamedContainers(symbol.ContainingSymbol), containerSegments);
   }

   /// <summary>
   /// Returns the namespaces and types that hold <paramref name="container"/>, and it, outermost first and without the
   /// global namespace.
   /// </summary>
   private static List<ISymbol> GetNamedContainers(ISymbol? container)
   {
      var containers = new List<ISymbol>();
      while (container is INamespaceOrTypeSymbol and not INamespaceSymbol { IsGlobalNamespace: true })
      {
         containers.Add(container);
         container = container.ContainingSymbol;
      }

      containers.Reverse();
      return containers;
   }

   /// <summary>
   /// Returns whether the last containers match the segments, one each, so <c>System</c> matches <c>Foo.System</c>
   /// but not <c>FileSystem</c>.
   /// </summary>
   private static bool ContainersEndWith(List<ISymbol> containers, string[] segments)
   {
      var offset = containers.Count - segments.Length;
      if (offset < 0)
         return false;

      for (var i = 0; i < segments.Length; i++)
      {
         if (!SegmentMatches(containers[offset + i], segments[i]))
            return false;
      }

      return true;
   }

   /// <summary>
   /// Returns whether a symbol has the name in a segment and, when the segment gives a type-parameter count as in
   /// <c>Box&lt;T&gt;</c> or <c>Box`1</c>, that many type parameters.
   /// </summary>
   /// <remarks>
   /// A segment without a count matches every count, and <see cref="PreferNonGeneric"/> then narrows the results.
   /// </remarks>
   private static bool SegmentMatches(ISymbol symbol, string segment)
   {
      if (symbol is INamedTypeSymbol { IsExtension: true } block)
         return RoslynHelpers.IsExtensionBlockNamed(block, segment);

      var (name, arity) = ParseSegment(segment);
      if (!string.Equals(symbol.Name, name, StringComparison.Ordinal))
         return false;

      return arity is not { } expected || expected == GetTypeParameterCount(symbol);
   }

   /// <summary>
   /// Splits a segment such as <c>Map&lt;K, V&gt;</c> or <c>Map`2</c> into its name and the number of type parameters
   /// it gives.
   /// </summary>
   /// <returns>The name, and the arity, or null when the segment gives no type-parameter count.</returns>
   private static (string Name, int? Arity) ParseSegment(string segment)
   {
      var tick = segment.LastIndexOf('`');
      if (tick > 0 && int.TryParse(segment.AsSpan(tick + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var count))
         return (segment[..tick], count);

      var open = segment.IndexOf('<');
      if (open <= 0 || !segment.EndsWith('>'))
         return (segment, null);

      var arity = 1;
      var depth = 0;
      foreach (var c in segment.AsSpan(open + 1, segment.Length - open - 2))
      {
         switch (c)
         {
            case '<' or '(' or '[':
               depth++;
               break;
            case '>' or ')' or ']':
               depth--;
               break;
            case ',' when depth == 0:
               arity++;
               break;
         }
      }

      return (segment[..open], arity);
   }

   /// <summary>
   /// Splits a dotted name at the dots outside brackets, so <c>Holder.extension(Geometry.Grid).this[]</c> has three
   /// segments.
   /// </summary>
   private static string[] SplitQualifiedName(string name)
   {
      var segments = new List<string>();
      var depth = 0;
      var start = 0;
      for (var i = 0; i < name.Length; i++)
      {
         switch (name[i])
         {
            case '<' or '(' or '[':
               depth++;
               break;
            case '>' or ')' or ']' when depth > 0:
               depth--;
               break;
            case '.' when depth == 0:
               segments.Add(name[start..i]);
               start = i + 1;
               break;
         }
      }

      segments.Add(name[start..]);
      return segments.ToArray();
   }

   private static async Task<SymbolLookupResult<TSymbol>> CreateResultAsync<TSymbol>(
      TSymbol symbol,
      Solution solution,
      CancellationToken cancellationToken) where TSymbol : ISymbol
   {
      // A compiler-generated accessor, such as one of a field-like event, has a source location but no syntax.
      var syntaxRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
      var syntaxTree = syntaxRef?.SyntaxTree ?? symbol.Locations.FirstOrDefault(location => location.IsInSource)?.SourceTree;

      if (syntaxTree is not null && solution.GetDocument(syntaxTree) is { } document)
      {
         return new SourceSymbolResult<TSymbol>
         {
            Symbol = symbol,
            Document = document,
            Syntax = syntaxRef is null ? null : await syntaxRef.GetSyntaxAsync(cancellationToken)
         };
      }

      // Metadata symbol
      var assemblyName = symbol.ContainingAssembly?.Name ?? "Unknown";
      return new MetadataSymbolResult<TSymbol>
      {
         Symbol = symbol,
         AssemblyName = assemblyName
      };
   }

   private static async Task<SyntaxNode?> GetDeclaringSyntaxAsync(ISymbol symbol, CancellationToken cancellationToken)
   {
      var syntaxRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
      return syntaxRef != null ? await syntaxRef.GetSyntaxAsync(cancellationToken) : null;
   }

   /// <summary>
   /// Checks if a symbol matches the given parameter types and return type filters.
   /// Only methods and indexers can match, and an indexer's type counts as its return type.
   /// </summary>
   private static bool MatchesSignatureFilter(ISymbol symbol, string[]? parameterTypes, string? returnType)
   {
      var (parameters, type) = symbol switch
      {
         IMethodSymbol method => (method.Parameters, method.ReturnType),
         IPropertySymbol { IsIndexer: true } indexer => (indexer.Parameters, indexer.Type),
         _ => ([], null)
      };
      if (type is null)
         return false;

      if (parameterTypes != null)
      {
         if (parameters.Length != parameterTypes.Length)
            return false;
         for (var i = 0; i < parameterTypes.Length; i++)
         {
            if (!ParameterMatches(parameters[i], parameterTypes[i]))
               return false;
         }
      }

      if (returnType != null)
      {
         if (!TypeNameMatches(type, returnType))
            return false;
      }

      return true;
   }

   /// <summary>
   /// Checks if a type symbol matches a user-provided type name.
   /// Supports C# keyword aliases (string ↔ System.String), simple names, and fully qualified names.
   /// </summary>
   private static bool TypeNameMatches(ITypeSymbol type, string name)
   {
      // Minimal format uses C# keywords: "string", "int", "List<string>"
      var minimalDisplay = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
      if (string.Equals(minimalDisplay, name, StringComparison.Ordinal))
         return true;

      // Simple name: "String", "Int32", "List"
      if (string.Equals(type.Name, name, StringComparison.Ordinal))
         return true;

      // Namespaced with keywords: "System.Collections.Generic.List<string>"
      var namespacedKeyword = type.ToDisplayString(NamespacedKeywordFormat);
      if (string.Equals(namespacedKeyword, name, StringComparison.Ordinal))
         return true;

      // Namespaced with CLR names: "System.String", "System.Collections.Generic.List<System.String>"
      var namespacedClr = type.ToDisplayString(NamespacedClrFormat);
      if (string.Equals(namespacedClr, name, StringComparison.Ordinal))
         return true;

      return false;
   }

   /// <summary>
   /// Checks if a parameter matches a user-provided type string, including optional modifier prefixes.
   /// When no modifier is specified, matches regardless of the parameter's actual modifier (backwards compatible).
   /// When a modifier is specified (e.g., "ref int"), matches only if the parameter has that exact modifier.
   /// </summary>
   private static bool ParameterMatches(IParameterSymbol parameter, string input)
   {
      var (expectedRefKind, expectedParams, typeName) = ParseParameterModifier(input);

      if (expectedRefKind is { } refKind)
      {
         var matches = parameter.RefKind == refKind
            || (refKind == RefKind.RefReadOnlyParameter && parameter.RefKind == RefKind.RefReadOnly);
         if (!matches)
            return false;
      }

      if (expectedParams is true && !parameter.IsParams)
         return false;

      return TypeNameMatches(parameter.Type, typeName);
   }

   /// <summary>
   /// Parses a modifier prefix (ref/out/in/params/ref readonly) from a user-provided parameter type string.
   /// Returns the expected RefKind, whether params is expected, and the remaining type name.
   /// </summary>
   private static (RefKind? RefKind, bool? IsParams, string TypeName) ParseParameterModifier(string input)
   {
      // Check multi-word modifiers first (order matters - "ref readonly" before "ref")
      if (input.StartsWith("ref readonly ", StringComparison.Ordinal))
         return (RefKind.RefReadOnlyParameter, null, input["ref readonly ".Length..]);

      if (input.StartsWith("ref ", StringComparison.Ordinal))
         return (RefKind.Ref, null, input["ref ".Length..]);

      if (input.StartsWith("out ", StringComparison.Ordinal))
         return (RefKind.Out, null, input["out ".Length..]);

      if (input.StartsWith("in ", StringComparison.Ordinal))
         return (RefKind.In, null, input["in ".Length..]);

      if (input.StartsWith("params ", StringComparison.Ordinal))
         return (null, true, input["params ".Length..]);

      return (null, null, input);
   }

   private static string FormatLookupForError(SymbolLookup lookup) => lookup switch
   {
      LookupByPosition pos => $"at {pos.FilePath}:{pos.Line}:{pos.Column}",
      LookupByName name => $"'{name.Name}'",
      _ => lookup.ToString() ?? "unknown"
   };

   /// <summary>
   /// Returns the error for a lookup that matched more than one symbol. It lists the candidates in the forms that
   /// <see cref="AmbiguityHint"/> tells a caller to look them up with.
   /// </summary>
   /// <remarks>
   /// When the name lookup for a candidate's listed form finds more than the candidate, the candidate also gets its
   /// declaration position, or else the project to look it up in.
   /// </remarks>
   private async Task<ToolException> AmbiguousSymbolErrorAsync(string kind, List<ISymbol> candidates, CancellationToken cancellationToken)
   {
      var listed = new List<string>();
      foreach (var candidate in candidates.Take(5))
      {
         var (text, lookup) = DescribeCandidate(candidate, typeParameters: true);
         if (await FindsOnlyAsync(lookup, candidate, cancellationToken))
            listed.Add(text);
         else if (await FormatDeclarationPositionAsync(candidate, cancellationToken) is { } position)
            listed.Add($"{text} at {position}");
         else if (await FindProjectThatFindsOnlyAsync(lookup, candidate, cancellationToken) is { } project)
            listed.Add($"{text} in project {project}");
         else
            listed.Add(text);
      }

      if (candidates.Count > 5)
         listed.Add($"and {candidates.Count - 5} more");

      return new ToolException(
         ErrorCodes.AmbiguousSymbol,
         $"Multiple {kind} found ({candidates.Count}): {string.Join("; ", listed)}",
         AmbiguityHint);
   }

   /// <returns>
   /// The name of a project that declares the symbol and in which the lookup finds the symbol and nothing else, or null
   /// when there is none.
   /// </returns>
   private async Task<string?> FindProjectThatFindsOnlyAsync(LookupByName lookup, ISymbol symbol, CancellationToken cancellationToken)
   {
      if (symbol.ContainingAssembly is not { } assembly)
         return null;

      var projects = _workspaceManager.EnsureSolutionLoaded().Projects
         .Where(p => string.Equals(p.AssemblyName, assembly.Name, StringComparison.OrdinalIgnoreCase));
      foreach (var project in projects)
      {
         var inProject = new LookupByName
         {
            Name = lookup.Name,
            ParameterTypes = lookup.ParameterTypes,
            ReturnType = lookup.ReturnType,
            Project = project.Name
         };
         if (await FindsOnlyAsync(inProject, symbol, cancellationToken))
            return project.Name;
      }

      return null;
   }

   /// <summary>
   /// Returns whether a lookup finds the symbol and nothing else.
   /// </summary>
   private async Task<bool> FindsOnlyAsync(SymbolLookup lookup, ISymbol symbol, CancellationToken cancellationToken)
   {
      IReadOnlyList<SymbolLookupResult<ISymbol>> found;
      try
      {
         found = await FindAsync<ISymbol>(lookup, filter: null, cancellationToken);
      }
      catch (ToolException)
      {
         return false;
      }

      var projectAssemblies = GetProjectAssemblies(_workspaceManager.EnsureSolutionLoaded());
      return found is [{ Symbol: var foundSymbol }]
         && SymbolIdentity.Of(foundSymbol, projectAssemblies) == SymbolIdentity.Of(symbol, projectAssemblies);
   }

   /// <summary>
   /// Describes a candidate by a name that a name lookup resolves, then its parameter types and, for a conversion, its
   /// return type, in the forms that parameterTypes and returnType accept.
   /// </summary>
   /// <param name="typeParameters">Whether the name lists the type parameters of its types and methods.</param>
   /// <returns>The description, and the name lookup that it stands for.</returns>
   private static (string Text, LookupByName Lookup) DescribeCandidate(ISymbol symbol, bool typeParameters)
   {
      var segments = new List<(string Name, string TypeParameters)>();
      for (var current = symbol; current is not (null or INamespaceSymbol { IsGlobalNamespace: true }); current = current.ContainingSymbol)
      {
         segments.Add(current switch
         {
            INamedTypeSymbol { IsExtension: true } block => (RoslynHelpers.GetFullyQualifiedExtensionBlockName(block), ""),
            INamedTypeSymbol type => (type.Name, FormatTypeParameters(type.TypeParameters)),
            IMethodSymbol method => (method.Name, FormatTypeParameters(method.TypeParameters)),
            _ => (current.Name, "")
         });
      }

      segments.Reverse();
      var name = string.Join(".", segments.Select(s => typeParameters ? s.Name + s.TypeParameters : s.Name));

      var (parameters, returnType) = symbol switch
      {
         IPropertySymbol { IsIndexer: true } indexer => (FormatParameters(indexer.Parameters), null),
         IMethodSymbol { MethodKind: MethodKind.Conversion } conversion =>
            (FormatParameters(conversion.Parameters), conversion.ReturnType.ToDisplayString(NamespacedKeywordFormat)),
         IMethodSymbol method => (FormatParameters(method.Parameters), null),
         _ => ((string[]?)null, (string?)null)
      };

      // An indexer's name, this[], takes its parameter list between the brackets.
      var text = parameters is null ? name
         : symbol is IPropertySymbol ? $"{name[..^1]}{string.Join(", ", parameters)}]"
         : $"{name}({string.Join(", ", parameters)})";
      if (returnType is not null)
         text += $" returns {returnType}";

      return (text, new LookupByName { Name = name, ParameterTypes = parameters, ReturnType = returnType });
   }

   private static string FormatTypeParameters(ImmutableArray<ITypeParameterSymbol> typeParameters) =>
      typeParameters.IsEmpty ? "" : $"<{string.Join(", ", typeParameters.Select(t => t.Name))}>";

   private static string[] FormatParameters(ImmutableArray<IParameterSymbol> parameters) =>
      parameters.Select(p => FormatParameterModifier(p) + p.Type.ToDisplayString(NamespacedKeywordFormat)).ToArray();

   /// <summary>
   /// Returns the modifier prefix that <see cref="ParseParameterModifier"/> reads back for the parameter.
   /// </summary>
   private static string FormatParameterModifier(IParameterSymbol parameter) => parameter.RefKind switch
   {
      RefKind.None => parameter.IsParams ? "params " : "",
      RefKind.Ref => "ref ",
      RefKind.Out => "out ",
      RefKind.In => "in ",
      RefKind.RefReadOnlyParameter => "ref readonly ",
      _ => throw new InvalidOperationException($"Unexpected ref kind: {parameter.RefKind}")
   };

   /// <returns>
   /// The file, line and column of the symbol's declared name, or null when a position lookup there does not find the
   /// symbol, as for a record's positional property, which the lookup finds as its constructor parameter.
   /// </returns>
   private async Task<string?> FormatDeclarationPositionAsync(ISymbol symbol, CancellationToken cancellationToken)
   {
      if (symbol.IsImplicitlyDeclared
         || symbol.Locations.FirstOrDefault(l => l.IsInSource) is not { SourceTree.FilePath: { } filePath } location)
         return null;

      var start = location.GetLineSpan().StartLinePosition;
      var lookup = new LookupByPosition { FilePath = filePath, Line = start.Line + 1, Column = start.Character + 1 };
      return await FindsOnlyAsync(lookup, symbol, cancellationToken)
         ? $"{_workspaceManager.FormatPath(filePath)}:{lookup.Line}:{lookup.Column}"
         : null;
   }
}
