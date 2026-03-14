using DotLens.Infrastructure;
using DotLens.Tools.Server;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.v3;

namespace DotLens.Tests.Server;

public class UnloadSolutionToolTests
{
   protected CancellationToken CT => TestContext.Current.CancellationToken;

   [Fact]
   public async Task UnloadSolution_NoSolutionsLoaded_ReturnsAlreadyUnloaded()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      A.CallTo(() => workerManager.WorkerCount).Returns(0);

      var tool = new UnloadSolutionTool(workerManager, NullLogger<UnloadSolutionTool>.Instance);

      var result = await tool.UnloadSolutionAsync(cancellationToken: CT);

      var json = System.Text.Json.JsonSerializer.Serialize(result, result.GetType(), DotLens.Tests.TestJsonOptions.CamelCase);
      Assert.Contains("No solutions are loaded", json);
      Assert.Contains("already_unloaded", json);
   }

   [Fact]
   public async Task UnloadSolution_NoActiveSolution_ReturnsAlreadyUnloaded()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      A.CallTo(() => workerManager.WorkerCount).Returns(1);
      A.CallTo(() => workerManager.ActiveSolutionPath).Returns((string?)null);

      var tool = new UnloadSolutionTool(workerManager, NullLogger<UnloadSolutionTool>.Instance);

      var result = await tool.UnloadSolutionAsync(cancellationToken: CT);

      var json = System.Text.Json.JsonSerializer.Serialize(result, result.GetType(), DotLens.Tests.TestJsonOptions.CamelCase);
      Assert.Contains("No active solution to unload", json);
   }

   [Fact]
   public async Task UnloadSolution_WithActiveSolution_UnloadsSuccessfully()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      A.CallTo(() => workerManager.WorkerCount).Returns(1).Once()
         .Then.Returns(0);
      A.CallTo(() => workerManager.ActiveSolutionPath).Returns(@"C:\Test\Solution.sln").Once()
         .Then.Returns((string?)null);
      A.CallTo(() => workerManager.UnloadSolutionAsync(@"C:\Test\Solution.sln", A<CancellationToken>._))
         .Returns(Task.CompletedTask);

      var tool = new UnloadSolutionTool(workerManager, NullLogger<UnloadSolutionTool>.Instance);

      var result = await tool.UnloadSolutionAsync(cancellationToken: CT);

      var json = System.Text.Json.JsonSerializer.Serialize(result, result.GetType(), DotLens.Tests.TestJsonOptions.CamelCase);
      Assert.Contains("Solution unloaded successfully", json);
      Assert.Contains("unloaded", json);
   }

   [Fact]
   public async Task UnloadSolution_SpecificPath_UnloadsCorrectSolution()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      A.CallTo(() => workerManager.WorkerCount).Returns(2).Once()
         .Then.Returns(1);
      A.CallTo(() => workerManager.ActiveSolutionPath).Returns(@"C:\Test\Active.sln");
      A.CallTo(() => workerManager.UnloadSolutionAsync(@"C:\Test\Other.sln", A<CancellationToken>._))
         .Returns(Task.CompletedTask);

      var tool = new UnloadSolutionTool(workerManager, NullLogger<UnloadSolutionTool>.Instance);

      var result = await tool.UnloadSolutionAsync(@"C:\Test\Other.sln", cancellationToken: CT);

      var json = System.Text.Json.JsonSerializer.Serialize(result, result.GetType(), DotLens.Tests.TestJsonOptions.CamelCase);
      Assert.Contains("Solution unloaded successfully", json);
      Assert.Contains("remainingSolutions", json);
   }

   [Fact]
   public async Task UnloadSolution_Error_ThrowsToolException()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      A.CallTo(() => workerManager.WorkerCount).Returns(1);
      A.CallTo(() => workerManager.ActiveSolutionPath).Returns(@"C:\Test\Solution.sln");
      A.CallTo(() => workerManager.UnloadSolutionAsync(A<string>._, A<CancellationToken>._))
         .ThrowsAsync(new Exception("Worker termination failed"));

      var tool = new UnloadSolutionTool(workerManager, NullLogger<UnloadSolutionTool>.Instance);

      var ex = await Assert.ThrowsAsync<ToolException>(
         () => tool.UnloadSolutionAsync(cancellationToken: CT));

      Assert.Equal("ANALYSIS_FAILED", ex.Code);
   }
}
