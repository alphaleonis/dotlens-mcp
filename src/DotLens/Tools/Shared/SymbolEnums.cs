using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;

namespace DotLens.Tools.Shared;

/// <summary>
/// Accessibility level for API response types.
/// Dto suffix avoids collision with Microsoft.CodeAnalysis.Accessibility.
/// </summary>
public enum AccessibilityDto
{
   Public,
   Internal,
   Protected,

   /// <summary>Serializes as "protected internal" (with space, matching C# syntax).</summary>
   [JsonStringEnumMemberName("protected internal")]
   ProtectedInternal,

   /// <summary>Serializes as "private protected" (with space, matching C# syntax).</summary>
   [JsonStringEnumMemberName("private protected")]
   PrivateProtected,

   Private
}

/// <summary>
/// Sort order for symbol search results.
/// </summary>
public enum SymbolSortBy
{
   /// <summary>Default order (by file, then position).</summary>
   Default,

   /// <summary>Sort alphabetically by symbol name.</summary>
   Name,

   /// <summary>Sort by symbol kind (types first, then members).</summary>
   Kind
}

/// <summary>
/// Modifiers that can be applied to symbols.
/// Flags enum for compact representation.
/// </summary>
[Flags]
public enum SymbolModifiers
{
   None = 0,
   Static = 1 << 0,
   Virtual = 1 << 1,
   Override = 1 << 2,
   Abstract = 1 << 3,
   Sealed = 1 << 4,
   Async = 1 << 5,
   Partial = 1 << 6,
   ReadOnly = 1 << 7,
   Const = 1 << 8,
   Volatile = 1 << 9,
   Extern = 1 << 10,
   Required = 1 << 11,
   Record = 1 << 12, // record class or record struct
   Union = 1 << 13 // union declaration, or a struct or class with the Union attribute
}

/// <summary>
/// Filter for symbol modifiers in search operations.
/// Non-flags enum for use as array elements (AND semantics: symbol must have ALL specified modifiers).
/// </summary>
/// <remarks>
/// Separate from <see cref="SymbolModifiers"/> (flags enum) because:
/// - No <c>None</c> value (null/empty array means no filter)
/// - Better schema as array elements (no ambiguity between flags and array)
/// - Follows existing pattern: Accessibility -> AccessibilityDto, SymbolKind -> SymbolKindFilter
/// </remarks>
public enum ModifierFilter
{
   Static,
   Virtual,
   Override,
   Abstract,
   Sealed,
   Async,
   Partial,

   [JsonStringEnumMemberName("readonly")]
   ReadOnly,

   Const,
   Volatile,
   Extern,
   Required,
   Record,
   Union
}

/// <summary>
/// Modifiers for method parameters.
/// Only includes actual C# keywords that appear on parameters.
/// </summary>
[Flags]
public enum ParameterModifiers
{
   None = 0,
   In = 1 << 0,      // in keyword (readonly ref)
   Out = 1 << 1,     // out keyword
   Ref = 1 << 2,     // ref keyword
   Params = 1 << 3   // params keyword
}

/// <summary>
/// Type kinds for named types.
/// Maps from Roslyn's TypeKind enum to a simplified DTO representation.
/// </summary>
/// <remarks>
/// Note: Records are represented as Class or Struct depending on whether
/// they are record classes or record structs. Unions likewise keep their Struct or Class kind.
/// </remarks>
public enum TypeKindDto
{
   Class,
   Interface,
   Struct,
   Enum,
   Delegate,
   Module,
   Extension
}

/// <summary>
/// Types of changes for impact analysis.
/// </summary>
public enum ChangeType
{
   /// <summary>Rename the symbol.</summary>
   Rename,

   /// <summary>Change the type of a field, property, or return type.</summary>
   ChangeType,

   /// <summary>Add a parameter to a method.</summary>
   AddParameter,

   /// <summary>Remove a parameter from a method.</summary>
   RemoveParameter,

   /// <summary>Change the accessibility (public, private, etc.).</summary>
   ChangeAccessibility,

   /// <summary>Delete the symbol entirely.</summary>
   Delete
}

/// <summary>
/// Symbol kind for API response types.
/// Dto suffix avoids collision with Microsoft.CodeAnalysis.SymbolKind.
/// </summary>
/// <remarks>
/// Records are represented as Class or Struct (their underlying TypeKind).
/// Use SymbolModifiers.Record to check if a type is a record.
/// Unions keep their Struct or Class kind too, and SymbolModifiers.Union marks them.
/// </remarks>
public enum SymbolKindDto
{
   // Types
   Class,
   Interface,
   Struct,
   Enum,
   Delegate,
   Extension,

   // Members
   Method,
   Property,
   Field,
   Event,
   Constructor,

