using DotLens.Tools.Worker;
using Xunit;

namespace DotLens.Tests.Worker;

public class WorkerHealthCheckToolTests : IsolatedProjectTestBase
{

   private WorkerHealthCheckTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      _tool = new WorkerHealthCheckTool(WorkspaceManager);
   }

   [Fact]
   public async Task HealthCheck_WithLoadedSolution_ReturnsReadyWithDetails()
   {
      var result = await _tool.HealthCheckAsync();

      Assert.NotNull(result.Data);
      var readyResult = Assert.IsType<WorkerHealthCheckReadyResult>(result.Data);

      // Verify status
      Assert.Equal("Ready", readyResult.Status);
      Assert.NotNull(readyResult.Message);

      // Verify solution info
      Assert.NotNull(readyResult.Solution);
      Assert.True(readyResult.Solution.Loaded);
      Assert.True(readyResult.Solution.Projects > 0);
      Assert.True(readyResult.Solution.Documents > 0);

      // Verify workspace info
      Assert.NotNull(readyResult.Workspace);
      Assert.True(readyResult.Workspace.Indexed);
      Assert.True(readyResult.Workspace.CacheSize >= 0);

      // Verify capabilities
      Assert.NotNull(readyResult.Capabilities);
      Assert.True(readyResult.Capabilities.FindReferences);
      Assert.True(readyResult.Capabilities.FindImplementations);
      Assert.True(readyResult.Capabilities.Diagnostics);

      // Verify configuration
      Assert.NotNull(readyResult.Configuration);
      Assert.True(readyResult.Configuration.MaxDiagnostics > 0);
   }
}
