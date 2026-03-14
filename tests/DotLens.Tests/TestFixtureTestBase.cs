using System.Text.Json;
using System.Text.Json.Nodes;
using DotLens.Infrastructure;
using DotLens.Infrastructure.Results;
using Xunit;
using Xunit.v3;

namespace DotLens.Tests;

/// <summary>
/// Base class for read-only integration tests that use the dedicated test fixtures.
/// Uses the shared TestFixturesCollectionFixture which loads TestFixtures.sln once
/// for all test classes in the collection, dramatically improving test performance.
///
/// Tests using this base class:
/// - Share a single workspace instance (loaded once per test run)
/// - Can run in parallel safely (only read-only operations allowed)
/// - Cannot modify workspace state (IReadOnlyWorkspace enforces this at compile time)
///
/// For tests that need to modify the workspace (apply refactorings, sync documents, etc.),
/// use <see cref="IsolatedProjectTestBase"/> instead.
///
/// Test classes must:
/// 1. Add [Collection("TestFixtures")] attribute
/// 2. Accept TestFixturesCollectionFixture in constructor and pass to base
/// </summary>
public abstract class TestFixtureTestBase : IAsyncLifetime
{
   private readonly TestFixturesCollectionFixture _fixture;

   protected IReadOnlyWorkspace Workspace => _fixture.Workspace;
   protected string TestFixturesSolutionPath => _fixture.TestFixturesSolutionPath;
   protected string TestFixturesSlnxPath => _fixture.TestFixturesSlnxPath;
   protected string TestFixturesDirectory => _fixture.TestFixturesDirectory;

   /// <summary>
   /// Gets the CancellationToken from xUnit v3 TestContext.
   /// Use this in all async tool calls to support test cancellation.
   /// </summary>
   protected CancellationToken CT => TestContext.Current.CancellationToken;

   protected TestFixtureTestBase(TestFixturesCollectionFixture fixture)
   {
      _fixture = fixture;
   }

   /// <summary>
   /// Override in derived classes to perform per-test-class initialization (e.g., creating tool instances).
   /// The workspace is already loaded via the collection fixture.
   /// </summary>
   public virtual ValueTask InitializeAsync() => ValueTask.CompletedTask;

   public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;

   // ===========================================
   // Path helpers for test fixture projects
   // ===========================================

   /// <summary>
   /// Gets the absolute path to a file within CoreTestProject.
   /// </summary>
   /// <param name="relativePath">Relative path within CoreTestProject (e.g., "Types/Classes.cs")</param>
   protected string GetCoreTestProjectPath(string relativePath)
   {
      return Path.Combine(TestFixturesDirectory, "CoreTestProject", relativePath.Replace('/', Path.DirectorySeparatorChar));
   }

   /// <summary>
   /// Gets the absolute path to a file within InheritanceTestProject.
   /// </summary>
   /// <param name="relativePath">Relative path within InheritanceTestProject (e.g., "InheritanceFixtures.cs")</param>
   protected string GetInheritanceTestProjectPath(string relativePath)
   {
      return Path.Combine(TestFixturesDirectory, "InheritanceTestProject", relativePath.Replace('/', Path.DirectorySeparatorChar));
   }

   /// <summary>
   /// Gets the absolute path to a file within AnalyzerTestProject.
   /// </summary>
   /// <param name="relativePath">Relative path within AnalyzerTestProject (e.g., "AnalyzerWarnings.cs")</param>
   protected string GetAnalyzerTestProjectPath(string relativePath)
   {
      return Path.Combine(TestFixturesDirectory, "AnalyzerTestProject", relativePath.Replace('/', Path.DirectorySeparatorChar));
   }

