using DotLens.Infrastructure;
using ModelContextProtocol.Server;
using Xunit;

namespace DotLens.Tests.Infrastructure;

public class ValidationAttributesTests
{
   // Test class with [PositiveInteger] on a property
   private class TestInput
   {
      [PositiveInteger]
      public int MaxResults { get; set; }
   }

   [Fact]
   public void SchemaTransform_AddsMinimumForPositiveIntegerProperty()
   {
      // Use the production SchemaCreateOptions, not a duplicate
      var tool = McpServerTool.Create(
         (TestInput input) => "test",
         new McpServerToolCreateOptions
         {
            Name = "test_tool",
            SchemaCreateOptions = ToolRegistrationExtensions.SchemaCreateOptions
         });

      // Schema structure: {"properties":{"input":{"properties":{"maxResults":{"type":"integer","minimum":1}}}}}
      var schema = tool.ProtocolTool.InputSchema;
      var inputSchema = schema.GetProperty("properties").GetProperty("input");
      var maxResultsSchema = inputSchema.GetProperty("properties").GetProperty("maxResults");

      Assert.True(maxResultsSchema.TryGetProperty("minimum", out var minimum));
      Assert.Equal(1, minimum.GetInt32());
   }

   // Test class without [PositiveInteger]
   private class TestInputNoAttr
   {
      public int RegularValue { get; set; }
   }

   [Fact]
   public void SchemaTransform_DoesNotAddMinimumForRegularIntProperty()
   {
      // Use the production SchemaCreateOptions - class without [PositiveInteger]
      var tool = McpServerTool.Create(
         (TestInputNoAttr input) => "test",
         new McpServerToolCreateOptions
         {
            Name = "test_tool",
            SchemaCreateOptions = ToolRegistrationExtensions.SchemaCreateOptions
         });

      // Schema structure: {"properties":{"input":{"properties":{"regularValue":{"type":"integer"}}}}}
      var schema = tool.ProtocolTool.InputSchema;
      var inputSchema = schema.GetProperty("properties").GetProperty("input");
      var regularValueSchema = inputSchema.GetProperty("properties").GetProperty("regularValue");

      Assert.False(regularValueSchema.TryGetProperty("minimum", out _));
   }

   // Test class with [NonNegative] on a property
   private class TestInputNonNegative
   {
      [NonNegative]
      public int Offset { get; set; }
   }

   [Fact]
   public void SchemaTransform_AddsMinimumZeroForNonNegativeProperty()
   {
      var tool = McpServerTool.Create(
         (TestInputNonNegative input) => "test",
         new McpServerToolCreateOptions
         {
            Name = "test_tool",
            SchemaCreateOptions = ToolRegistrationExtensions.SchemaCreateOptions
         });

      var schema = tool.ProtocolTool.InputSchema;
      var inputSchema = schema.GetProperty("properties").GetProperty("input");
      var offsetSchema = inputSchema.GetProperty("properties").GetProperty("offset");

      Assert.True(offsetSchema.TryGetProperty("minimum", out var minimum));
      Assert.Equal(0, minimum.GetInt32());
   }

}
