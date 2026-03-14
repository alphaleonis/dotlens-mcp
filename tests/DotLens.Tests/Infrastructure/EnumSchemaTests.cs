using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using DotLens.Infrastructure;
using DotLens.Tools.Shared;
using ModelContextProtocol;
using Xunit;

namespace DotLens.Tests.Infrastructure;

public class EnumSchemaTests
{
   [Fact]
   public void ToolResultVerbosity_Schema_UsesCamelCase()
   {
      // Use DotLens JSON options which configure camelCase enum converter
      var options = DotLensJsonOptions.Default;

      // Generate schema
      var schema = options.GetJsonSchemaAsNode(typeof(ToolResultVerbosity));
      var schemaJson = schema.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

      // Verify the schema has enum values in camelCase
      Assert.Contains("\"summary\"", schemaJson);
      Assert.Contains("\"compact\"", schemaJson);
      Assert.Contains("\"full\"", schemaJson);

      // Verify PascalCase values are NOT in the schema
      Assert.DoesNotContain("\"Summary\"", schemaJson);
      Assert.DoesNotContain("\"Compact\"", schemaJson);
      Assert.DoesNotContain("\"Full\"", schemaJson);
   }

   [Fact]
   public void ToolResultVerbosity_Serialization_UsesCamelCase()
   {
      var options = DotLensJsonOptions.Default;

      Assert.Equal("\"summary\"", JsonSerializer.Serialize(ToolResultVerbosity.Summary, options));
      Assert.Equal("\"compact\"", JsonSerializer.Serialize(ToolResultVerbosity.Compact, options));
      Assert.Equal("\"full\"", JsonSerializer.Serialize(ToolResultVerbosity.Full, options));
   }

   [Fact]
   public void ToolResultVerbosity_Deserialization_AcceptsCamelCase()
   {
      var options = DotLensJsonOptions.Default;

      // camelCase (expected format)
      Assert.Equal(ToolResultVerbosity.Summary, JsonSerializer.Deserialize<ToolResultVerbosity>("\"summary\"", options));
      Assert.Equal(ToolResultVerbosity.Compact, JsonSerializer.Deserialize<ToolResultVerbosity>("\"compact\"", options));
      Assert.Equal(ToolResultVerbosity.Full, JsonSerializer.Deserialize<ToolResultVerbosity>("\"full\"", options));
   }

   [Fact]
   public void SymbolModifiers_FlagsEnum_SerializesAsCamelCase()
   {
      var options = DotLensJsonOptions.Default;

      // Single flags
      Assert.Equal("\"static\"", JsonSerializer.Serialize(SymbolModifiers.Static, options));
      Assert.Equal("\"virtual\"", JsonSerializer.Serialize(SymbolModifiers.Virtual, options));
      Assert.Equal("\"abstract\"", JsonSerializer.Serialize(SymbolModifiers.Abstract, options));

      // Combined flags - should serialize as comma-separated camelCase
      var combined = SymbolModifiers.Static | SymbolModifiers.Async;
      var serialized = JsonSerializer.Serialize(combined, options);
      Assert.Contains("static", serialized);
      Assert.Contains("async", serialized);

      // Verify round-trip for combined flags
      var deserialized = JsonSerializer.Deserialize<SymbolModifiers>(serialized, options);
      Assert.Equal(combined, deserialized);
   }

   [Fact]
   public void SymbolModifiers_None_SerializesCorrectly()
   {
      var options = DotLensJsonOptions.Default;

      var serialized = JsonSerializer.Serialize(SymbolModifiers.None, options);
      Assert.Equal("\"none\"", serialized);

      var deserialized = JsonSerializer.Deserialize<SymbolModifiers>(serialized, options);
      Assert.Equal(SymbolModifiers.None, deserialized);
   }

   [Fact]
   public void ModifierFilter_Schema_UsesCamelCase()
   {
      var options = DotLensJsonOptions.Default;

      var schema = options.GetJsonSchemaAsNode(typeof(ModifierFilter));
      var schemaJson = schema.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

      // Verify camelCase values
      Assert.Contains("\"static\"", schemaJson);
      Assert.Contains("\"virtual\"", schemaJson);
      Assert.Contains("\"abstract\"", schemaJson);
      Assert.Contains("\"async\"", schemaJson);
      Assert.Contains("\"record\"", schemaJson);

      // Verify ReadOnly uses explicit [JsonStringEnumMemberName("readonly")] - lowercase, no camelCase
      Assert.Contains("\"readonly\"", schemaJson);
      Assert.DoesNotContain("\"readOnly\"", schemaJson);

      // Verify PascalCase values are NOT in the schema
      Assert.DoesNotContain("\"Static\"", schemaJson);
      Assert.DoesNotContain("\"Virtual\"", schemaJson);
      Assert.DoesNotContain("\"ReadOnly\"", schemaJson);
   }

   [Fact]
   public void ModifierFilter_Serialization_RoundTrips()
   {
      var options = DotLensJsonOptions.Default;

      Assert.Equal("\"static\"", JsonSerializer.Serialize(ModifierFilter.Static, options));
      Assert.Equal("\"readonly\"", JsonSerializer.Serialize(ModifierFilter.ReadOnly, options));
      Assert.Equal("\"async\"", JsonSerializer.Serialize(ModifierFilter.Async, options));

      // Round-trip
      Assert.Equal(ModifierFilter.Static, JsonSerializer.Deserialize<ModifierFilter>("\"static\"", options));
      Assert.Equal(ModifierFilter.ReadOnly, JsonSerializer.Deserialize<ModifierFilter>("\"readonly\"", options));
   }

   [Fact]
   public void AccessibilityDto_ExplicitAttributes_TakePrecedenceOverCamelCase()
   {
      var options = DotLensJsonOptions.Default;

      // Values with [JsonStringEnumMemberName] should use the explicit name (with space)
      Assert.Equal("\"protected internal\"", JsonSerializer.Serialize(AccessibilityDto.ProtectedInternal, options));
      Assert.Equal("\"private protected\"", JsonSerializer.Serialize(AccessibilityDto.PrivateProtected, options));

      // Values without explicit attributes should use camelCase
      Assert.Equal("\"public\"", JsonSerializer.Serialize(AccessibilityDto.Public, options));
      Assert.Equal("\"private\"", JsonSerializer.Serialize(AccessibilityDto.Private, options));

      // Verify round-trip for compound modifiers
      Assert.Equal(AccessibilityDto.ProtectedInternal,
         JsonSerializer.Deserialize<AccessibilityDto>("\"protected internal\"", options));
      Assert.Equal(AccessibilityDto.PrivateProtected,
         JsonSerializer.Deserialize<AccessibilityDto>("\"private protected\"", options));
   }
}
