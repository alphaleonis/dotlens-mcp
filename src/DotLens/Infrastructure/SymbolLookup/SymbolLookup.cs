using System.ComponentModel;
using System.Text.Json.Serialization;

namespace DotLens.Infrastructure.SymbolLookup;

/// <summary>
/// Polymorphic base type for symbol lookup parameters.
/// Use "mode" discriminator to specify lookup strategy.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "mode")]
[JsonDerivedType(typeof(LookupByPosition), "position")]
[JsonDerivedType(typeof(LookupByName), "name")]
public abstract class SymbolLookup { }

/// <summary>
/// Locate symbol by file position. Best when you have cursor coordinates from an editor.
/// </summary>
[Description("Locate symbol by file position.")]
public class LookupByPosition : SymbolLookup
{
   [Description("File path (absolute or solution-relative)")]
   public required string FilePath { get; set; }

   [Description("Line number")]
   [PositiveInteger]
   public required int Line { get; set; }

   [Description("Column number")]
   [PositiveInteger]
   public required int Column { get; set; }
}

/// <summary>
/// Locate symbol by name. Accepts type names, member paths, or FQNs.
/// For overloaded methods and indexers, use parameterTypes and/or returnType to disambiguate.
/// </summary>
[Description("Locate by name. Use parameterTypes/returnType for overloads.")]
public class LookupByName : SymbolLookup
{
   [Description("Symbol name, e.g. 'MyClass.Method', 'MyClass.this[]' or 'MyExtensions.extension(Receiver).Method'")]
   public required string Name { get; set; }

   [Description("Limit to specific project")]
   public string? Project { get; set; }

   [Description("Param types (ordered) for overload matching. Use ref/out/in/params prefix for modifiers.")]
   public string[]? ParameterTypes { get; set; }

   [Description("Filter by return type")]
   public string? ReturnType { get; set; }
}
