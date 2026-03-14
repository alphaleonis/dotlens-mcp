using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotLens.Infrastructure.SymbolLookup;

/// <summary>
/// Unified service for looking up symbols across the solution.
/// Supports position-based and name-based lookups.
/// </summary>
public class SymbolLookupService
{
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
      _workspaceManager.EnsureSolutionLoaded();

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

      return results;
   }

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
         var symbols = results.Select(r => FormatSymbolForError(r.Symbol)).ToList();
         throw new ToolException(
            ErrorCodes.AmbiguousSymbol,
            $"Multiple symbols found ({results.Count}): {string.Join(", ", symbols.Take(5))}" +
            (symbols.Count > 5 ? $" and {symbols.Count - 5} more" : ""),
            "Use parameterTypes/returnType in name mode to disambiguate overloads, or use position mode");
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
         var symbols = sourceResults.Select(r => FormatSymbolForError(r.Symbol)).ToList();
         throw new ToolException(
            ErrorCodes.AmbiguousSymbol,
            $"Multiple source symbols found ({sourceResults.Count}): {string.Join(", ", symbols.Take(5))}" +
            (symbols.Count > 5 ? $" and {symbols.Count - 5} more" : ""),
            "Use parameterTypes/returnType in name mode to disambiguate overloads, or use position mode");
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

      if (symbol == null)
      {
         // Try walking up the tree to find a declaration
         symbol = node?.AncestorsAndSelf()
            .Select(n => semanticModel.GetDeclaredSymbol(n, cancellationToken))
            .FirstOrDefault(s => s != null);
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

      var results = new List<SymbolLookupResult<TSymbol>>();

      // Determine which projects to search
      var projects = lookup.Project != null
         ? solution.Projects.Where(p => string.Equals(p.Name, lookup.Project, StringComparison.OrdinalIgnoreCase))
         : solution.Projects;

      // Track symbols we've already added (by display string) to avoid duplicates across projects
      var seen = new HashSet<string>();

      foreach (var project in projects)
      {
         cancellationToken.ThrowIfCancellationRequested();

         var compilation = await project.GetCompilationAsync(cancellationToken);
         if (compilation == null)
            continue;

         IEnumerable<TSymbol> symbols;

         if (!name.Contains('.'))
         {
            // Simple name: use GetSymbolsWithName for efficiency
            symbols = FindSymbolsBySimpleName<TSymbol>(compilation, name);
         }
         else
         {
            // Dotted pattern: use progressive resolution
            symbols = ResolveProgressively<TSymbol>(compilation, name);
         }

         foreach (var typedSymbol in symbols)
         {
            // Deduplicate by display string (same symbol from different project references)
            var key = typedSymbol.ToDisplayString(DeduplicationFormat);
            if (!seen.Add(key))
               continue;

            results.Add(await CreateResultAsync(typedSymbol, solution, cancellationToken));
         }
      }

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

      return compilation.GetSymbolsWithName(
         name => string.Equals(name, simpleName, StringComparison.Ordinal),
         filter).OfType<TSymbol>();
   }

   /// <summary>
   /// Resolve a dotted name progressively through namespaces and types.
   /// Handles the ambiguity between namespace.type and type.nestedtype correctly.
   /// </summary>
   private static IEnumerable<TSymbol> ResolveProgressively<TSymbol>(
      Compilation compilation,
      string dottedName) where TSymbol : ISymbol
   {
      var segments = dottedName.Split('.');

      // Validate: no empty segments (from leading/trailing/consecutive dots)
      if (segments.Any(string.IsNullOrEmpty))
      {
         throw new ToolException(
            ErrorCodes.InvalidParameter,
            $"Invalid symbol name '{dottedName}': contains empty segments (leading, trailing, or consecutive dots)",
            "Provide a valid symbol name without leading, trailing, or consecutive dots");
      }

      // Start resolution from global namespace
      var results = new List<ISymbol>();
      ResolveSegments(compilation.GlobalNamespace, segments, 0, results);

      // If progressive resolution found nothing, fall back to simple name search
      // with container filtering (handles cases like searching for "System.Console")
      if (results.Count == 0)
      {
         var simpleName = segments[^1];
         var containerPattern = string.Join(".", segments[..^1]);

         foreach (var symbol in compilation.GetSymbolsWithName(
            name => string.Equals(name, simpleName, StringComparison.Ordinal),
            SymbolFilter.All))
         {
            if (MatchesContainer(symbol, containerPattern))
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

      // Get all members matching this segment name
      var members = container.GetMembers(segment);

      foreach (var member in members)
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

   /// <summary>
   /// Check if a symbol's containing path matches the expected container pattern.
   /// Used as fallback when progressive resolution doesn't find results.
   /// </summary>
   private static bool MatchesContainer(ISymbol symbol, string containerPattern)
   {
      // Build the container path for this symbol
      var container = symbol.ContainingSymbol;
      var parts = new List<string>();

      while (container != null && container is not IAssemblySymbol)
      {
         if (!string.IsNullOrEmpty(container.Name))
         {
            parts.Add(container.Name);
         }
         container = container.ContainingSymbol;
      }

      parts.Reverse();
      var actualContainer = string.Join(".", parts);

      // Check if the pattern matches the actual container at segment boundaries
      // E.g., pattern "System" matches "System" or "Foo.System" but not "FileSystem"
      if (string.Equals(actualContainer, containerPattern, StringComparison.Ordinal))
         return true;

      // Check for segment-boundary match (must be preceded by a dot)
      if (containerPattern.Length > 0 && actualContainer.EndsWith("." + containerPattern, StringComparison.Ordinal))
         return true;

      return false;
   }

   private static async Task<SymbolLookupResult<TSymbol>> CreateResultAsync<TSymbol>(
      TSymbol symbol,
      Solution solution,
      CancellationToken cancellationToken) where TSymbol : ISymbol
   {
      // Check if symbol is from source
      var syntaxRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();

      if (syntaxRef != null)
      {
         var document = solution.GetDocument(syntaxRef.SyntaxTree);
         if (document != null)
         {
            var syntax = await syntaxRef.GetSyntaxAsync(cancellationToken);
            return new SourceSymbolResult<TSymbol>
            {
               Symbol = symbol,
               Document = document,
               Syntax = syntax
            };
         }
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
   /// Only method symbols can match; non-method symbols are filtered out.
   /// </summary>
   private static bool MatchesSignatureFilter(ISymbol symbol, string[]? parameterTypes, string? returnType)
   {
      if (symbol is not IMethodSymbol method)
         return false;

      if (parameterTypes != null)
      {
         if (method.Parameters.Length != parameterTypes.Length)
            return false;
         for (var i = 0; i < parameterTypes.Length; i++)
         {
            if (!ParameterMatches(method.Parameters[i], parameterTypes[i]))
               return false;
         }
      }

      if (returnType != null)
      {
         if (!TypeNameMatches(method.ReturnType, returnType))
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

   private static string FormatSymbolForError(ISymbol symbol)
   {
      return symbol switch
      {
         IMethodSymbol method => $"{method.ContainingType?.Name}.{method.Name}({string.Join(", ", method.Parameters.Select(p => p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)))})",
         IPropertySymbol prop => $"{prop.ContainingType?.Name}.{prop.Name}",
         IFieldSymbol field => $"{field.ContainingType?.Name}.{field.Name}",
         INamedTypeSymbol type => type.ToDisplayString(),
         _ => symbol.ToDisplayString()
      };
   }
}
