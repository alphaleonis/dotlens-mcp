using DotLens.Tools.Worker;
using Xunit;

namespace DotLens.Tests.Worker;

public class WorkerLoadSolutionToolTests : IsolatedProjectTestBase
{

   private WorkerLoadSolutionTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      _tool = new WorkerLoadSolutionTool(WorkspaceManager);
   }

   [Fact]
   public async Task LoadSolution_ValidPath_ReturnsSuccess()
   {
      // Re-load the same solution (already loaded in base)
      var result = await _tool.LoadSolutionAsync(TestFixturesSolutionPath);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.NotNull(data["solutionPath"]);
      Assert.True(data.GetIntOrNull("projectCount") > 0);
   }

   [Fact]
   public async Task LoadSolution_InvalidPath_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.LoadSolutionAsync(@"C:\nonexistent\solution.sln"));
   }

   [Fact]
   public async Task LoadSolution_SlnxFormat_ReturnsSuccess()
   {
      // Test loading the .slnx format (XML-based solution format)
      var result = await _tool.LoadSolutionAsync(TestFixturesSlnxPath);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.NotNull(data["solutionPath"]);
      Assert.True(data.GetIntOrNull("projectCount") > 0);
   }
}
