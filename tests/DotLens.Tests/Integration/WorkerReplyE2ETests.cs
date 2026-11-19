using System.Text.Json.Nodes;
using DotLens.Infrastructure;
using ModelContextProtocol.Protocol;
using Xunit;

namespace DotLens.Tests.Integration;

/// <summary>
/// Covers what the server reports from the replies of a worker process: load_solution's counts and warnings, and the
/// worker's errors.
/// </summary>
/// <remarks>
/// The solution is an unrestored copy of AnalyzerTestProject, so its load has a warning, as
/// <see cref="DotLens.Tests.Infrastructure.UnresolvedFrameworkReferencesTests"/> describes.
/// </remarks>
public sealed class WorkerReplyE2ETests : IAsyncLifetime
{
   private const string ExpectedWarning =
      "Project AnalyzerTestProject resolved no framework references (System.Object is missing), so its diagnostics "
      + "are incomplete. Run dotnet restore and call reload_solution.";

   private string? _tempDir;

   private static CancellationToken CT => TestContext.Current.CancellationToken;

   private string TempDir => _tempDir ?? throw new InvalidOperationException("Test not initialized");

   private string SolutionPath => Path.Combine(TempDir, "Analyzer.slnx");

   public async ValueTask InitializeAsync()
   {
      _tempDir = Path.Combine(Path.GetTempPath(), $"DotLensLoadSolutionE2ETest_{Guid.NewGuid():N}");
      var repoRoot = FindRepoRoot();
      var projectDir = Path.Combine(_tempDir, "AnalyzerTestProject");
      Directory.CreateDirectory(projectDir);
      foreach (var file in Directory.GetFiles(Path.Combine(repoRoot, "tests", "TestFixtures", "AnalyzerTestProject")))
         File.Copy(file, Path.Combine(projectDir, Path.GetFileName(file)));

      // Outside the repo, MSBuild would otherwise load the copy with the newest installed SDK.
      File.Copy(Path.Combine(repoRoot, "global.json"), Path.Combine(_tempDir, "global.json"));
      await File.WriteAllTextAsync(SolutionPath, """
         <Solution>
           <Project Path="AnalyzerTestProject/AnalyzerTestProject.csproj" />
         </Solution>
         """, CT);
   }

   public ValueTask DisposeAsync()
   {
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

   /// <summary>
   /// The second call finds the worker loaded, so it reports what the server kept from the first reply.
   /// </summary>
   [Fact]
   public async Task WorkspaceManager_FailureConsistency_LoadSolution_ReportsTheWorkerCountsAndWarnings()
   {
      await using var fixture = await DotLensMcpFixture.CreateAsync(workerMode: false, CT);

      foreach (var expectedStatus in new[] { "loaded", "already_loaded" })
      {
         var result = await fixture.Client.CallToolAsync(
            "load_solution",
            new Dictionary<string, object?> { ["solutionPath"] = SolutionPath, ["timeoutSeconds"] = 300 },
            cancellationToken: CT);

         var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
         Assert.False(result.IsError ?? false, text);
         var data = JsonNode.Parse(text).GetObject("data");
         Assert.Equal(expectedStatus, data.GetString("status"));
         Assert.Equal(1, data.GetInt("projectCount"));
         Assert.Equal(5, data.GetInt("documentCount"));
         Assert.Equal([ExpectedWarning], data.GetArray("warnings").Select(w => w?.GetValue<string>()));
      }
   }

   [Fact]
   public async Task WorkspaceManager_FailureConsistency_LoadSolution_FailedLoad_ReportsTheWorkerError()
   {
      var brokenPath = Path.Combine(TempDir, "Broken.slnx");
      await File.WriteAllTextAsync(brokenPath, "<Solution><Project Path=", CT);
      await using var fixture = await DotLensMcpFixture.CreateAsync(workerMode: false, CT);

      var result = await fixture.Client.CallToolAsync(
         "load_solution",
         new Dictionary<string, object?> { ["solutionPath"] = brokenPath, ["timeoutSeconds"] = 300 },
         cancellationToken: CT);

      var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
      Assert.True(result.IsError ?? false, text);
      var error = JsonNode.Parse(text).GetObject("error");
      Assert.Equal(ErrorCodes.AnalysisFailed, error.GetString("code"));
      Assert.Equal(
         $"Failed to load solution {brokenPath}: Unexpected end of file has occurred. Line 1, position 25.", error.GetString("message"));
      Assert.Equal("Fix the cause, then load the solution again.", error.GetString("hint"));
   }

   [Fact]
   public async Task WorkspaceManager_FailureConsistency_ForwardedTool_ReportsTheWorkerError()
   {
      var missingPath = Path.Combine(TempDir, "AnalyzerTestProject", "Missing.cs");
      await using var fixture = await DotLensMcpFixture.CreateAsync(workerMode: false, CT);
      var loaded = await fixture.Client.CallToolAsync(
         "load_solution",
         new Dictionary<string, object?> { ["solutionPath"] = SolutionPath, ["timeoutSeconds"] = 300 },
         cancellationToken: CT);
      Assert.False(loaded.IsError ?? false);

      var result = await fixture.Client.CallToolAsync(
         "get_file_outline", new Dictionary<string, object?> { ["filePath"] = missingPath }, cancellationToken: CT);

      var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
      Assert.True(result.IsError ?? false, text);
      var error = JsonNode.Parse(text).GetObject("error");
      Assert.Equal(ErrorCodes.FileNotInSolution, error.GetString("code"));
      Assert.Equal($"File not found in solution: {missingPath}", error.GetString("message"));
      Assert.Equal("Check the file path or reload the solution", error.GetString("hint"));
   }

   /// <summary>
   /// The reply is the real one from a worker process, as the server's own calls to it, such as worker_health_check,
   /// receive it.
   /// </summary>
   [Fact]
   public async Task WorkspaceManager_FailureConsistency_ServerCallToWorker_KeepsTheWorkerError()
   {
      var missingPath = Path.Combine(TempDir, "Missing.slnx");
      await using var fixture = await DotLensMcpFixture.CreateAsync(workerMode: true, CT);
      var result = await fixture.Client.CallToolAsync(
         "worker_load_solution", new Dictionary<string, object?> { ["solutionPath"] = missingPath }, cancellationToken: CT);

      var ex = MultiWorkerManager.WorkerToolError("worker_load_solution", result);

      Assert.Equal(ErrorCodes.FileNotFound, ex.Code);
      Assert.Equal($"Solution file not found: {missingPath}", ex.Message);
      Assert.Equal("Verify the path is correct and the file exists", ex.Hint);
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
