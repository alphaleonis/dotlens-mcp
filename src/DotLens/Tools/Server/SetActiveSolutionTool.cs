using System.ComponentModel;
using DotLens.Infrastructure;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Server;

#region Response Types

/// <summary>
/// Result from set active solution.
/// </summary>
public record SetActiveSolutionResponse
{
   [Description("Human-readable status message.")]
   public required string Message { get; init; }

   [Description("Path to the newly active solution.")]
   public required string ActiveSolution { get; init; }

   [Description("Number of solutions currently loaded.")]
   public required int TotalSolutions { get; init; }
}

#endregion

[McpServerToolType]
[DotLensServerTool]
public class SetActiveSolutionTool
{
   private readonly IMultiWorkerManager _workerManager;

   public SetActiveSolutionTool(IMultiWorkerManager workerManager)
   {
      _workerManager = workerManager;
   }

   [McpServerTool(Name = "set_active_solution", Destructive = false, Idempotent = true, OpenWorld = false)]
   [Description("Switch the active solution context. All subsequent tool calls will target this solution.")]
   public ToolResult<SetActiveSolutionResponse> SetActiveSolution(
       [Description("Path to a previously loaded solution")] string solutionPath)
   {
      try
      {
         _workerManager.SetActiveSolution(solutionPath);

         return ToolResult.Create(
            new SetActiveSolutionResponse
            {
               Message = "Active solution switched",
               ActiveSolution = solutionPath,
               TotalSolutions = _workerManager.WorkerCount
            });
      }
      catch (InvalidOperationException ex)
      {
         throw new ToolException(
             ErrorCodes.NoSolutionLoaded,
             ex.Message,
             "Use list_solutions to see loaded solutions, or load_solution to load a new one.");
      }
   }
}
