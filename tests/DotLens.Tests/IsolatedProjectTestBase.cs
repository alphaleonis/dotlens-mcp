using System.Text.Json;
using System.Text.Json.Nodes;
using DotLens.Infrastructure;
using DotLens.Infrastructure.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.v3;

namespace DotLens.Tests;

/// <summary>
/// Base class for tests that need to modify files in isolation.
/// Creates a temp copy of the TestFixtures directory, loads it into a workspace,
/// and cleans up after the test.
///
/// Use this when testing tools that write to disk (preview=false scenarios) or
/// tools that require IWorkspace (mutating operations).
/// For read-only tests, use <see cref="TestFixtureTestBase"/> instead.
/// </summary>
public abstract class IsolatedProjectTestBase : IAsyncLifetime
{
   private string? _tempDir;

   /// <summary>
   /// The temp directory containing the copied test fixtures.
   /// </summary>
   protected string TempProjectDir => _tempDir ?? throw new InvalidOperationException("Test not initialized");

   /// <summary>
   /// Alias for TempProjectDir for compatibility with TestFixtureTestBase.
   /// </summary>
   protected string TestFixturesDirectory => TempProjectDir;

   /// <summary>
   /// Path to TestFixtures.sln in the temp copy.
   /// </summary>
   protected string TestFixturesSolutionPath => Path.Combine(TempProjectDir, "TestFixtures.sln");

   /// <summary>
   /// Path to TestFixtures.slnx in the temp copy.
   /// </summary>
   protected string TestFixturesSlnxPath => Path.Combine(TempProjectDir, "TestFixtures.slnx");

   /// <summary>
   /// Workspace manager loaded with the temp copy of TestFixtures.
   /// </summary>
   protected WorkspaceManager WorkspaceManager { get; private set; } = null!;

   /// <summary>
   /// Gets the CancellationToken from xUnit v3 TestContext.
   /// </summary>
   protected CancellationToken CT => TestContext.Current.CancellationToken;

   public virtual async ValueTask InitializeAsync()
   {
      // Find the TestFixtures source directory
      var sourceDir = FindTestFixturesDir();

      // Create temp copy
      _tempDir = Path.Combine(Path.GetTempPath(), $"DotLensIsolatedTest_{Guid.NewGuid():N}");
      CopyDirectory(sourceDir, _tempDir);

      // Initialize workspace manager for temp project
      var options = Options.Create(new DotLensOptions());
      WorkspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance, options);

      // Load the solution from temp copy
      var slnxPath = Path.Combine(_tempDir, "TestFixtures.slnx");
      await WorkspaceManager.LoadSolutionAsync(slnxPath);
   }

   public virtual ValueTask DisposeAsync()
   {
      // Dispose workspace
      (WorkspaceManager as IDisposable)?.Dispose();

      // Clean up temp directory
      if (_tempDir != null && Directory.Exists(_tempDir))
      {
         try
         {
            Directory.Delete(_tempDir, recursive: true);
         }
         catch
         {
            // Ignore cleanup failures - Windows may still have file locks
         }
      }

      return ValueTask.CompletedTask;
   }

   // ===========================================
   // Path helpers for test fixture projects
   // ===========================================

   /// <summary>
   /// Gets the absolute path to a file within CoreTestProject (in temp copy).
   /// </summary>
   protected string GetCoreTestProjectPath(string relativePath)
   {
      return Path.Combine(TempProjectDir, "CoreTestProject", relativePath.Replace('/', Path.DirectorySeparatorChar));
   }

   /// <summary>
   /// Gets the absolute path to a file within InheritanceTestProject (in temp copy).
   /// </summary>
   protected string GetInheritanceTestProjectPath(string relativePath)
   {
      return Path.Combine(TempProjectDir, "InheritanceTestProject", relativePath.Replace('/', Path.DirectorySeparatorChar));
   }

   /// <summary>
   /// Gets the absolute path to a file within AnalyzerTestProject (in temp copy).
   /// </summary>
   protected string GetAnalyzerTestProjectPath(string relativePath)
   {
      return Path.Combine(TempProjectDir, "AnalyzerTestProject", relativePath.Replace('/', Path.DirectorySeparatorChar));
   }

   /// <summary>
   /// Converts a relative path from search results to an absolute path.
   /// </summary>
   protected string GetAbsolutePath(string relativePath)
   {
      return Path.Combine(TempProjectDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
   }

   // ===========================================
   // Response assertion helpers
   // ===========================================

   /// <summary>
   /// Checks if a response is successful.
   /// </summary>
   private static bool IsSuccessResponse(JsonNode json)
   {
      if (json["data"] != null || json["Data"] != null)
         return true;
      if (json["success"] is JsonNode success)
         return success.GetValue<bool>();
      return false;
   }

   private static JsonNode? GetErrorNode(JsonNode json)
   {
      return json["error"] ?? json["Error"];
   }

   private static JsonNode? GetDataNode(JsonNode json)
   {
      return json["data"] ?? json["Data"];
   }

   /// <summary>
   /// Asserts that a response indicates success.
   /// </summary>
   protected void AssertSuccess(object response)
   {
      if (response is ToolResult)
      {
         return;
      }
      var json = JsonNode.Parse(JsonSerializer.Serialize(response, response.GetType(), TestJsonOptions.CamelCase))!;
      Assert.True(IsSuccessResponse(json), $"Expected success but got: {GetErrorNode(json)}");
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

   protected static T? GetProperty<T>(JsonNode? node, string pascalCaseName) where T : struct
   {
      if (node == null)
         return null;
      var camelCaseName = char.ToLowerInvariant(pascalCaseName[0]) + pascalCaseName[1..];
      var value = node[pascalCaseName] ?? node[camelCaseName];
      return value?.GetValue<T>();
   }

   protected static string? GetStringProperty(JsonNode? node, string pascalCaseName)
   {
      if (node == null)
         return null;
      var camelCaseName = char.ToLowerInvariant(pascalCaseName[0]) + pascalCaseName[1..];
      var value = node[pascalCaseName] ?? node[camelCaseName];
      return value?.GetValue<string>();
   }

   protected static JsonNode? GetNode(JsonNode? node, string pascalCaseName)
   {
      if (node == null)
         return null;
      var camelCaseName = char.ToLowerInvariant(pascalCaseName[0]) + pascalCaseName[1..];
      return node[pascalCaseName] ?? node[camelCaseName];
   }

   // ===========================================
   // Private helpers
   // ===========================================

   private static string FindTestFixturesDir()
   {
      var dir = Directory.GetCurrentDirectory();
      while (dir != null)
      {
         var testFixturesPath = Path.Combine(dir, "tests", "TestFixtures");
         if (Directory.Exists(testFixturesPath) && File.Exists(Path.Combine(testFixturesPath, "TestFixtures.slnx")))
            return testFixturesPath;
         dir = Directory.GetParent(dir)?.FullName;
      }
      throw new InvalidOperationException("Could not find tests/TestFixtures directory");
   }

   private static void CopyDirectory(string source, string destination)
   {
      Directory.CreateDirectory(destination);

      foreach (var file in Directory.GetFiles(source))
      {
         var destFile = Path.Combine(destination, Path.GetFileName(file));
         File.Copy(file, destFile);
      }

      foreach (var dir in Directory.GetDirectories(source))
      {
         var dirName = Path.GetFileName(dir);
         if (dirName is "bin" or "obj")
            continue;

         var destDir = Path.Combine(destination, dirName);
         CopyDirectory(dir, destDir);
      }
   }
}
