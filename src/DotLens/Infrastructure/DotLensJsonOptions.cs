using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;

namespace DotLens.Infrastructure;

/// <summary>
/// Provides JSON serializer options for DotLens with camelCase enum serialization.
/// </summary>
public static class DotLensJsonOptions
{
   /// <summary>
   /// JSON serializer options with camelCase enum converter.
   /// Based on MCP SDK defaults but uses camelCase for enum values.
   /// </summary>
   public static JsonSerializerOptions Default { get; } = CreateOptions();

   private static JsonSerializerOptions CreateOptions()
   {
      // Start with the MCP SDK's default options (includes Web defaults, null handling, etc.)
      // We need to create a new instance to modify converters
      var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions);

      // Remove the default JsonStringEnumConverter (if present) and add camelCase version
      for (int i = options.Converters.Count - 1; i >= 0; i--)
      {
         if (options.Converters[i] is JsonStringEnumConverter)
         {
            options.Converters.RemoveAt(i);
         }
      }

      // Add camelCase enum converter
      options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

      // Prevent accidental mutation of the shared instance
      options.MakeReadOnly();

      return options;
   }
}
