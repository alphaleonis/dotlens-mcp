using System.ComponentModel;
using DotLens.Infrastructure;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Server;

#region Response Types

/// <summary>
/// Result from unload solution.
/// </summary>
public record UnloadSolutionResponse
{
   [Description("Human-readable status message.")]
   public required string Message { get; init; }

   [Description("Unload status: unloaded or already_unloaded.")]
   public required string Status { get; init; }

   [Description("Path to the unloaded solution.")]
   public string? UnloadedSolution { get; init; }

   [Description("Number of solutions still loaded.")]
   public int? RemainingSolutions { get; init; }

   [Description("Path to the new active solution (if any).")]
   public string? ActiveSolution { get; init; }

   [Description("Suggested actions after unload.")]
   public IReadOnlyList<string>? NextSteps { get; init; }
}

#endregion

[McpServerToolType]
[DotLensServerTool]
public class UnloadSolutionTool
{
   private static readonly string[] NextStepsRemaining = ["Continue working with remaining solutions", "Or call load_solution to add more"];
   private static readonly string[] NextStepsNoRemaining = ["Run your build commands (dotnet build, etc.)", "Call load_solution to reload when ready"];
   private static readonly string[] SuggestedNextTools = ["load_solution"];

   private readonly IMultiWorkerManager _workerManager;
   private readonly ILogger<UnloadSolutionTool> _logger;

   public UnloadSolutionTool(IMultiWorkerManager workerManager, ILogger<UnloadSolutionTool> logger)
   {
      _workerManager = workerManager;
      _logger = logger;
   }

   [McpServerTool(Name = "unload_solution", Destructive = true, Idempotent = true, OpenWorld = false)]
   [Description("Unload a solution and release all file locks held by MSBuildWorkspace. Terminates the worker process. Call load_solution to reload when ready.")]
   public async Task<ToolResult<UnloadSolutionResponse>> UnloadSolutionAsync(
       [Description("Path to solution to unload. If not specified, unloads the active solution.")]
       string? solutionPath = null,
       CancellationToken cancellationToken = default)
   {
      if (_workerManager.WorkerCount == 0)
      {
         return ToolResult.Create(
            new UnloadSolutionResponse
            {
               Message = "No solutions are loaded",
               Status = "already_unloaded"
            });
      }

      var pathToUnload = solutionPath ?? _workerManager.ActiveSolutionPath;
      if (pathToUnload == null)
      {
         return ToolResult.Create(
            new UnloadSolutionResponse
            {
               Message = "No active solution to unload",
               Status = "already_unloaded"
            });
      }

      try
      {
         _logger.LogInformation("Unloading solution: {Path}", pathToUnload);
         await _workerManager.UnloadSolutionAsync(pathToUnload, cancellationToken);

         var remaining = _workerManager.WorkerCount;
         var newActive = _workerManager.ActiveSolutionPath;

         return ToolResult.Create(
             new UnloadSolutionResponse
             {
                Message = "Solution unloaded successfully. All file locks released.",
                Status = "unloaded",
                UnloadedSolution = pathToUnload,
                RemainingSolutions = remaining,
                ActiveSolution = newActive,
                NextSteps = remaining > 0 ? NextStepsRemaining : NextStepsNoRemaining
             },
             suggestedNextTools: SuggestedNextTools);
      }
      catch (Exception ex)
      {
         _logger.LogError(ex, "Error unloading solution: {Path}", pathToUnload);
         throw new ToolException(
             ErrorCodes.AnalysisFailed,
             $"Error unloading solution: {ex.Message}",
             "The worker process may need to be killed manually");
      }
   }
}
