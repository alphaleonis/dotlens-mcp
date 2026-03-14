using DotLens.Infrastructure;
using DotLens.Tools.Server;
using FakeItEasy;
using Xunit;

namespace DotLens.Tests.Server;

public class SetActiveSolutionToolTests
{
   [Fact]
   public void SetActiveSolution_NotLoaded_ThrowsToolException()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      A.CallTo(() => workerManager.SetActiveSolution(A<string>._))
         .Throws(new InvalidOperationException("Solution not loaded: C:\\Test\\NotLoaded.sln"));

      var tool = new SetActiveSolutionTool(workerManager);

      var ex = Assert.Throws<ToolException>(
         () => tool.SetActiveSolution(@"C:\Test\NotLoaded.sln"));

      Assert.Equal("NO_SOLUTION_LOADED", ex.Code);
   }

   [Fact]
   public void SetActiveSolution_InvalidPath_ThrowsToolException()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      A.CallTo(() => workerManager.SetActiveSolution(A<string>._))
         .Throws(new InvalidOperationException("Invalid solution path: not_a_solution.txt"));

      var tool = new SetActiveSolutionTool(workerManager);

      var ex = Assert.Throws<ToolException>(
         () => tool.SetActiveSolution("not_a_solution.txt"));

      Assert.Equal("NO_SOLUTION_LOADED", ex.Code);
   }

   [Fact]
   public void SetActiveSolution_ValidPath_SwitchesSuccessfully()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      A.CallTo(() => workerManager.SetActiveSolution(@"C:\Test\Loaded.sln")).DoesNothing();
      A.CallTo(() => workerManager.WorkerCount).Returns(2);

      var tool = new SetActiveSolutionTool(workerManager);

      var result = tool.SetActiveSolution(@"C:\Test\Loaded.sln");

      var json = System.Text.Json.JsonSerializer.Serialize(result, result.GetType(), DotLens.Tests.TestJsonOptions.CamelCase);
      Assert.Contains("Active solution switched", json);
      Assert.Contains("totalSolutions", json);
      A.CallTo(() => workerManager.SetActiveSolution(@"C:\Test\Loaded.sln"))
         .MustHaveHappenedOnceExactly();
   }
}
