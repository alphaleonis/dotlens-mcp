using DotLens.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DotLens.Tests;

/// <summary>
/// Shared fixture that loads TestFixtures.sln once for all test classes in the collection.
/// This dramatically improves test performance by avoiding repeated MSBuildWorkspace.OpenSolutionAsync calls.
///
/// Exposes <see cref="IReadOnlyWorkspace"/> to ensure tests are read-only and can run in parallel safely.
/// Tests that need to modify the workspace should use <see cref="IsolatedProjectTestBase"/> instead.
/// </summary>
public sealed class TestFixturesCollectionFixture : IAsyncLifetime
{
   private WorkspaceManager? _workspaceManager;

   /// <summary>
   /// Read-only view of the shared workspace. Tests cannot modify workspace state.
   /// </summary>
   public IReadOnlyWorkspace Workspace => _workspaceManager
      ?? throw new InvalidOperationException("Fixture not initialized");

   /// <summary>
   /// Path to TestFixtures.sln.
   /// </summary>
   public string TestFixturesSolutionPath { get; private set; } = null!;

   /// <summary>
   /// Path to TestFixtures.slnx.
   /// </summary>
   public string TestFixturesSlnxPath { get; private set; } = null!;

   /// <summary>
   /// Directory containing the test fixtures.
   /// </summary>
   public string TestFixturesDirectory => Path.GetDirectoryName(TestFixturesSolutionPath)!;

   public async ValueTask InitializeAsync()
   {
      // Find the TestFixtures solution path (go up from test bin directory)
      var currentDir = Directory.GetCurrentDirectory();
      var solutionDir = currentDir;

      while (solutionDir != null)
      {
         var testFixturesPath = Path.Combine(solutionDir, "tests", "TestFixtures", "TestFixtures.sln");
         var testFixturesSlnxPath = Path.Combine(solutionDir, "tests", "TestFixtures", "TestFixtures.slnx");
         if (File.Exists(testFixturesPath))
         {
            TestFixturesSolutionPath = testFixturesPath;
            TestFixturesSlnxPath = testFixturesSlnxPath;
            break;
         }
         solutionDir = Directory.GetParent(solutionDir)?.FullName;
      }

      if (TestFixturesSolutionPath == null)
         throw new InvalidOperationException("Could not find TestFixtures.sln");

      // Create workspace manager with default options and load solution
      var options = Options.Create(new DotLensOptions());
      _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance, options);

      await TestFixtureRestore.EnsureRestoredAsync(TestFixturesSolutionPath);
      await _workspaceManager.LoadSolutionAsync(TestFixturesSolutionPath);
   }

   public ValueTask DisposeAsync()
   {
      _workspaceManager?.Dispose();
      return ValueTask.CompletedTask;
   }
}

/// <summary>
/// Collection definition for tests that use the shared TestFixtures workspace.
/// Test classes should be decorated with [Collection("TestFixtures")].
/// </summary>
[CollectionDefinition("TestFixtures")]
public class TestFixturesCollection : ICollectionFixture<TestFixturesCollectionFixture>
{
}
