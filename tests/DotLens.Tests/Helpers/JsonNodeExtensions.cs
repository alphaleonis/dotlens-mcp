using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace DotLens.Tests.Helpers;

/// <summary>
/// Extension methods for cleaner JsonNode access in test assertions.
/// </summary>
public static class JsonNodeExtensions
{
   // === Required value accessors (throw if missing or null) ===

   /// <summary>
   /// Gets a required string property value.
   /// Accepts nullable node but asserts it is not null.
   /// </summary>
   /// <exception cref="ArgumentNullException">Thrown when node is null.</exception>
   /// <exception cref="InvalidOperationException">Thrown when property is missing or null.</exception>
   public static string GetString(
      this JsonNode? node,
      string property,
      [CallerArgumentExpression(nameof(node))] string? nodeExpression = null)
   {
      ArgumentNullException.ThrowIfNull(node, nodeExpression);
      var value = node[property];
      if (value is null)
         throw new InvalidOperationException($"Property '{property}' is missing or null");
      return value.GetValue<string>();
   }

   /// <summary>
   /// Gets a required int property value.
   /// Accepts nullable node but asserts it is not null.
   /// </summary>
   /// <exception cref="ArgumentNullException">Thrown when node is null.</exception>
   /// <exception cref="InvalidOperationException">Thrown when property is missing or null.</exception>
   public static int GetInt(
      this JsonNode? node,
      string property,
      [CallerArgumentExpression(nameof(node))] string? nodeExpression = null)
   {
      ArgumentNullException.ThrowIfNull(node, nodeExpression);
      var value = node[property];
      if (value is null)
         throw new InvalidOperationException($"Property '{property}' is missing or null");
      return value.GetValue<int>();
   }

   /// <summary>
   /// Gets a required bool property value.
   /// Accepts nullable node but asserts it is not null.
   /// </summary>
   /// <exception cref="ArgumentNullException">Thrown when node is null.</exception>
   /// <exception cref="InvalidOperationException">Thrown when property is missing or null.</exception>
   public static bool GetBool(
      this JsonNode? node,
      string property,
      [CallerArgumentExpression(nameof(node))] string? nodeExpression = null)
   {
      ArgumentNullException.ThrowIfNull(node, nodeExpression);
      var value = node[property];
      if (value is null)
         throw new InvalidOperationException($"Property '{property}' is missing or null");
      return value.GetValue<bool>();
   }

   /// <summary>
   /// Gets a required JsonArray property value.
   /// Accepts nullable node but asserts it is not null.
   /// </summary>
   /// <exception cref="ArgumentNullException">Thrown when node is null.</exception>
   /// <exception cref="InvalidOperationException">Thrown when property is missing, null, or not an array.</exception>
   public static JsonArray GetArray(
      this JsonNode? node,
      string property,
      [CallerArgumentExpression(nameof(node))] string? nodeExpression = null)
   {
      ArgumentNullException.ThrowIfNull(node, nodeExpression);
      var value = node[property];
      if (value is null)
         throw new InvalidOperationException($"Property '{property}' is missing or null");
      return value.AsArray();
   }

   /// <summary>
   /// Gets a required JsonObject property value.
   /// Accepts nullable node but asserts it is not null.
   /// </summary>
   /// <exception cref="ArgumentNullException">Thrown when node is null.</exception>
   /// <exception cref="InvalidOperationException">Thrown when property is missing, null, or not an object.</exception>
   public static JsonObject GetObject(
      this JsonNode? node,
      string property,
      [CallerArgumentExpression(nameof(node))] string? nodeExpression = null)
   {
      ArgumentNullException.ThrowIfNull(node, nodeExpression);
      var value = node[property];
      if (value is null)
         throw new InvalidOperationException($"Property '{property}' is missing or null");
      return value.AsObject();
   }

   // === Optional value accessors (return null/default if missing) ===

   /// <summary>
   /// Gets an optional string property value, returning null if node or property is missing.
   /// </summary>
   public static string? GetStringOrNull(this JsonNode? node, string property)
   {
      return node?[property]?.GetValue<string>();
   }

   /// <summary>
   /// Gets an optional int property value, returning null if node or property is missing.
   /// </summary>
   public static int? GetIntOrNull(this JsonNode? node, string property)
   {
      return node?[property]?.GetValue<int>();
   }

   /// <summary>
   /// Gets an optional bool property value, returning null if node or property is missing.
   /// </summary>
   public static bool? GetBoolOrNull(this JsonNode? node, string property)
   {
      return node?[property]?.GetValue<bool>();
   }

   /// <summary>
   /// Gets an optional JsonArray property value, returning null if node or property is missing.
   /// </summary>
   public static JsonArray? GetArrayOrNull(this JsonNode? node, string property)
   {
      return node?[property]?.AsArray();
   }

   /// <summary>
   /// Gets an optional JsonObject property value, returning null if node or property is missing.
   /// </summary>
   public static JsonObject? GetObjectOrNull(this JsonNode? node, string property)
   {
      return node?[property]?.AsObject();
   }

   // === Direct value accessors (for array elements that are simple values) ===

   /// <summary>
   /// Gets the string value from a node, returning null if the node is null.
   /// Use for array elements that are simple string values.
   /// </summary>
   public static string? GetStringValue(this JsonNode? node)
   {
      return node?.GetValue<string>();
   }

   /// <summary>
   /// Gets the int value from a node, returning null if the node is null.
   /// Use for array elements that are simple int values.
   /// </summary>
   public static int? GetIntValue(this JsonNode? node)
   {
      return node?.GetValue<int>();
   }

   /// <summary>
   /// Gets the bool value from a node, returning null if the node is null.
   /// Use for array elements that are simple bool values.
   /// </summary>
   public static bool? GetBoolValue(this JsonNode? node)
   {
      return node?.GetValue<bool>();
   }
}