   /// <summary>
   /// Converts a relative path from search results to an absolute path.
   /// </summary>
   protected string GetAbsolutePath(string relativePath)
   {
      return Path.Combine(TestFixturesDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
   }

   // ===========================================
   // Response assertion helpers
   // ===========================================

   /// <summary>
   /// Checks if a response is successful.
   /// With ToolResult&lt;T&gt;, success is indicated by presence of data property.
   /// Errors throw ToolException, so if we have a result with data, it's successful.
   /// </summary>
   private static bool IsSuccessResponse(JsonNode json)
   {
      // Current format: ToolResult<T> has data property
      if (json["data"] != null || json["Data"] != null)
         return true;

      // Old anonymous type format: success property
      if (json["success"] is JsonNode success)
         return success.GetValue<bool>();

      return false;
   }

   /// <summary>
   /// Gets the error info from a response. Handles both old and new formats.
   /// </summary>
   private static JsonNode? GetErrorNode(JsonNode json)
   {
      // New typed format: error property (camelCase from JSON serialization)
      return json["error"] ?? json["Error"];
   }

   /// <summary>
   /// Gets the data from a response. Handles both old and new formats.
   /// </summary>
   private static JsonNode? GetDataNode(JsonNode json)
   {
      // New typed format: data property (camelCase from JSON serialization)
      return json["data"] ?? json["Data"];
   }

   /// <summary>
   /// Asserts that a response indicates success.
   /// ToolResult is always successful (errors throw ToolException).
   /// </summary>
   protected void AssertSuccess(object response)
   {
      // Handle typed ToolResult directly - if we got a result, it succeeded
      // Errors now throw ToolException instead of returning failure results
      if (response is ToolResult)
      {
         return; // Success - errors are thrown, not returned
      }

      var json = JsonNode.Parse(JsonSerializer.Serialize(response, response.GetType(), TestJsonOptions.CamelCase))!;
      Assert.True(IsSuccessResponse(json),
         $"Expected success but got: {GetErrorNode(json)}");
   }

   /// <summary>
   /// Asserts that a ToolException was thrown with specific error code.
   /// </summary>
   protected void AssertToolException(ToolException ex, string? errorCodeContains = null)
   {
      if (errorCodeContains != null)
      {
         Assert.Contains(errorCodeContains, ex.Code, StringComparison.OrdinalIgnoreCase);
      }
   }

   /// <summary>
   /// Asserts that the action throws a ToolException with specific error code.
   /// This is the preferred way to test error conditions - tools now throw instead of returning failures.
   /// </summary>
   protected async Task<ToolException> AssertThrowsToolExceptionAsync(Func<Task> action, string? expectedErrorCode = null)
   {
      var ex = await Assert.ThrowsAsync<ToolException>(action);
      if (expectedErrorCode != null)
      {
         Assert.Equal(expectedErrorCode, ex.Code);
      }
      return ex;
   }

   /// <summary>
   /// Asserts that a response indicates failure with specific error.
   /// DEPRECATED: Use AssertThrowsToolExceptionAsync for typed tool responses.
   /// For legacy JSON responses only - typed ToolResult failures now throw ToolException.
   /// </summary>
   protected void AssertError(object response, string? errorCodeContains = null)
   {
      var json = JsonNode.Parse(JsonSerializer.Serialize(response, response.GetType(), TestJsonOptions.CamelCase))!;
      Assert.False(IsSuccessResponse(json));

      if (errorCodeContains != null)
      {
         var errorNode = GetErrorNode(json);
         var errorCode = errorNode?["Code"]?.GetValue<string>();
         Assert.NotNull(errorCode);
         Assert.Contains(errorCodeContains, errorCode, StringComparison.OrdinalIgnoreCase);
      }
   }

   /// <summary>
   /// Gets the data portion of a successful response.
   /// </summary>
   protected JsonNode GetData(object response)
   {
      AssertSuccess(response);

      var json = JsonNode.Parse(JsonSerializer.Serialize(response, response.GetType(), TestJsonOptions.CamelCase))!;
      return GetDataNode(json)!;
   }

   /// <summary>
   /// Gets the typed data from a successful ToolResult response.
   /// ToolResult is always successful (errors throw ToolException).
   /// </summary>
   protected T GetTypedData<T>(ToolResult response) where T : class
   {
      var success = Assert.IsType<ToolResult<T>>(response);
      return success.Data;
   }

   /// <summary>
   /// Parses a response to JSON without assertions.
   /// </summary>
   protected JsonNode ToJson(object response)
   {
      return JsonNode.Parse(JsonSerializer.Serialize(response, response.GetType(), TestJsonOptions.CamelCase))!;
   }

   /// <summary>
   /// Gets a property value handling both camelCase (old format) and PascalCase (new format).
   /// </summary>
   protected static T? GetProperty<T>(JsonNode? node, string pascalCaseName) where T : struct
   {
      if (node == null)
         return null;
      var camelCaseName = char.ToLowerInvariant(pascalCaseName[0]) + pascalCaseName[1..];
      var value = node[pascalCaseName] ?? node[camelCaseName];
      return value?.GetValue<T>();
   }

   /// <summary>
   /// Gets a string property handling both camelCase (old format) and PascalCase (new format).
   /// </summary>
   protected static string? GetStringProperty(JsonNode? node, string pascalCaseName)
   {
      if (node == null)
         return null;
      var camelCaseName = char.ToLowerInvariant(pascalCaseName[0]) + pascalCaseName[1..];
      var value = node[pascalCaseName] ?? node[camelCaseName];
      return value?.GetValue<string>();
   }

   /// <summary>
   /// Gets a child node handling both camelCase (old format) and PascalCase (new format).
   /// </summary>
   protected static JsonNode? GetNode(JsonNode? node, string pascalCaseName)
   {
      if (node == null)
         return null;
      var camelCaseName = char.ToLowerInvariant(pascalCaseName[0]) + pascalCaseName[1..];
      return node[pascalCaseName] ?? node[camelCaseName];
   }
}
