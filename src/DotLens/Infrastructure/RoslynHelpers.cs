using System.Text;
using System.Text.RegularExpressions;
using DotLens.Tools.Refactoring;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DotLens.Infrastructure;

using System.Text.Json.Serialization;

/// <summary>
/// Location information for a symbol in source code.
/// </summary>
public readonly record struct SourceLocation
{
   [JsonPropertyName("filePath")]
   public string FilePath { get; init; }

   [JsonPropertyName("line")]
   public int Line { get; init; }

   [JsonPropertyName("column")]
   public int Column { get; init; }
}

/// <summary>
/// Formatted member information with minimal allocations.
/// Uses nullable properties with JsonIgnore to omit unset values.
/// </summary>
public readonly record struct MemberInfo
{
   // Core properties (always present)
   [JsonPropertyName("name")]
   public string Name { get; init; }

   [JsonPropertyName("kind")]
   public string Kind { get; init; }

   // Compact verbosity properties
   [JsonPropertyName("accessibility")]
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
   public string? Accessibility { get; init; }

   [JsonPropertyName("isStatic")]
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
   public bool? IsStatic { get; init; }

   [JsonPropertyName("line")]
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
   public int? Line { get; init; }

   // Method-specific properties
   [JsonPropertyName("signature")]
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
   public string? Signature { get; init; }

   [JsonPropertyName("returnType")]
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
   public string? ReturnType { get; init; }

   [JsonPropertyName("isAsync")]
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
   public bool? IsAsync { get; init; }

   [JsonPropertyName("isVirtual")]
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
   public bool? IsVirtual { get; init; }

   [JsonPropertyName("isOverride")]
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
   public bool? IsOverride { get; init; }

   [JsonPropertyName("isAbstract")]
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
   public bool? IsAbstract { get; init; }

   // Property/Field/Event type
   [JsonPropertyName("type")]
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
   public string? Type { get; init; }

   // Property-specific
   [JsonPropertyName("hasGetter")]
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
   public bool? HasGetter { get; init; }

   [JsonPropertyName("hasSetter")]
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
   public bool? HasSetter { get; init; }

   // Field-specific
   [JsonPropertyName("isReadOnly")]
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
   public bool? IsReadOnly { get; init; }

   [JsonPropertyName("isConst")]
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
   public bool? IsConst { get; init; }

   // Full verbosity properties
   [JsonPropertyName("documentation")]
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
   public string? Documentation { get; init; }

   [JsonPropertyName("attributes")]
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
   public string[]? Attributes { get; init; }

   [JsonPropertyName("location")]
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
   public SourceLocation? Location { get; init; }

   [JsonPropertyName("containingType")]
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
   public string? ContainingType { get; init; }
}

/// <summary>
/// Result of a type lookup operation, including the compilation context.
/// </summary>
public sealed class TypeLookupResult
{
   public required INamedTypeSymbol Type { get; init; }
   public required Compilation Compilation { get; init; }
   public required Project Project { get; init; }
}

