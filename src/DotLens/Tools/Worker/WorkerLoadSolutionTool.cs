using System.ComponentModel;
using DotLens.Infrastructure;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Worker;

[McpServerToolType]
[DotLensWorkerTool(Internal = true)]
public class WorkerLoadSolutionTool
{
   private readonly IWorkspace _workspaceManager;

   public WorkerLoadSolutionTool(IWorkspace workspaceManager)
   {
      _workspaceManager = workspaceManager;
   }

   [McpServerTool(Name = "worker_load_solution")]
   [Description("Internal: Load solution into MSBuildWorkspace")]
   public async Task<ToolResult<WorkerLoadSolutionResult>> LoadSolutionAsync(
       [Description("Absolute path to .sln or .slnx file")]
       string solutionPath)
   {
      return await _workspaceManager.LoadSolutionAsync(solutionPath);
   }
}
