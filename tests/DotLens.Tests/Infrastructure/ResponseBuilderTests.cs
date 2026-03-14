using System.Text.Json;
using System.Text.Json.Nodes;
using DotLens.Infrastructure;
using DotLens.Infrastructure.Results;
using Xunit;

namespace DotLens.Tests.Infrastructure;

public class ResponseBuilderTests
{
   [Fact]
   public void Success_WithData_ReturnsCorrectStructure()
   {
      var testData = new { name = "test", value = 42 };
      var result = ToolResult.Create(testData);

      var json = JsonNode.Parse(JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase))!;
      Assert.NotNull(json["data"]);  // Success indicated by presence of data
      Assert.Equal("test", json["data"]!.GetStringOrNull("name"));
      Assert.Equal(42, json["data"]!.GetIntOrNull("value"));
   }

   [Fact]
   public void Success_WithMetadata_IncludesAllMetadataFields()
   {
      var testData = new { name = "test" };
      var result = ToolResult.Create(
          testData,
          suggestedNextTools: ["tool1", "tool2"],
          totalCount: 100,
          returnedCount: 10,
          verbosity: ToolResultVerbosity.Full);

      var json = JsonNode.Parse(JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase))!;
      Assert.NotNull(json["data"]);  // Success indicated by presence of data
      Assert.NotNull(json["metadata"]);

      var metadata = json["metadata"]!;
      Assert.Equal(100, metadata.GetIntOrNull("totalCount"));
      Assert.Equal(10, metadata.GetIntOrNull("returnedCount"));
      Assert.True(metadata.GetBoolOrNull("truncated")); // 10 < 100
      Assert.Equal("full", metadata.GetStringOrNull("verbosity"));
      Assert.Equal(2, (metadata["suggestedNextTools"]?.AsArray())?.Count);
   }

   [Fact]
   public void Success_WithMetadata_SetsNotTruncatedWhenReturnedEqualsTotal()
   {
      var result = ToolResult.Create(
          new { },
          suggestedNextTools: null,
          totalCount: 10,
          returnedCount: 10);

      var json = JsonNode.Parse(JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase))!;
      var metadata = json["metadata"]!;
      Assert.False(metadata.GetBoolOrNull("truncated"));
   }
}

public class ToolExceptionTests
{
   [Fact]
   public void ToolException_StoresCodeAndMessage()
   {
      var ex = new ToolException("TEST_ERROR", "Something went wrong");

      Assert.Equal("TEST_ERROR", ex.Code);
      Assert.Equal("Something went wrong", ex.Message);
      Assert.Null(ex.Hint);
      Assert.Null(ex.Context);
   }

   [Fact]
   public void ToolException_WithHint_StoresHint()
   {
      var ex = new ToolException("TEST_ERROR", "Something went wrong", "Try doing X instead");

      Assert.Equal("TEST_ERROR", ex.Code);
      Assert.Equal("Something went wrong", ex.Message);
      Assert.Equal("Try doing X instead", ex.Hint);
      Assert.Null(ex.Context);
   }

   [Fact]
   public void ToolException_WithContext_StoresContext()
   {
      var context = new { filePath = "test.cs", line = 10 };
      var ex = new ToolException("PARSE_ERROR", "Failed to parse", context: context);

      Assert.Equal("PARSE_ERROR", ex.Code);
      Assert.Equal("Failed to parse", ex.Message);
      Assert.Null(ex.Hint);
      Assert.NotNull(ex.Context);
   }

   [Fact]
   public void ToolException_IsException()
   {
      var ex = new ToolException("ERROR", "Message");
      Assert.IsAssignableFrom<Exception>(ex);
   }
}
