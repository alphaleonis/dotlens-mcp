using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotLens.Tests;

/// <summary>
/// JSON serialization options for test assertions.
/// Uses camelCase to match expected property names in tool responses.
/// Includes JsonStringEnumConverter with camelCase naming policy.
/// </summary>
internal static class TestJsonOptions
{
   public static readonly JsonSerializerOptions CamelCase = new()
   {
      PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
   };
}
