using DotLens.Infrastructure;
using DotLens.Tools.Editing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DotLens.Tests.Infrastructure;

/// <summary>
/// Covers how a failed load leaves the solution that DotLens holds and the MSBuildWorkspace together.
/// </summary>
public sealed class WorkspaceManagerFailureConsistencyTests : IAsyncLifetime
{
   private const string CalcText = "namespace App;\n\npublic static class Calc\n{\n    public static int Value() => 2;\n}\n";

   private const string BrokenSolutionError = "Unexpected end of file has occurred. Line 1, position 25.";

   private const string UnrestoredAppWarning =
      "Project App resolved no framework references (System.Object is missing), so its diagnostics are incomplete. "
      + "Run dotnet restore and call reload_solution.";

   private string? _tempDir;
   private WorkspaceManager? _workspaceManager;

   private static CancellationToken CT => TestContext.Current.CancellationToken;

   private string TempDir => _tempDir ?? throw new InvalidOperationException("Test not initialized");

   private WorkspaceManager Workspace => _workspaceManager ?? throw new InvalidOperationException("Test not initialized");

   private string SolutionPath => Path.Combine(TempDir, "App.slnx");

   private string BrokenSolutionPath => Path.Combine(TempDir, "Broken.slnx");

   private string CalcPath => Path.Combine(TempDir, "App", "Calc.cs");

   public async ValueTask InitializeAsync()
   {
      _tempDir = Path.Combine(Path.GetTempPath(), $"DotLensFailureConsistencyTest_{Guid.NewGuid():N}");
      Directory.CreateDirectory(Path.Combine(_tempDir, "App"));

      // Outside the repo, MSBuild would otherwise load the solution with the newest installed SDK.
      File.Copy(Path.Combine(FindRepoRoot(), "global.json"), Path.Combine(_tempDir, "global.json"));
      await TestFixtureRestore.RequireRestoreForTargetingPacksAsync(_tempDir, CT);
      await File.WriteAllTextAsync(Path.Combine(_tempDir, "App", "App.csproj"), """
         <Project Sdk="Microsoft.NET.Sdk">
           <PropertyGroup>
             <TargetFramework>net10.0</TargetFramework>
           </PropertyGroup>
         </Project>
         """, CT);
      await File.WriteAllTextAsync(CalcPath, CalcText, CT);
      await File.WriteAllTextAsync(SolutionPath, """
         <Solution>
           <Project Path="App/App.csproj" />
         </Solution>
         """, CT);
      await File.WriteAllTextAsync(BrokenSolutionPath, "<Solution><Project Path=", CT);

      _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance, Options.Create(new DotLensOptions()));
   }

   public ValueTask DisposeAsync()
   {
      _workspaceManager?.Dispose();
      if (_tempDir is not null && Directory.Exists(_tempDir))
      {
         try
         {
            Directory.Delete(_tempDir, recursive: true);
         }
         catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
         {
            // A leftover temp directory does not affect other tests.
         }
      }

      return ValueTask.CompletedTask;
   }

   [Fact]
   public async Task WorkspaceManager_FailureConsistency_LoadFails_KeepsThePreviousSolutionAndWorkspace()
   {
      await Workspace.LoadSolutionAsync(SolutionPath, CT);
      var solution = Workspace.EnsureSolutionLoaded();
      var loadWarnings = Workspace.LoadWarnings;
      Assert.Equal([new LoadWarning(UnrestoredAppWarning, Assert.Single(solution.Projects).Id)], loadWarnings);

      var ex = await Assert.ThrowsAsync<ToolException>(() => Workspace.LoadSolutionAsync(BrokenSolutionPath, CT));

      Assert.Equal(ErrorCodes.AnalysisFailed, ex.Code);
      Assert.Equal($"Failed to load solution {BrokenSolutionPath}: {BrokenSolutionError}", ex.Message);
      Assert.Equal($"Fix the cause, then load the solution again. {SolutionPath} stays loaded.", ex.Hint);
      Assert.Same(solution, Workspace.EnsureSolutionLoaded());
      Assert.Same(solution, Workspace.WorkspaceSolution);
      Assert.Equal(loadWarnings, Workspace.LoadWarnings);

      var edited = await new EditFileTool(Workspace).EditFileAsync(
         CalcPath, [new EditOperation { Find = "=> 2", Replace = "=> 3" }], CT);

      Assert.True(edited.Data.Applied);
      Assert.Equal(CalcText.Replace("=> 2", "=> 3", StringComparison.Ordinal), await File.ReadAllTextAsync(CalcPath, CT));
   }

   [Fact]
   public async Task WorkspaceManager_FailureConsistency_FirstLoadFails_LeavesNoSolution()
   {
      var ex = await Assert.ThrowsAsync<ToolException>(() => Workspace.LoadSolutionAsync(BrokenSolutionPath, CT));

      Assert.Equal(ErrorCodes.AnalysisFailed, ex.Code);
      Assert.Equal($"Failed to load solution {BrokenSolutionPath}: {BrokenSolutionError}", ex.Message);
      Assert.Equal("Fix the cause, then load the solution again.", ex.Hint);
      Assert.False(Workspace.IsSolutionLoaded);
      Assert.Null(Workspace.WorkspaceSolution);
      Assert.Empty(Workspace.LoadWarnings);
   }

   private static string FindRepoRoot()
   {
      for (var dir = Directory.GetCurrentDirectory(); dir is not null; dir = Directory.GetParent(dir)?.FullName)
      {
         if (File.Exists(Path.Combine(dir, "global.json")) && Directory.Exists(Path.Combine(dir, "tests", "TestFixtures")))
            return dir;
      }

      throw new InvalidOperationException("Could not find the repository root");
   }
}
