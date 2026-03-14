using System.ComponentModel;
using DotLens.Infrastructure;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Server;

#region Response Types

/// <summary>
/// Result from reload solution.
/// </summary>
public record ReloadSolutionResponse
{
   [Description("Reload status: reloaded.")]
   public required string Status { get; init; }

   [Description("Absolute path to the reloaded solution.")]
   public required string SolutionPath { get; init; }

   [Description("Number of solutions currently loaded.")]
   public required int TotalSolutions { get; init; }

   [Description("When the solution was reloaded (UTC).")]
   public DateTime? ReloadedAt { get; init; }

   [Description("Number of projects in the solution.")]
   public int? ProjectCount { get; init; }

   [Description("Total number of source documents.")]
   public int? DocumentCount { get; init; }
}

#endregion

[McpServerToolType]
[DotLensServerTool]
public class ReloadSolutionTool
{
   private readonly IMultiWorkerManager _workerManager;
   private readonly ILogger<ReloadSolutionTool> _logger;

   public ReloadSolutionTool(IMultiWorkerManager workerManager, ILogger<ReloadSolutionTool> logger)
   {
      _workerManager = workerManager;
      _logger = logger;
   }

   [McpServerTool(Name = "reload_solution", Destructive = true, Idempotent = true, OpenWorld = false)]
   [Description("Reload the current solution to pick up new files or project changes. Briefly releases all file locks during reload.")]
   public async Task<ToolResult<ReloadSolutionResponse>> ReloadSolutionAsync(
       [Description("Path to solution to reload. If not specified, reloads the active solution.")]
       string? solutionPath = null,
       [Description("Optional timeout in seconds for loading. Overrides default TimeoutSeconds config.")]
       int? timeoutSeconds = null,
       CancellationToken cancellationToken = default)
   {
      var pathToReload = solutionPath ?? _workerManager.ActiveSolutionPath;

      if (pathToReload == null)
      {
         throw new ToolException(
             ErrorCodes.NoSolutionLoaded,
             "No solution to reload. Call load_solution first.",
             "Use load_solution to load a solution");
      }

      // Create timeout-linked cancellation token if timeout specified
      using var timeoutCts = timeoutSeconds.HasValue
          ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
          : null;
      if (timeoutCts != null)
         timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds!.Value));
      var effectiveToken = timeoutCts?.Token ?? cancellationToken;

      try
      {
         _logger.LogInformation("Reloading solution: {Path}", pathToReload);

         // Unload
         await _workerManager.UnloadSolutionAsync(pathToReload, effectiveToken);

         // Load again (LoadSolutionAsync handles worker_load_solution internally)
         var result = await _workerManager.LoadSolutionAsync(pathToReload, effectiveToken);

         _logger.LogInformation("Solution reloaded: {Path}", pathToReload);

         return ToolResult.Create(
            new ReloadSolutionResponse
            {
               Status = "reloaded",
               SolutionPath = pathToReload,
               TotalSolutions = _workerManager.WorkerCount,
               ReloadedAt = result.Instance.LoadedAt,
               ProjectCount = result.Instance.ProjectCount,
               DocumentCount = result.Instance.DocumentCount
            });
      }
      catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true)
      {
         _logger.LogError("Timed out reloading solution after {Timeout}s: {Path}",
             timeoutSeconds, pathToReload);
         throw new ToolException(
             ErrorCodes.Timeout,
             $"Timed out reloading solution after {timeoutSeconds}s",
             "Try increasing timeoutSeconds for large solutions");
      }
      catch (TimeoutException ex)
      {
         _logger.LogError(ex, "Solution reload timed out: {Path}", pathToReload);
         throw new ToolException(
             ErrorCodes.Timeout,
             ex.Message,
             "The solution load exceeded the maximum time. Consider increasing DOTLENS_MaxLoadTimeoutSeconds for very large solutions.");
      }
      catch (Exception ex)
      {
         _logger.LogError(ex, "Failed to reload solution: {Path}", pathToReload);
         throw new ToolException(
             ErrorCodes.AnalysisFailed,
             $"Failed to reload solution: {ex.Message}",
             "Check if the solution file is valid and try load_solution manually");
      }
   }
}