   // Other
   Namespace,
   Parameter,
   TypeParameter,
   Local,

   /// <summary>Unknown or unsupported symbol kind.</summary>
   Unknown
}

/// <summary>
/// How a symbol is referenced at a particular location.
/// </summary>
public enum ReferenceKind
{
   /// <summary>The symbol's value is read.</summary>
   Read,

   /// <summary>The symbol is assigned or mutated.</summary>
   Write,

   /// <summary>The symbol is invoked or instantiated.</summary>
   Call
}

/// <summary>
/// Extension methods for converting Roslyn symbols to DTO types.
/// </summary>
public static class SymbolKindExtensions
{
   /// <summary>
   /// Converts a Roslyn symbol to its DTO kind representation.
   /// </summary>
   public static SymbolKindDto ToSymbolKindDto(this ISymbol symbol)
   {
      return symbol switch
      {
         INamedTypeSymbol namedType => namedType.TypeKind switch
         {
            TypeKind.Class => SymbolKindDto.Class,
            TypeKind.Interface => SymbolKindDto.Interface,
            TypeKind.Struct => SymbolKindDto.Struct,
            TypeKind.Enum => SymbolKindDto.Enum,
            TypeKind.Delegate => SymbolKindDto.Delegate,
            TypeKind.Extension => SymbolKindDto.Extension,
            _ => SymbolKindDto.Unknown
         },
         IMethodSymbol method => method.MethodKind == MethodKind.Constructor ||
                                 method.MethodKind == MethodKind.StaticConstructor
            ? SymbolKindDto.Constructor
            : SymbolKindDto.Method,
         IPropertySymbol => SymbolKindDto.Property,
         IFieldSymbol => SymbolKindDto.Field,
         IEventSymbol => SymbolKindDto.Event,
         INamespaceSymbol => SymbolKindDto.Namespace,
         IParameterSymbol => SymbolKindDto.Parameter,
         ITypeParameterSymbol => SymbolKindDto.TypeParameter,
         ILocalSymbol => SymbolKindDto.Local,
         _ => SymbolKindDto.Unknown
      };
   }

   /// <summary>
   /// Converts a Roslyn TypeKind to its DTO representation.
   /// </summary>
   /// <exception cref="ArgumentOutOfRangeException">
   /// Thrown for type kinds that don't represent named types (Array, Pointer, Dynamic, etc.).
   /// </exception>
   public static TypeKindDto ToTypeKindDto(this TypeKind typeKind)
   {
      return typeKind switch
      {
         TypeKind.Class => TypeKindDto.Class,
         TypeKind.Interface => TypeKindDto.Interface,
         TypeKind.Struct => TypeKindDto.Struct,
         TypeKind.Enum => TypeKindDto.Enum,
         TypeKind.Delegate => TypeKindDto.Delegate,
         TypeKind.Module => TypeKindDto.Module,
         TypeKind.Extension => TypeKindDto.Extension,
         _ => throw new ArgumentOutOfRangeException(
            nameof(typeKind),
            typeKind,
            $"TypeKind '{typeKind}' cannot be converted to TypeKindDto. " +
            "Only Class, Interface, Struct, Enum, Delegate, Module, and Extension are supported.")
      };
   }

   /// <summary>
   /// Gets the parameter modifiers for a parameter symbol.
   /// </summary>
   /// <returns>ParameterModifiers flags, or null if no modifiers are present.</returns>
   public static ParameterModifiers? GetParameterModifiers(this IParameterSymbol parameter)
   {
      var modifiers = ParameterModifiers.None;

      if (parameter.RefKind == RefKind.In)
         modifiers |= ParameterModifiers.In;
      else if (parameter.RefKind == RefKind.Out)
         modifiers |= ParameterModifiers.Out;
      else if (parameter.RefKind is RefKind.Ref or RefKind.RefReadOnly or RefKind.RefReadOnlyParameter)
         modifiers |= ParameterModifiers.Ref;

      if (parameter.IsParams)
         modifiers |= ParameterModifiers.Params;

      return modifiers == ParameterModifiers.None ? null : modifiers;
   }

   /// <summary>
   /// Gets the default value expression for a parameter as a string.
   /// </summary>
   /// <returns>The default value as a string, or null if no default value.</returns>
   public static string? GetDefaultValueString(this IParameterSymbol parameter)
   {
      if (!parameter.HasExplicitDefaultValue)
         return null;

      var defaultValue = parameter.ExplicitDefaultValue;
      return defaultValue switch
      {
         null => "null",
         string s => $"\"{s}\"",
         char c => $"'{c}'",
         bool b => b ? "true" : "false",
         _ => defaultValue.ToString()
      };
   }
}
