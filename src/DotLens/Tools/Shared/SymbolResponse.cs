using System.Text.Json.Serialization;

namespace DotLens.Tools.Shared;

/// <summary>
/// Polymorphic base for symbol information in API responses.
/// The "kind" discriminator indicates the symbol type (class, interface, method, etc.).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ClassSymbolResponse), "class")]
[JsonDerivedType(typeof(InterfaceSymbolResponse), "interface")]
[JsonDerivedType(typeof(StructSymbolResponse), "struct")]
[JsonDerivedType(typeof(EnumSymbolResponse), "enum")]
[JsonDerivedType(typeof(DelegateSymbolResponse), "delegate")]
[JsonDerivedType(typeof(ExtensionSymbolResponse), "extension")]
[JsonDerivedType(typeof(MethodSymbolResponse), "method")]
[JsonDerivedType(typeof(ConstructorSymbolResponse), "constructor")]
[JsonDerivedType(typeof(PropertySymbolResponse), "property")]
[JsonDerivedType(typeof(FieldSymbolResponse), "field")]
[JsonDerivedType(typeof(EventSymbolResponse), "event")]
[JsonDerivedType(typeof(NamespaceSymbolResponse), "namespace")]
[JsonDerivedType(typeof(UnknownSymbolResponse), "unknown")]
public abstract record SymbolResponse
{
   // Core (always present) - Summary level
   public required string Name { get; init; }

   // Compact level
   public SymbolLocation? Location { get; init; }
   public IReadOnlyList<SymbolLocation>? Locations { get; init; }
   public string? ContainingType { get; init; }
   public AccessibilityDto? Accessibility { get; init; }
   public SymbolModifiers? Modifiers { get; init; }

   // Full level
   public string? FullyQualifiedName { get; init; }
   public string? ContainingNamespace { get; init; }
   public string? Documentation { get; init; }

   /// <summary>
   /// Type parameters for generic types/methods (Full verbosity only).
   /// Example: ["T", "TResult"] for a generic method.
   /// </summary>
   public IReadOnlyList<string>? TypeParameters { get; init; }

   /// <summary>
   /// Full declaration string with modifiers (Full verbosity only).
   /// Example: "public static async Task&lt;int&gt; MyMethod(string x)"
   /// </summary>
   public string? Declaration { get; init; }
}

// Type symbols

public record ClassSymbolResponse : SymbolResponse;
public record InterfaceSymbolResponse : SymbolResponse;
public record StructSymbolResponse : SymbolResponse;
public record EnumSymbolResponse : SymbolResponse;
public record DelegateSymbolResponse : SymbolResponse;

/// <summary>
/// A C# <c>extension(...)</c> block. Its <see cref="SymbolResponse.Name"/> is the block header without the
/// receiver's name, such as <c>extension(Grid)</c>, because the language gives the block no name.
/// </summary>
public record ExtensionSymbolResponse : SymbolResponse;

// Callable symbols (methods and constructors)

public record MethodSymbolResponse : SymbolResponse
{
   public string? Signature { get; init; }
   public string? ReturnType { get; init; }
}

public record ConstructorSymbolResponse : SymbolResponse
{
   public string? Signature { get; init; }
}

// Member symbols with a type

public record PropertySymbolResponse : SymbolResponse
{
   public string? Type { get; init; }
   public bool? HasGetter { get; init; }
   public bool? HasSetter { get; init; }
}

public record FieldSymbolResponse : SymbolResponse
{
   public string? Type { get; init; }
}

public record EventSymbolResponse : SymbolResponse
{
   public string? Type { get; init; }
}

// Other symbols

public record NamespaceSymbolResponse : SymbolResponse;

/// <summary>Fallback for symbol types not explicitly handled.</summary>
public record UnknownSymbolResponse : SymbolResponse;
