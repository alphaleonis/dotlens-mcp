using DotLens.Infrastructure;
using DotLens.Tools.Server;
using DotLens.Tools.Worker;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.v3;

namespace DotLens.Tests.Server;

public class ReloadSolutionToolTests
{
   protected CancellationToken CT => TestContext.Current.CancellationToken;

   [Fact]
   public async Task ReloadSolution_NoActiveSolution_ThrowsToolException()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      A.CallTo(() => workerManager.ActiveSolutionPath).Returns((string?)null);

      var tool = new ReloadSolutionTool(workerManager, NullLogger<ReloadSolutionTool>.Instance);

      var ex = await Assert.ThrowsAsync<ToolException>(
         () => tool.ReloadSolutionAsync(cancellationToken: CT));

      Assert.Equal("NO_SOLUTION_LOADED", ex.Code);
   }

   [Fact]
   public async Task ReloadSolution_WithActiveSolution_ReloadsSuccessfully()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      var solutionPath = @"C:\Test\Solution.sln";
      A.CallTo(() => workerManager.ActiveSolutionPath).Returns(solutionPath);
      A.CallTo(() => workerManager.UnloadSolutionAsync(solutionPath, A<CancellationToken>._))
         .Returns(Task.CompletedTask);
      A.CallTo(() => workerManager.LoadSolutionAsync(solutionPath, A<CancellationToken>._))
         .Returns(new LoadSolutionResult(new WorkerInstance
         {
            SolutionPath = solutionPath,
            Client = null!,
            LoadedAt = DateTime.UtcNow,
            ProjectCount = 5,
            DocumentCount = 20
         }, WasAlreadyLoaded: false));
      A.CallTo(() => workerManager.WorkerCount).Returns(1);

      var tool = new ReloadSolutionTool(workerManager, NullLogger<ReloadSolutionTool>.Instance);

      var result = await tool.ReloadSolutionAsync(cancellationToken: CT);

      // Verify unload + load happened
      A.CallTo(() => workerManager.UnloadSolutionAsync(solutionPath, A<CancellationToken>._))
         .MustHaveHappenedOnceExactly();
      A.CallTo(() => workerManager.LoadSolutionAsync(solutionPath, A<CancellationToken>._))
         .MustHaveHappenedOnceExactly();

      // worker_load_solution must NOT be called separately -- LoadSolutionAsync handles it internally
      A.CallTo(() => workerManager.CallToolAsync<WorkerLoadSolutionResult>(
            "worker_load_solution", A<Dictionary<string, object?>>._, A<CancellationToken>._))
         .MustNotHaveHappened();

      // Response should use counts from WorkerInstance
      Assert.NotNull(result.Data);
      Assert.Equal("reloaded", result.Data.Status);
      Assert.Equal(solutionPath, result.Data.SolutionPath);
      Assert.Equal(5, result.Data.ProjectCount);
      Assert.Equal(20, result.Data.DocumentCount);
      Assert.Equal(1, result.Data.TotalSolutions);
   }

   [Fact]
   public async Task ReloadSolution_SpecificPath_ReloadsCorrectSolution()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      var solutionPath = @"C:\Test\Other.sln";
      A.CallTo(() => workerManager.UnloadSolutionAsync(solutionPath, A<CancellationToken>._))
         .Returns(Task.CompletedTask);
      A.CallTo(() => workerManager.LoadSolutionAsync(solutionPath, A<CancellationToken>._))
         .Returns(new LoadSolutionResult(new WorkerInstance
         {
            SolutionPath = solutionPath,
            Client = null!,
            LoadedAt = DateTime.UtcNow,
            ProjectCount = 3,
            DocumentCount = 15
         }, WasAlreadyLoaded: false));
      A.CallTo(() => workerManager.WorkerCount).Returns(1);

      var tool = new ReloadSolutionTool(workerManager, NullLogger<ReloadSolutionTool>.Instance);

      var result = await tool.ReloadSolutionAsync(solutionPath, cancellationToken: CT);

      A.CallTo(() => workerManager.UnloadSolutionAsync(solutionPath, A<CancellationToken>._))
         .MustHaveHappenedOnceExactly();
      A.CallTo(() => workerManager.LoadSolutionAsync(solutionPath, A<CancellationToken>._))
         .MustHaveHappenedOnceExactly();

      // worker_load_solution must NOT be called separately
      A.CallTo(() => workerManager.CallToolAsync<WorkerLoadSolutionResult>(
            "worker_load_solution", A<Dictionary<string, object?>>._, A<CancellationToken>._))
         .MustNotHaveHappened();

      Assert.NotNull(result.Data);
      Assert.Equal("reloaded", result.Data.Status);
      Assert.Equal(solutionPath, result.Data.SolutionPath);
      Assert.Equal(3, result.Data.ProjectCount);
      Assert.Equal(15, result.Data.DocumentCount);
   }

   [Fact]
   public async Task ReloadSolution_LoadFails_ThrowsToolException()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      var solutionPath = @"C:\Test\Solution.sln";
      A.CallTo(() => workerManager.ActiveSolutionPath).Returns(solutionPath);
      A.CallTo(() => workerManager.UnloadSolutionAsync(solutionPath, A<CancellationToken>._))
         .Returns(Task.CompletedTask);
      A.CallTo(() => workerManager.LoadSolutionAsync(solutionPath, A<CancellationToken>._))
         .ThrowsAsync(new Exception("Failed to start worker"));

      var tool = new ReloadSolutionTool(workerManager, NullLogger<ReloadSolutionTool>.Instance);

      var ex = await Assert.ThrowsAsync<ToolException>(
         () => tool.ReloadSolutionAsync(cancellationToken: CT));

      Assert.Equal("ANALYSIS_FAILED", ex.Code);
   }

   [Fact]
   public async Task ReloadSolution_UnloadFails_ThrowsToolException()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      var solutionPath = @"C:\Test\Solution.sln";
      A.CallTo(() => workerManager.ActiveSolutionPath).Returns(solutionPath);
      A.CallTo(() => workerManager.UnloadSolutionAsync(solutionPath, A<CancellationToken>._))
         .ThrowsAsync(new Exception("Failed to terminate worker"));

      var tool = new ReloadSolutionTool(workerManager, NullLogger<ReloadSolutionTool>.Instance);

      var ex = await Assert.ThrowsAsync<ToolException>(
         () => tool.ReloadSolutionAsync(cancellationToken: CT));

      Assert.Equal("ANALYSIS_FAILED", ex.Code);
   }
}
