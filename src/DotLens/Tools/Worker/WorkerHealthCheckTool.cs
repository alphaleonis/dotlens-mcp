using System.ComponentModel;
using DotLens.Infrastructure;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Worker;

[McpServerToolType]
[DotLensWorkerTool(Internal = true)]
public class WorkerHealthCheckTool
{
   private readonly IWorkspace _workspaceManager;

   public WorkerHealthCheckTool(IWorkspace workspaceManager)
   {
      _workspaceManager = workspaceManager;
   }

   [McpServerTool(Name = "worker_health_check")]
   [Description("Internal: Get workspace health status")]
   public async Task<ToolResult<IWorkerHealthCheckResult>> HealthCheckAsync()
   {
      return await _workspaceManager.GetHealthCheckAsync();
   }
}