/// <summary>
/// Common Roslyn helper methods used across tool implementations.
/// </summary>
public static class RoslynHelpers
{
   /// <summary>
   /// Checks if a symbol has a specific attribute (by simple name).
   /// </summary>
   public static bool HasAttribute(ISymbol symbol, string attributeName)
   {
      return symbol.GetAttributes().Any(attr =>
          attr.AttributeClass?.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase) == true ||
          attr.AttributeClass?.Name.Equals($"{attributeName}Attribute", StringComparison.OrdinalIgnoreCase) == true);
   }

   /// <summary>
   /// Finds a type in a compilation using FQN or simple name matching.
   /// </summary>
   private static INamedTypeSymbol? FindTypeInCompilation(
       Compilation compilation,
       string typeName,
       bool requireFullyQualifiedName,
       CancellationToken cancellationToken)
   {
      // Strategy 1: Fully-qualified metadata name (most precise)
      var foundType = compilation.GetTypeByMetadataName(typeName);

      // Strategy 2: Search by simple name (case-insensitive exact match)
      // Only used when requireFullyQualifiedName is false
      if (foundType == null && !requireFullyQualifiedName)
      {
         var symbols = compilation.GetSymbolsWithName(
             name => name.Equals(typeName, StringComparison.OrdinalIgnoreCase),
             SymbolFilter.Type,
             cancellationToken);
         foundType = symbols.OfType<INamedTypeSymbol>().FirstOrDefault();
      }

      return foundType;
   }

   /// <summary>
   /// Finds a type by name across all projects in the solution, returning the compilation context.
   /// Throws ToolException if the type name is ambiguous (found in multiple projects with same FQN).
   /// Does NOT support partial/fuzzy matching - use search_symbols tool for that.
   /// </summary>
   /// <param name="solution">The solution to search.</param>
   /// <param name="typeName">The type name to search for.</param>
   /// <param name="requireFullyQualifiedName">
   /// If true, only matches fully-qualified names (e.g., "MyNamespace.MyClass").
   /// If false (default), also matches simple names with case-insensitive matching.
   /// Use true for precise operations like type compatibility checking.
   /// </param>
   /// <param name="projectName">
   /// Optional: limit search to a specific project by name. When provided:
   /// - Only searches that project (faster for large solutions)
   /// - No ambiguity check needed (explicit project selection)
   /// - Returns null if type not found in that project
   /// </param>
   /// <param name="cancellationToken">Cancellation token.</param>
   public static async Task<TypeLookupResult?> FindTypeWithCompilationAsync(
       Solution solution,
       string typeName,
       bool requireFullyQualifiedName = false,
       string? projectName = null,
       CancellationToken cancellationToken = default)
   {
      // Fast path: search specific project only
      if (projectName != null)
      {
         var project = solution.Projects.FirstOrDefault(p =>
             string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase));

         if (project == null)
            return null; // Project not found

         var compilation = await project.GetCompilationAsync(cancellationToken);
         if (compilation == null)
            return null;

         var foundType = FindTypeInCompilation(compilation, typeName, requireFullyQualifiedName, cancellationToken);
         if (foundType == null)
            return null;

         return new TypeLookupResult
         {
            Type = foundType,
            Compilation = compilation,
            Project = project
         };
      }

      // Full search: iterate all projects (with ambiguity detection)
      var matches = new List<TypeLookupResult>();

      foreach (var project in solution.Projects)
      {
         cancellationToken.ThrowIfCancellationRequested();
         var compilation = await project.GetCompilationAsync(cancellationToken);
         if (compilation == null)
            continue;

         var foundType = FindTypeInCompilation(compilation, typeName, requireFullyQualifiedName, cancellationToken);

         if (foundType != null)
         {
            matches.Add(new TypeLookupResult
            {
               Type = foundType,
               Compilation = compilation,
               Project = project
            });
         }
      }

      if (matches.Count == 0)
         return null;

      // Check for ambiguity:
      // 1. Same FQN defined in multiple independent projects (different assemblies)
      // 2. Multiple different FQNs matched (e.g., partial name matched multiple types)
      if (matches.Count > 1)
      {
         // Group by FQN to handle case where we find same type through project references
         var groupsByFqn = matches
             .GroupBy(m => m.Type.ToDisplayString())
             .ToList();

         foreach (var group in groupsByFqn)
         {
            // Within each FQN group, check if types are from different assemblies/projects
            // (i.e., independently defined, not visible via project references)
            // Fall back to project name when assembly name is null (e.g., in-memory compilations)
            var distinctIdentifiers = group
                .Select(m => m.Type.ContainingAssembly?.Name ?? m.Project.Name)
                .Distinct()
                .ToList();

            if (distinctIdentifiers.Count > 1)
            {
               var projectNames = string.Join(", ", group.Select(m => m.Project.Name));
               throw new ToolException(
                   ErrorCodes.AmbiguousType,
                   $"Ambiguous type name '{typeName}' found in multiple projects: {projectNames}",
                   "The same fully-qualified type name is defined independently in multiple projects");
            }
         }

         // Multiple different FQNs found - also ambiguous
         if (groupsByFqn.Count > 1)
         {
            var projectNames = string.Join(", ", matches.Select(m => m.Project.Name));
            throw new ToolException(
                ErrorCodes.AmbiguousType,
                $"Ambiguous type name '{typeName}' matches multiple types: {string.Join(", ", groupsByFqn.Select(g => g.Key))}",
                "Use fully-qualified type name to disambiguate");
         }
      }

      return matches.First();
   }

   /// <summary>
   /// Finds a type by name across all projects in the solution.
   /// Supports fully-qualified names, simple names, and common namespace prefixes.
   /// Throws ToolException if the type name is ambiguous (found in multiple projects with same FQN).
   /// </summary>
   public static async Task<INamedTypeSymbol?> FindTypeByNameAsync(
       Solution solution,
       string typeName,
       CancellationToken cancellationToken = default)
   {
      var result = await FindTypeWithCompilationAsync(solution, typeName, requireFullyQualifiedName: false, cancellationToken: cancellationToken);
      return result?.Type;
   }

   /// <summary>
   /// Finds the syntax node and document for a given symbol.
   /// </summary>
   public static async Task<(SyntaxNode? node, Document? document, SyntaxTree? syntaxTree)> FindMemberSyntaxAsync(
       Solution solution,
       ISymbol memberSymbol,
       CancellationToken cancellationToken = default)
   {
      var location = memberSymbol.Locations.FirstOrDefault(l => l.IsInSource);
      if (location?.SourceTree == null)
         return (null, null, null);

      var document = solution.GetDocument(location.SourceTree);
      if (document == null)
         return (null, null, null);

      var root = await location.SourceTree.GetRootAsync(cancellationToken);
      var node = root.FindNode(location.SourceSpan);

      return (node, document, location.SourceTree);
   }


   /// <summary>
   /// Converts 1-based line/column coordinates to a position in the syntax tree.
   /// Throws ToolException if the coordinates are out of range.
   /// </summary>
   public static int GetPosition(SyntaxTree syntaxTree, int line, int column)
   {
      var text = syntaxTree.GetText();

      // Convert from 1-based (API) to 0-based (Roslyn internal)
      var line0 = line - 1;
      var column0 = column - 1;

      if (line0 < 0 || line0 >= text.Lines.Count)
      {
         throw new ToolException(
             ErrorCodes.InvalidParameter,
             $"Line {line} is out of range. File has {text.Lines.Count} lines (1-{text.Lines.Count}).",
             "Use valid line/column numbers");
      }

      var lineText = text.Lines[line0];
      if (column0 < 0 || column0 > lineText.Span.Length)
      {
         throw new ToolException(
             ErrorCodes.InvalidParameter,
             $"Column {column} is out of range for line {line}. Line has {lineText.Span.Length + 1} characters (1-{lineText.Span.Length + 1}).",
             "Use valid line/column numbers");
      }

      var linePosition = new LinePosition(line0, column0);
      return text.Lines.GetPosition(linePosition);
   }

   /// <summary>
   /// Gets the end position from a PositionTarget if EndLine and EndColumn are both set.
   /// Returns null if either is not set.
   /// </summary>
   public static int? GetEndPosition(this PositionTarget pos, SyntaxTree syntaxTree)
       => pos.EndLine.HasValue && pos.EndColumn.HasValue
           ? GetPosition(syntaxTree, pos.EndLine.Value, pos.EndColumn.Value)
           : null;

   /// <summary>
   /// Finds the symbol at a given position in a document.
   /// Tries both GetSymbolInfo and GetDeclaredSymbol.
   /// </summary>
   /// <remarks>
   /// An argument list, the node of its brackets, parentheses and commas, resolves to the symbol of the node that owns
   /// the list, such as a call, an indexer or an attribute. A token inside an argument has the argument's own node, so
   /// it keeps its own symbol.
   /// </remarks>
   public static ISymbol? FindSymbolAtPosition(SemanticModel semanticModel, SyntaxNode? node)
   {
      if (node is BaseArgumentListSyntax or AttributeArgumentListSyntax && node.Parent is { } owner)
         node = owner;

      if (node == null)
         return null;

      var symbolInfo = semanticModel.GetSymbolInfo(node);
      var symbol = symbolInfo.Symbol;

      if (symbol == null)
      {
         // Try getting the declared symbol if we're on a declaration
         symbol = semanticModel.GetDeclaredSymbol(node);
      }

      return symbol;
   }

   /// <summary>
   /// Gets a human-readable string for a member's kind.
   /// </summary>
   public static string GetMemberKindString(ISymbol member)
   {
      return member switch
      {
         IMethodSymbol m when m.MethodKind == MethodKind.Ordinary => "Method",
         IMethodSymbol m when m.MethodKind == MethodKind.Constructor => "Constructor",
         IPropertySymbol => "Property",
         IFieldSymbol => "Field",
         IEventSymbol => "Event",
         _ => member.Kind.ToString()
      };
   }

   /// <summary>
   /// Formats a member for API responses with varying levels of detail.
   /// </summary>
   public static MemberInfo FormatMember(ISymbol member, ToolResultVerbosity verbosity, string? solutionFilePath, bool useAbsolutePaths)
   {
      var kind = GetMemberKindString(member);

      // Summary: minimal info
      if (verbosity == ToolResultVerbosity.Summary)
      {
         return new MemberInfo
         {
            Name = member.Name,
            Kind = kind
         };
      }

      // Get line number for source-defined symbols (1-based for output)
      var sourceLocation = member.Locations.FirstOrDefault(loc => loc.IsInSource);
      var line = sourceLocation?.GetLineNumber();

      // Build base compact info
      var info = new MemberInfo
      {
         Name = member.Name,
         Kind = kind,
         Accessibility = member.DeclaredAccessibility.ToString(),
         IsStatic = member.IsStatic,
         Line = line
      };

      // Add type-specific properties
      info = member switch
      {
         IMethodSymbol method => info with
         {
            Signature = method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            ReturnType = method.ReturnType.ToDisplayString(),
            IsAsync = method.IsAsync,
            IsVirtual = method.IsVirtual,
            IsOverride = method.IsOverride,
            IsAbstract = method.IsAbstract
         },
         IPropertySymbol property => info with
         {
            Type = property.Type.ToDisplayString(),
            HasGetter = property.GetMethod != null,
            HasSetter = property.SetMethod != null,
            IsVirtual = property.IsVirtual
         },
         IFieldSymbol field => info with
         {
            Type = field.Type.ToDisplayString(),
            IsReadOnly = field.IsReadOnly,
            IsConst = field.IsConst
         },
         IEventSymbol evt => info with
         {
            Type = evt.Type.ToDisplayString()
         },
         _ => info
      };

      // Full: add documentation, attributes, location
      if (verbosity == ToolResultVerbosity.Full)
      {
         var attrs = member.GetAttributes();
         string[]? attrNames = null;
         if (attrs.Length > 0)
         {
            attrNames = attrs
               .Select(a => a.AttributeClass?.Name ?? "")
               .Where(n => !string.IsNullOrEmpty(n))
               .ToArray();
         }

         SourceLocation? location = null;
         if (sourceLocation != null)
         {
            var lineSpan = sourceLocation.GetLineSpan();
            location = new SourceLocation
            {
               FilePath = PathUtils.FormatPath(lineSpan.Path, solutionFilePath, useAbsolutePaths),
               Line = lineSpan.StartLinePosition.Line + 1,
               Column = lineSpan.StartLinePosition.Character + 1
            };
         }

         info = info with
         {
            Documentation = member.GetDocumentationCommentXml() ?? "",
            Attributes = attrNames,
            Location = location,
            ContainingType = member.ContainingType?.ToDisplayString() ?? ""
         };
      }

      return info;
   }

   /// <summary>
   /// Strips common leading indentation from source code so the leftmost code starts at column 0.
   /// Useful for reducing token count in AI contexts.
   /// </summary>
   public static string CompactSourceOutput(string source)
   {
      if (string.IsNullOrEmpty(source))
         return source;

      // Detect newline style (default to \n if no newlines present)
      string newline = source.Contains("\r\n") ? "\r\n" : "\n";

      // Pass 1: Find minimum indentation across non-empty lines
      int minIndent = int.MaxValue;
      foreach (var line in source.AsSpan().EnumerateLines())
      {
         int indent = MeasureIndent(line);
         if (indent >= 0)
            minIndent = Math.Min(minIndent, indent);
      }

      if (minIndent is 0 or int.MaxValue)
         return source;

      // Pass 2: Build result with stripped indentation
      var sb = new StringBuilder(source.Length);
      bool first = true;
      foreach (var line in source.AsSpan().EnumerateLines())
      {
         if (!first)
            sb.Append(newline);
         first = false;

         if (!line.IsWhiteSpace())
            sb.Append(StripIndent(line, minIndent));
      }

      return sb.ToString();
   }

   private static int MeasureIndent(ReadOnlySpan<char> line)
   {
      if (line.IsWhiteSpace())
         return -1;

      int width = 0;
      foreach (char c in line)
      {
         if (c == ' ')
            width++;
         else if (c == '\t')
            width += 3;
         else
            return width;
      }
      return width;
   }

   private static ReadOnlySpan<char> StripIndent(ReadOnlySpan<char> line, int width)
   {
      int stripped = 0, chars = 0;
      foreach (char c in line)
      {
         if (stripped >= width)
            break;
         if (c == ' ')
         { stripped++; chars++; }
         else if (c == '\t')
         { stripped += 3; chars++; }
         else
            break;
      }
      return line[chars..];
   }

   /// <summary>
   /// Creates a SymbolResponse from a Roslyn ISymbol.
   /// Returns the appropriate derived type based on symbol kind.
   /// </summary>
   /// <param name="symbol">The symbol to format.</param>
   /// <param name="verbosity">Level of detail to include.</param>
   /// <param name="solutionFilePath">Optional solution file path for relative path formatting.</param>
   /// <param name="useAbsolutePaths">Whether to use absolute paths in output.</param>
   public static SymbolResponse CreateSymbolResponse(
      ISymbol symbol,
      ToolResultVerbosity verbosity,
      string? solutionFilePath,
      bool useAbsolutePaths)
   {
      var sourceLocation = symbol.Locations.FirstOrDefault(loc => loc.IsInSource);

      // Build location for Compact+ verbosity
      SymbolLocation? location = null;
      if (verbosity >= ToolResultVerbosity.Compact && sourceLocation != null)
      {
         var lineSpan = sourceLocation.GetLineSpan();
         location = new SymbolLocation
         {
            FilePath = PathUtils.FormatPath(lineSpan.Path, solutionFilePath, useAbsolutePaths),
            Line = lineSpan.StartLinePosition.Line + 1,
            Column = lineSpan.StartLinePosition.Character + 1
         };
      }

      // Build locations list for Full verbosity (for partial types)
      IReadOnlyList<SymbolLocation>? locations = null;
      if (verbosity == ToolResultVerbosity.Full)
      {
         var sourceLocations = symbol.Locations
            .Where(loc => loc.IsInSource)
            .Select(loc =>
            {
               var lineSpan = loc.GetLineSpan();
               return new SymbolLocation
               {
                  FilePath = PathUtils.FormatPath(lineSpan.Path, solutionFilePath, useAbsolutePaths),
                  Line = lineSpan.StartLinePosition.Line + 1,
                  Column = lineSpan.StartLinePosition.Character + 1
               };
            })
            .ToList();

         // Only populate Locations for partial types (multiple source locations)
         if (sourceLocations.Count > 1)
            locations = sourceLocations;
      }

      // Build modifiers
      SymbolModifiers? modifiers = null;
      if (verbosity >= ToolResultVerbosity.Compact)
      {
         modifiers = GetSymbolModifiers(symbol);
         if (modifiers == SymbolModifiers.None)
            modifiers = null; // Don't serialize empty modifiers
      }

      // Build accessibility
      AccessibilityDto? accessibility = null;
      if (verbosity >= ToolResultVerbosity.Compact)
      {
         accessibility = MapAccessibilityDto(symbol.DeclaredAccessibility);
      }

      // Get documentation for Full verbosity
      string? documentation = null;
      if (verbosity == ToolResultVerbosity.Full)
      {
         var xmlDoc = symbol.GetDocumentationCommentXml();
         if (!string.IsNullOrWhiteSpace(xmlDoc))
            documentation = xmlDoc;
      }

      // Helper for common properties
      string? containingType = verbosity >= ToolResultVerbosity.Compact ? symbol.ContainingType?.ToDisplayString() : null;
      string? fqn = verbosity == ToolResultVerbosity.Full ? symbol.ToDisplayString() : null;
      string? containingNs = verbosity == ToolResultVerbosity.Full ? symbol.ContainingNamespace?.ToDisplayString() : null;

      // Get type parameters for Full verbosity (applies to generic types and methods)
      IReadOnlyList<string>? typeParameters = null;
      if (verbosity == ToolResultVerbosity.Full)
      {
         var typeParams = symbol switch
         {
            INamedTypeSymbol namedType when namedType.TypeParameters.Length > 0
               => namedType.TypeParameters.Select(t => t.Name).ToList(),
            IMethodSymbol method when method.TypeParameters.Length > 0
               => method.TypeParameters.Select(t => t.Name).ToList(),
            _ => null
         };
         if (typeParams is { Count: > 0 })
            typeParameters = typeParams;
      }

      // Get declaration for Full verbosity
      string? declaration = verbosity == ToolResultVerbosity.Full ? GetDeclaration(symbol) : null;

      // Create the appropriate derived type based on symbol kind
      return symbol switch
      {
         // Types - each type kind gets its own response type
         INamedTypeSymbol { TypeKind: Microsoft.CodeAnalysis.TypeKind.Class } => new ClassSymbolResponse
         {
            Name = symbol.Name,
            Location = location,
            Locations = locations,
            ContainingType = containingType,
            Accessibility = accessibility,
            Modifiers = modifiers,
            FullyQualifiedName = fqn,
            ContainingNamespace = containingNs,
            Documentation = documentation,
            TypeParameters = typeParameters,
            Declaration = declaration
         },
         INamedTypeSymbol { TypeKind: Microsoft.CodeAnalysis.TypeKind.Interface } => new InterfaceSymbolResponse
         {
            Name = symbol.Name,
            Location = location,
            Locations = locations,
            ContainingType = containingType,
            Accessibility = accessibility,
            Modifiers = modifiers,
            FullyQualifiedName = fqn,
            ContainingNamespace = containingNs,
            Documentation = documentation,
            TypeParameters = typeParameters,
            Declaration = declaration
         },
         INamedTypeSymbol { TypeKind: Microsoft.CodeAnalysis.TypeKind.Struct } => new StructSymbolResponse
         {
            Name = symbol.Name,
            Location = location,
            Locations = locations,
            ContainingType = containingType,
            Accessibility = accessibility,
            Modifiers = modifiers,
            FullyQualifiedName = fqn,
            ContainingNamespace = containingNs,
            Documentation = documentation,
            TypeParameters = typeParameters,
            Declaration = declaration
         },
         INamedTypeSymbol { TypeKind: Microsoft.CodeAnalysis.TypeKind.Enum } => new EnumSymbolResponse
         {
            Name = symbol.Name,
            Location = location,
            Locations = locations,
            ContainingType = containingType,
            Accessibility = accessibility,
            Modifiers = modifiers,
            FullyQualifiedName = fqn,
            ContainingNamespace = containingNs,
            Documentation = documentation,
            Declaration = declaration
         },
         INamedTypeSymbol { TypeKind: Microsoft.CodeAnalysis.TypeKind.Delegate } => new DelegateSymbolResponse
         {
            Name = symbol.Name,
            Location = location,
            Locations = locations,
            ContainingType = containingType,
            Accessibility = accessibility,
            Modifiers = modifiers,
            FullyQualifiedName = fqn,
            ContainingNamespace = containingNs,
            Documentation = documentation,
            TypeParameters = typeParameters,
            Declaration = declaration
         },
         INamedTypeSymbol { TypeKind: Microsoft.CodeAnalysis.TypeKind.Extension } extension => new ExtensionSymbolResponse
         {
            Name = GetExtensionBlockName(extension),
            Location = location,
            Locations = locations,
            ContainingType = containingType,
            Accessibility = accessibility,
            Modifiers = modifiers,
            FullyQualifiedName = fqn,
            ContainingNamespace = containingNs,
            Documentation = documentation,
            TypeParameters = typeParameters,
            Declaration = declaration
         },
         // Fallback for other named types (e.g., TypeKind.Error)
         INamedTypeSymbol => new ClassSymbolResponse
         {
            Name = symbol.Name,
            Location = location,
            Locations = locations,
            ContainingType = containingType,
            Accessibility = accessibility,
            Modifiers = modifiers,
            FullyQualifiedName = fqn,
            ContainingNamespace = containingNs,
            Documentation = documentation,
            TypeParameters = typeParameters,
            Declaration = declaration
         },

         // Methods and constructors
         IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } ctor => new ConstructorSymbolResponse
         {
            Name = symbol.Name,
            Location = location,
            Locations = locations,
            ContainingType = containingType,
            Accessibility = accessibility,
            Modifiers = modifiers,
            FullyQualifiedName = fqn,
            ContainingNamespace = containingNs,
            Documentation = documentation,
            Signature = verbosity >= ToolResultVerbosity.Compact ? ctor.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) : null,
            Declaration = declaration
         },
         IMethodSymbol method => new MethodSymbolResponse
         {
            Name = symbol.Name,
            Location = location,
            Locations = locations,
            ContainingType = containingType,
            Accessibility = accessibility,
            Modifiers = modifiers,
            FullyQualifiedName = fqn,
            ContainingNamespace = containingNs,
            Documentation = documentation,
            TypeParameters = typeParameters,
            Signature = verbosity >= ToolResultVerbosity.Compact ? method.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) : null,
            ReturnType = verbosity >= ToolResultVerbosity.Compact ? method.ReturnType.ToDisplayString() : null,
            Declaration = declaration
         },

         // Properties, fields, events
         IPropertySymbol property => new PropertySymbolResponse
         {
            Name = symbol.Name,
            Location = location,
            Locations = locations,
            ContainingType = containingType,
            Accessibility = accessibility,
            Modifiers = modifiers,
            FullyQualifiedName = fqn,
            ContainingNamespace = containingNs,
            Documentation = documentation,
            Type = verbosity >= ToolResultVerbosity.Compact ? property.Type.ToDisplayString() : null,
            HasGetter = verbosity >= ToolResultVerbosity.Compact ? property.GetMethod != null : null,
            HasSetter = verbosity >= ToolResultVerbosity.Compact ? property.SetMethod != null : null,
            Declaration = declaration
         },
         IFieldSymbol field => new FieldSymbolResponse
         {
            Name = symbol.Name,
            Location = location,
            Locations = locations,
            ContainingType = containingType,
            Accessibility = accessibility,
            Modifiers = modifiers,
            FullyQualifiedName = fqn,
            ContainingNamespace = containingNs,
            Documentation = documentation,
            Type = verbosity >= ToolResultVerbosity.Compact ? field.Type.ToDisplayString() : null,
            Declaration = declaration
         },
         IEventSymbol evt => new EventSymbolResponse
         {
            Name = symbol.Name,
            Location = location,
            Locations = locations,
            ContainingType = containingType,
            Accessibility = accessibility,
            Modifiers = modifiers,
            FullyQualifiedName = fqn,
            ContainingNamespace = containingNs,
            Documentation = documentation,
            Type = verbosity >= ToolResultVerbosity.Compact ? evt.Type.ToDisplayString() : null,
            Declaration = declaration
         },

         // Namespaces
         INamespaceSymbol => new NamespaceSymbolResponse
         {
            Name = symbol.Name,
            Location = location,
            Locations = locations,
            FullyQualifiedName = fqn,
            Declaration = declaration
         },

         // Default fallback for unhandled symbol types
         _ => new UnknownSymbolResponse
         {
            Name = symbol.Name,
            Location = location,
            Locations = locations,
            ContainingType = containingType,
            Accessibility = accessibility,
            Modifiers = modifiers,
            FullyQualifiedName = fqn,
            ContainingNamespace = containingNs,
            Documentation = documentation,
            Declaration = declaration
         }
      };
   }

   /// <summary>
   /// Returns the header of an extension block without the receiver's name, such as <c>extension(scoped ref Meter)</c>.
   /// </summary>
   private static string GetExtensionBlockName(INamedTypeSymbol extension)
   {
      // Roslyn's display keeps the receiver's ref and scoped modifiers but always prefixes the containing type.
      var display = extension.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
      var containerPrefix = extension.ContainingType is { } container
         ? container.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) + "."
         : "";
      return display.StartsWith(containerPrefix, StringComparison.Ordinal) ? display[containerPrefix.Length..] : display;
   }

   /// <summary>
   /// Gets SymbolModifiers flags from a Roslyn ISymbol.
   /// </summary>
   public static SymbolModifiers GetSymbolModifiers(ISymbol symbol)
   {
      var modifiers = SymbolModifiers.None;

      if (symbol.IsStatic)
         modifiers |= SymbolModifiers.Static;
      if (symbol.IsVirtual)
         modifiers |= SymbolModifiers.Virtual;
      if (symbol.IsOverride)
         modifiers |= SymbolModifiers.Override;
      if (symbol.IsAbstract)
         modifiers |= SymbolModifiers.Abstract;
      if (symbol.IsSealed)
         modifiers |= SymbolModifiers.Sealed;
      if (symbol.IsExtern)
         modifiers |= SymbolModifiers.Extern;

      // Method-specific modifiers
      if (symbol is IMethodSymbol method && method.IsAsync)
         modifiers |= SymbolModifiers.Async;

      // Type-specific modifiers
      if (symbol is INamedTypeSymbol namedType)
      {
         // Check for partial keyword in syntax - Roslyn doesn't have IsPartial property.
         // Cannot use DeclaringSyntaxReferences.Length > 1 because a partial class
         // may exist in a single file with only one declaration.
         if (namedType.DeclaringSyntaxReferences.Any(r =>
            r.GetSyntax() is TypeDeclarationSyntax decl &&
            decl.Modifiers.Any(SyntaxKind.PartialKeyword)))
            modifiers |= SymbolModifiers.Partial;
         if (namedType.IsRecord)
            modifiers |= SymbolModifiers.Record;
         if (namedType.IsReadOnly)
            modifiers |= SymbolModifiers.ReadOnly;
      }

      // Field-specific modifiers
      if (symbol is IFieldSymbol field)
      {
         if (field.IsReadOnly)
            modifiers |= SymbolModifiers.ReadOnly;
         if (field.IsConst)
            modifiers |= SymbolModifiers.Const;
         if (field.IsVolatile)
            modifiers |= SymbolModifiers.Volatile;
         if (field.IsRequired)
            modifiers |= SymbolModifiers.Required;
      }

      // Property-specific modifiers
      if (symbol is IPropertySymbol property)
      {
         if (property.IsRequired)
            modifiers |= SymbolModifiers.Required;
      }

      return modifiers;
   }

   /// <summary>
   /// Maps Roslyn Accessibility to our AccessibilityDto enum.
   /// </summary>
   public static AccessibilityDto MapAccessibilityDto(Microsoft.CodeAnalysis.Accessibility accessibility)
   {
      return accessibility switch
      {
         Microsoft.CodeAnalysis.Accessibility.Public => AccessibilityDto.Public,
         Microsoft.CodeAnalysis.Accessibility.Internal => AccessibilityDto.Internal,
         Microsoft.CodeAnalysis.Accessibility.Protected => AccessibilityDto.Protected,
         Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal => AccessibilityDto.ProtectedInternal,
         Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal => AccessibilityDto.PrivateProtected,
         Microsoft.CodeAnalysis.Accessibility.Private => AccessibilityDto.Private,
         _ => AccessibilityDto.Private
      };
   }

   /// <summary>
   /// Gets a full declaration string for a symbol including accessibility and modifiers.
   /// </summary>
   internal static string GetDeclaration(ISymbol symbol)
   {
      // For members, Roslyn's format includes accessibility and modifiers
      if (symbol is IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol)
         return symbol.ToDisplayString(RoslynDisplayFormats.MethodSignatureCompact);

      // For types, SymbolDisplayMemberOptions don't apply - must build manually
      if (symbol is INamedTypeSymbol type)
         return BuildTypeDeclaration(type);

      // For namespaces
      if (symbol is INamespaceSymbol ns)
         return $"namespace {ns.ToDisplayString()}";

      return symbol.ToDisplayString();
   }

   /// <summary>
   /// Format for type names with generics (no accessibility/modifiers).
   /// </summary>
   private static readonly SymbolDisplayFormat TypeNameFormat = new(
      typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
      genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

   private static string BuildTypeDeclaration(INamedTypeSymbol type)
   {
      var sb = new System.Text.StringBuilder();

      // Accessibility
      sb.Append(type.DeclaredAccessibility switch
      {
         Microsoft.CodeAnalysis.Accessibility.Public => "public ",
         Microsoft.CodeAnalysis.Accessibility.Internal => "internal ",
         Microsoft.CodeAnalysis.Accessibility.Protected => "protected ",
         Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal => "protected internal ",
         Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal => "private protected ",
         Microsoft.CodeAnalysis.Accessibility.Private => "private ",
         _ => ""
      });

      // Modifiers
      if (type.IsStatic)
         sb.Append("static ");
      if (type.IsAbstract && type.TypeKind == TypeKind.Class && !type.IsStatic)
         sb.Append("abstract ");
      if (type.IsSealed && type.TypeKind == TypeKind.Class && !type.IsStatic)
         sb.Append("sealed ");
      if (type.IsReadOnly && type.TypeKind == TypeKind.Struct)
         sb.Append("readonly ");
      if (type.IsRefLikeType)
         sb.Append("ref ");
      if (type.IsRecord)
         sb.Append("record ");

      // Type keyword
      sb.Append(type.TypeKind switch
      {
         TypeKind.Class when !type.IsRecord => "class ",
         TypeKind.Struct => "struct ",
         TypeKind.Interface => "interface ",
         TypeKind.Enum => "enum ",
         TypeKind.Delegate => "delegate ",
         _ => ""
      });

      // Name with type parameters (use Roslyn for this part)
      sb.Append(type.ToDisplayString(TypeNameFormat));

      return sb.ToString().TrimEnd();
   }

}
