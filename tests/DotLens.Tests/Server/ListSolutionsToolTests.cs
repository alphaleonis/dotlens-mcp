using DotLens.Infrastructure;
using DotLens.Tools.Server;
using FakeItEasy;
using Xunit;

namespace DotLens.Tests.Server;

public class ListSolutionsToolTests
{
   [Fact]
   public void ListSolutions_NoSolutionsLoaded_ReturnsEmptyList()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      A.CallTo(() => workerManager.Workers)
         .Returns(new Dictionary<string, WorkerInstance>());

      var tool = new ListSolutionsTool(workerManager);

      var result = tool.ListSolutions();

      var json = System.Text.Json.JsonSerializer.Serialize(result, result.GetType(), DotLens.Tests.TestJsonOptions.CamelCase);
      Assert.Contains("No solutions loaded", json);
      Assert.Contains("\"totalSolutions\":0", json);
   }

   [Fact]
   public void ListSolutions_WithLoadedSolutions_ReturnsAllSolutions()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      var workers = new Dictionary<string, WorkerInstance>
      {
         [@"C:\Test\Solution1.sln"] = new WorkerInstance
         {
            SolutionPath = @"C:\Test\Solution1.sln",
            Client = null!,
            LoadedAt = DateTime.UtcNow
         },
         [@"C:\Test\Solution2.sln"] = new WorkerInstance
         {
            SolutionPath = @"C:\Test\Solution2.sln",
            Client = null!,
            LoadedAt = DateTime.UtcNow
         }
      };
      A.CallTo(() => workerManager.Workers).Returns(workers);
      A.CallTo(() => workerManager.ActiveSolutionPath).Returns(@"C:\Test\Solution1.sln");

      var tool = new ListSolutionsTool(workerManager);

      var result = tool.ListSolutions();

      var json = System.Text.Json.JsonSerializer.Serialize(result, result.GetType(), DotLens.Tests.TestJsonOptions.CamelCase);
      Assert.Contains("\"totalSolutions\":2", json);
      Assert.Contains("Solution1.sln", json);
      Assert.Contains("Solution2.sln", json);
   }

   [Fact]
   public void ListSolutions_MarksActiveSolutionCorrectly()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      var workers = new Dictionary<string, WorkerInstance>
      {
         [@"C:\Test\Active.sln"] = new WorkerInstance
         {
            SolutionPath = @"C:\Test\Active.sln",
            Client = null!,
            LoadedAt = DateTime.UtcNow
         }
      };
      A.CallTo(() => workerManager.Workers).Returns(workers);
      A.CallTo(() => workerManager.ActiveSolutionPath).Returns(@"C:\Test\Active.sln");

      var tool = new ListSolutionsTool(workerManager);

      var result = tool.ListSolutions();

      var json = System.Text.Json.JsonSerializer.Serialize(result, result.GetType(), DotLens.Tests.TestJsonOptions.CamelCase);
      Assert.Contains("\"isActive\":true", json);
      Assert.Contains(@"C:\\Test\\Active.sln", json);
   }
}
