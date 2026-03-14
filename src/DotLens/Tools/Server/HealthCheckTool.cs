using System.ComponentModel;
using DotLens.Infrastructure;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Server;

[McpServerToolType]
[DotLensServerTool]
public class HealthCheckTool
{
   private readonly IMultiWorkerManager _workerManager;
   private readonly ILogger<HealthCheckTool> _logger;

   public HealthCheckTool(IMultiWorkerManager workerManager, ILogger<HealthCheckTool> logger)
   {
      _workerManager = workerManager;
      _logger = logger;
   }

   [McpServerTool(Name = "health_check", ReadOnly = true, Idempotent = true, OpenWorld = false)]
   [Description("Check the health and status of the DotLens server and loaded solutions.")]
   public async Task<ToolResult<HealthCheckResponse>> HealthCheckAsync(CancellationToken cancellationToken = default)
   {
      var workers = _workerManager.Workers;
      var activePath = _workerManager.ActiveSolutionPath;
      var loadingPaths = _workerManager.LoadingPaths;
      var failedLoads = _workerManager.FailedLoads;

      if (workers.Count == 0 && loadingPaths.Count == 0)
      {
         if (failedLoads.Count > 0)
         {
            var failures = failedLoads.Select(f => $"{Path.GetFileName(f.Key)}: {f.Value}");
            return ToolResult.Create(
               new HealthCheckResponse
               {
                  Status = "Not Ready",
                  Message = $"Solution failed to load: {string.Join("; ", failures)}",
                  TotalSolutions = 0,
                  ActiveSolution = null,
                  FailedSolutions = failedLoads.Keys.ToList(),
                  Hint = "Check the solution path and try load_solution again."
               });
         }

         return ToolResult.Create(
            new HealthCheckResponse
            {
               Status = "Not Ready",
               Message = "No solution loaded. Call load_solution first or set DOTLENS_SolutionPath environment variable.",
               TotalSolutions = 0,
               ActiveSolution = null
            });
      }

      if (workers.Count == 0 && loadingPaths.Count > 0)
      {
         return ToolResult.Create(
            new HealthCheckResponse
            {
               Status = "Loading",
               Message = $"Loading {loadingPaths.Count} solution(s) in the background: {string.Join(", ", loadingPaths.Select(Path.GetFileName))}",
               TotalSolutions = 0,
               ActiveSolution = null,
               LoadingSolutions = loadingPaths.ToList(),
               Hint = "Solution is loading. Try again in a few seconds."
            });
      }

      try
      {
         // Get health check from active worker
         var activeHealthResult = await _workerManager.CallToolAsync<object>(
             "worker_health_check",
             new Dictionary<string, object?>(),
             cancellationToken);

         // Build summary of all loaded solutions
         var solutionSummary = workers.Select(w => new SolutionSummary
         {
            Path = w.Key,
            IsActive = w.Key == activePath,
            LoadedAt = w.Value.LoadedAt
         }).ToList();

         return ToolResult.Create(
            new HealthCheckResponse
            {
               Status = "Ready",
               TotalSolutions = workers.Count,
               ActiveSolution = activePath,
               Solutions = solutionSummary,
               LoadingSolutions = loadingPaths.Count > 0 ? loadingPaths.ToList() : null,
               ActiveWorkerHealth = activeHealthResult
            });
      }
      catch (Exception ex)
      {
         _logger.LogWarning(ex, "Health check failed - worker may have crashed");
         return ToolResult.Create(
            new HealthCheckResponse
            {
               Status = "Error",
               Message = $"Worker communication failed: {ex.Message}",
               TotalSolutions = workers.Count,
               ActiveSolution = activePath,
               Hint = "Try calling load_solution to restart the worker"
            });
      }
   }
}

#region Result Types

/// <summary>
/// A loaded solution summary.
/// </summary>
public record SolutionSummary
{
   [Description("Absolute path to the solution file.")]
   public required string Path { get; init; }

   [Description("Whether this is the currently active solution.")]
   public required bool IsActive { get; init; }

   [Description("When this solution was loaded (UTC).")]
   public required DateTime LoadedAt { get; init; }
}

/// <summary>
/// Result from health check - unified type for all states (Ready, Loading, Not Ready, Error).
/// </summary>
public record HealthCheckResponse
{
   [Description("Health status: Ready, Loading, Not Ready, or Error.")]
   public required string Status { get; init; }

   [Description("Human-readable status message.")]
   public string? Message { get; init; }

   [Description("Number of solutions currently loaded.")]
   public required int TotalSolutions { get; init; }

   [Description("Path to the currently active solution.")]
   public string? ActiveSolution { get; init; }

   [Description("List of all loaded solutions.")]
   public IReadOnlyList<SolutionSummary> Solutions { get; init; } = [];

   [Description("Solutions currently being loaded in the background.")]
   public IReadOnlyList<string>? LoadingSolutions { get; init; }

   [Description("Solutions that failed to load, with error details.")]
   public IReadOnlyList<string>? FailedSolutions { get; init; }

   [Description("Health details from the active worker process.")]
   public object? ActiveWorkerHealth { get; init; }

   [Description("Suggested action if not ready.")]
   public string? Hint { get; init; }
}

#endregion
