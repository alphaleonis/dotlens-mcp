using System.Text.Json;
using ModelContextProtocol.Protocol;
using Xunit;

namespace DotLens.Tests.Integration;

/// <summary>
/// E2E tests that verify parameter validation attributes ([PositiveInteger], [NonNegative])
/// are enforced at the MCP protocol level.
/// </summary>
public class ParameterValidationE2ETests
{
   #region [PositiveInteger] Tests

   [Theory]
   [InlineData("abc")]
   [InlineData("1.5")]
   [InlineData("true")]
   public async Task PositiveInteger_RejectsNonIntegerValues(object invalidValue)
   {
      await using var fixture = await DotLensMcpFixture.CreateAsync(workerMode: true);

      var result = await fixture.Client.CallToolAsync(
         "search_symbols",
         new Dictionary<string, object?>
         {
            ["query"] = "*",
            ["maxResults"] = invalidValue
         });

      // The MCP SDK may throw or return an error for type mismatches
      Assert.True(result.IsError, "Expected error for non-integer value");
   }

   [Fact]
   public async Task PositiveInteger_RejectsZero()
   {
      await using var fixture = await DotLensMcpFixture.CreateAsync(workerMode: true);

      var result = await fixture.Client.CallToolAsync(
         "search_symbols",
         new Dictionary<string, object?>
         {
            ["query"] = "*",
            ["maxResults"] = 0
         });

      Assert.True(result.IsError, "Expected validation error for zero value");
      var error = ParseErrorResponse(result);
      Assert.Equal("INVALID_PARAMETER", error.Code);
   }

   [Fact]
   public async Task PositiveInteger_RejectsNegative()
   {
      await using var fixture = await DotLensMcpFixture.CreateAsync(workerMode: true);

      var result = await fixture.Client.CallToolAsync(
         "search_symbols",
         new Dictionary<string, object?>
         {
            ["query"] = "*",
            ["maxResults"] = -1
         });

      Assert.True(result.IsError, "Expected validation error for negative value");
      var error = ParseErrorResponse(result);
      Assert.Equal("INVALID_PARAMETER", error.Code);
   }

   [Theory]
   [InlineData(1)]
   [InlineData(10)]
   [InlineData(100)]
   public async Task PositiveInteger_AcceptsPositiveValues(int validValue)
   {
      await using var fixture = await DotLensMcpFixture.CreateAsync(workerMode: true);

      var result = await fixture.Client.CallToolAsync(
         "search_symbols",
         new Dictionary<string, object?>
         {
            ["query"] = "*",
            ["maxResults"] = validValue
         });

      // The call will fail because no solution is loaded, but it should NOT be a validation error
      // A validation error would have code "INVALID_PARAMETER"
      if (result.IsError == true)
      {
         var error = TryParseErrorResponse(result);
         Assert.NotEqual("INVALID_PARAMETER", error?.Code ?? "");
      }
   }

   #endregion

   #region [NonNegative] Tests

   [Theory]
   [InlineData("abc")]
   [InlineData("1.5")]
   [InlineData("true")]
   public async Task NonNegative_RejectsNonIntegerValues(object invalidValue)
   {
      await using var fixture = await DotLensMcpFixture.CreateAsync(workerMode: true);

      var result = await fixture.Client.CallToolAsync(
         "search_symbols",
         new Dictionary<string, object?>
         {
            ["query"] = "*",
            ["offset"] = invalidValue
         });

      // The MCP SDK may throw or return an error for type mismatches
      Assert.True(result.IsError, "Expected error for non-integer value");
   }

   [Fact]
   public async Task NonNegative_RejectsNegative()
   {
      await using var fixture = await DotLensMcpFixture.CreateAsync(workerMode: true);

      var result = await fixture.Client.CallToolAsync(
         "search_symbols",
         new Dictionary<string, object?>
         {
            ["query"] = "*",
            ["offset"] = -1
         });

      Assert.True(result.IsError, "Expected validation error for negative value");
      var error = ParseErrorResponse(result);
      Assert.Equal("INVALID_PARAMETER", error.Code);
   }

   [Fact]
   public async Task NonNegative_AcceptsZero()
   {
      await using var fixture = await DotLensMcpFixture.CreateAsync(workerMode: true);

      var result = await fixture.Client.CallToolAsync(
         "search_symbols",
         new Dictionary<string, object?>
         {
            ["query"] = "*",
            ["offset"] = 0
         });

      // The call will fail because no solution is loaded, but it should NOT be a validation error
      if (result.IsError == true)
      {
         var error = TryParseErrorResponse(result);
         Assert.NotEqual("INVALID_PARAMETER", error?.Code ?? "");
      }
   }

   [Theory]
   [InlineData(1)]
   [InlineData(10)]
   [InlineData(100)]
   public async Task NonNegative_AcceptsPositiveValues(int validValue)
   {
      await using var fixture = await DotLensMcpFixture.CreateAsync(workerMode: true);

      var result = await fixture.Client.CallToolAsync(
         "search_symbols",
         new Dictionary<string, object?>
         {
            ["query"] = "*",
            ["offset"] = validValue
         });

      // The call will fail because no solution is loaded, but it should NOT be a validation error
      if (result.IsError == true)
      {
         var error = TryParseErrorResponse(result);
         Assert.NotEqual("INVALID_PARAMETER", error?.Code ?? "");
      }
   }

   #endregion

   /// <summary>
   /// Parses the error response from a CallToolResult.
   /// </summary>
   private static ErrorInfo ParseErrorResponse(CallToolResult result)
   {
      var text = GetTextContent(result);
      var json = JsonDocument.Parse(text);
      var error = json.RootElement.GetProperty("error");

      return new ErrorInfo
      {
         Code = error.GetProperty("code").GetString() ?? "",
         Message = error.GetProperty("message").GetString() ?? "",
         Hint = error.TryGetProperty("hint", out var hint) ? hint.GetString() : null
      };
   }

   /// <summary>
   /// Attempts to parse the error response, returning null if parsing fails.
   /// </summary>
   private static ErrorInfo? TryParseErrorResponse(CallToolResult result)
   {
      try
      {
         return ParseErrorResponse(result);
      }
      catch
      {
         return null;
      }
   }

   /// <summary>
   /// Extracts text content from a CallToolResult.
   /// </summary>
   private static string GetTextContent(CallToolResult result)
   {
      if (result.Content.Count == 0)
         return string.Empty;

      var textContent = result.Content[0] as TextContentBlock;
      return textContent?.Text ?? string.Empty;
   }

   private sealed record ErrorInfo
   {
      public required string Code { get; init; }
      public required string Message { get; init; }
      public string? Hint { get; init; }
   }
}
