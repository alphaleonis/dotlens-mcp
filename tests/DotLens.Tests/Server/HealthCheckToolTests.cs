using DotLens.Infrastructure;
using DotLens.Tools.Server;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.v3;

namespace DotLens.Tests.Server;

public class HealthCheckToolTests
{
   protected CancellationToken CT => TestContext.Current.CancellationToken;

   private static readonly string SolutionPath = Path.Combine("C:", "Test", "Solution.sln");
   private static readonly string Solution1Path = Path.Combine("C:", "Test", "Solution1.sln");
   private static readonly string Solution2Path = Path.Combine("C:", "Test", "Solution2.sln");

   [Fact]
   public async Task HealthCheck_NoSolutionLoaded_ReturnsNotReady()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      A.CallTo(() => workerManager.Workers)
         .Returns(new Dictionary<string, WorkerInstance>());
      A.CallTo(() => workerManager.LoadingPaths)
         .Returns(Array.Empty<string>());

      var tool = new HealthCheckTool(workerManager, NullLogger<HealthCheckTool>.Instance);

      var result = await tool.HealthCheckAsync(CT);

      Assert.NotNull(result.Data);
      Assert.Equal("Not Ready", result.Data.Status);
      Assert.Equal(0, result.Data.TotalSolutions);
      Assert.Null(result.Data.ActiveSolution);
      Assert.Equal("No solution loaded. Call load_solution first or set DOTLENS_SolutionPath environment variable.", result.Data.Message);
   }

   [Fact]
   public async Task HealthCheck_SolutionLoading_ReturnsLoading()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      A.CallTo(() => workerManager.Workers)
         .Returns(new Dictionary<string, WorkerInstance>());
      A.CallTo(() => workerManager.LoadingPaths)
         .Returns(new[] { SolutionPath });

      var tool = new HealthCheckTool(workerManager, NullLogger<HealthCheckTool>.Instance);

      var result = await tool.HealthCheckAsync(CT);

      Assert.NotNull(result.Data);
      Assert.Equal("Loading", result.Data.Status);
      Assert.Equal(0, result.Data.TotalSolutions);
      Assert.Null(result.Data.ActiveSolution);
      var loadingPath = Assert.Single(result.Data.LoadingSolutions!);
      Assert.Equal(SolutionPath, loadingPath);
      Assert.Equal("Loading 1 solution(s) in the background: Solution.sln", result.Data.Message);
      Assert.Equal("Solution is loading. Try again in a few seconds.", result.Data.Hint);
   }

   [Fact]
   public async Task HealthCheck_FailedLoad_ReturnsNotReadyWithFailedSolutions()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      A.CallTo(() => workerManager.Workers)
         .Returns(new Dictionary<string, WorkerInstance>());
      A.CallTo(() => workerManager.LoadingPaths)
         .Returns(Array.Empty<string>());
      A.CallTo(() => workerManager.FailedLoads)
         .Returns(new Dictionary<string, string>
         {
            [SolutionPath] = "MSBuild workspace failed to open"
         });

      var tool = new HealthCheckTool(workerManager, NullLogger<HealthCheckTool>.Instance);

      var result = await tool.HealthCheckAsync(CT);

      Assert.NotNull(result.Data);
      Assert.Equal("Not Ready", result.Data.Status);
      Assert.Equal(0, result.Data.TotalSolutions);
      Assert.Null(result.Data.ActiveSolution);
      Assert.Equal("Solution failed to load: Solution.sln: MSBuild workspace failed to open", result.Data.Message);
      Assert.Equal("Check the solution path and try load_solution again.", result.Data.Hint);
      var failedPath = Assert.Single(result.Data.FailedSolutions!);
      Assert.Equal(SolutionPath, failedPath);
   }

   [Fact]
   public async Task HealthCheck_WithLoadedSolution_ReturnsReady()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      var loadedAt = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
      var workers = new Dictionary<string, WorkerInstance>
      {
         [SolutionPath] = new WorkerInstance
         {
            SolutionPath = SolutionPath,
            Client = null!,
            LoadedAt = loadedAt
         }
      };
      A.CallTo(() => workerManager.Workers).Returns(workers);
      A.CallTo(() => workerManager.ActiveSolutionPath).Returns(SolutionPath);
      A.CallTo(() => workerManager.LoadingPaths).Returns(Array.Empty<string>());
      A.CallTo(() => workerManager.CallToolAsync<object>("worker_health_check", A<Dictionary<string, object?>>._, A<CancellationToken>._))
         .Returns(new { status = "Ready", projectCount = 5 });

      var tool = new HealthCheckTool(workerManager, NullLogger<HealthCheckTool>.Instance);

      var result = await tool.HealthCheckAsync(CT);

      Assert.NotNull(result.Data);
      Assert.Equal("Ready", result.Data.Status);
      Assert.Equal(1, result.Data.TotalSolutions);
      Assert.Equal(SolutionPath, result.Data.ActiveSolution);
      Assert.NotNull(result.Data.ActiveWorkerHealth);
      Assert.Null(result.Data.Hint);
      Assert.Null(result.Data.LoadingSolutions);

      // Verify solution summary
      Assert.Single(result.Data.Solutions);
      var solution = result.Data.Solutions[0];
      Assert.Equal(SolutionPath, solution.Path);
      Assert.True(solution.IsActive);
      Assert.Equal(loadedAt, solution.LoadedAt);
   }

   [Fact]
   public async Task HealthCheck_WithMultipleSolutions_ReturnsAllSolutions()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      var loadedAt1 = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
      var loadedAt2 = new DateTime(2026, 1, 15, 13, 0, 0, DateTimeKind.Utc);
      var workers = new Dictionary<string, WorkerInstance>
      {
         [Solution1Path] = new WorkerInstance
         {
            SolutionPath = Solution1Path,
            Client = null!,
            LoadedAt = loadedAt1
         },
         [Solution2Path] = new WorkerInstance
         {
            SolutionPath = Solution2Path,
            Client = null!,
            LoadedAt = loadedAt2
         }
      };
      A.CallTo(() => workerManager.Workers).Returns(workers);
      A.CallTo(() => workerManager.ActiveSolutionPath).Returns(Solution1Path);
      A.CallTo(() => workerManager.LoadingPaths).Returns(Array.Empty<string>());
      A.CallTo(() => workerManager.CallToolAsync<object>("worker_health_check", A<Dictionary<string, object?>>._, A<CancellationToken>._))
         .Returns(new { status = "Ready" });

      var tool = new HealthCheckTool(workerManager, NullLogger<HealthCheckTool>.Instance);

      var result = await tool.HealthCheckAsync(CT);

      Assert.NotNull(result.Data);
      Assert.Equal("Ready", result.Data.Status);
      Assert.Equal(2, result.Data.TotalSolutions);
      Assert.Equal(Solution1Path, result.Data.ActiveSolution);

      // Verify both solutions are listed
      Assert.Equal(2, result.Data.Solutions.Count);
      var activeSolution = result.Data.Solutions.Single(s => s.IsActive);
      Assert.Equal(Solution1Path, activeSolution.Path);
      var inactiveSolution = result.Data.Solutions.Single(s => !s.IsActive);
      Assert.Equal(Solution2Path, inactiveSolution.Path);
   }

   [Fact]
   public async Task HealthCheck_WorkerCommunicationFails_ReturnsError()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      var workers = new Dictionary<string, WorkerInstance>
      {
         [SolutionPath] = new WorkerInstance
         {
            SolutionPath = SolutionPath,
            Client = null!,
            LoadedAt = DateTime.UtcNow
         }
      };
      A.CallTo(() => workerManager.Workers).Returns(workers);
      A.CallTo(() => workerManager.ActiveSolutionPath).Returns(SolutionPath);
      A.CallTo(() => workerManager.LoadingPaths).Returns(Array.Empty<string>());
      A.CallTo(() => workerManager.CallToolAsync<object>("worker_health_check", A<Dictionary<string, object?>>._, A<CancellationToken>._))
         .ThrowsAsync(new Exception("Worker crashed"));

      var tool = new HealthCheckTool(workerManager, NullLogger<HealthCheckTool>.Instance);

      var result = await tool.HealthCheckAsync(CT);

      Assert.NotNull(result.Data);
      Assert.Equal("Error", result.Data.Status);
      Assert.Equal(1, result.Data.TotalSolutions);
      Assert.Equal(SolutionPath, result.Data.ActiveSolution);
      Assert.Equal("Worker communication failed: Worker crashed", result.Data.Message);
      Assert.Equal("Try calling load_solution to restart the worker", result.Data.Hint);
   }
}
