using DotLens.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DotLens.Tests.Integration;

/// <summary>
/// A workspace loaded with a fixture project that has its own solution, which a test class shares across its tests.
/// </summary>
/// <remarks>
/// Such a fixture has its own solution because tests of <c>TestFixtures.sln</c> assert counts across its projects,
/// which another project there would change.
/// </remarks>
public abstract class StandaloneFixtureWorkspace : IAsyncLifetime
{
   private readonly string _projectName;

   private WorkspaceManager? _workspaceManager;
   private string? _projectDirectory;

   /// <param name="projectName">The name of the fixture's directory under <c>tests/TestFixtures</c>, of its project and of its solution.</param>
   protected StandaloneFixtureWorkspace(string projectName)
   {
      _projectName = projectName;
   }

   public IReadOnlyWorkspace Workspace => _workspaceManager
      ?? throw new InvalidOperationException("Fixture not initialized");

   /// <summary>
   /// The absolute path of the directory that holds the fixture project and its solution.
   /// </summary>
   public string ProjectDirectory => _projectDirectory
      ?? throw new InvalidOperationException("Fixture not initialized");

   /// <summary>
   /// Returns the absolute path of a file in the fixture project.
   /// </summary>
   /// <param name="relativePath">The path relative to the project directory, with '/' separators.</param>
   public string GetPath(string relativePath) =>
      Path.Combine(ProjectDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

   public async ValueTask InitializeAsync()
   {
      var solutionPath = FindSolutionPath();
      _projectDirectory = Path.GetDirectoryName(solutionPath);

      _workspaceManager = new WorkspaceManager(
         NullLogger<WorkspaceManager>.Instance, Options.Create(new DotLensOptions()));

      await TestFixtureRestore.EnsureRestoredAsync(solutionPath);
      await _workspaceManager.LoadSolutionAsync(solutionPath);
   }

   public ValueTask DisposeAsync()
   {
      _workspaceManager?.Dispose();
      return ValueTask.CompletedTask;
   }

   private string FindSolutionPath()
   {
      for (var dir = Directory.GetCurrentDirectory(); dir is not null; dir = Directory.GetParent(dir)?.FullName)
      {
         var candidate = Path.Combine(dir, "tests", "TestFixtures", _projectName, $"{_projectName}.slnx");
         if (File.Exists(candidate))
            return candidate;
      }

      throw new InvalidOperationException($"Could not find {_projectName}.slnx");
   }
}
