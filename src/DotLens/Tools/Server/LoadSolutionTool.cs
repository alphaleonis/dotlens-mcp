using System.ComponentModel;
using DotLens.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Server;

#region Response Types

/// <summary>
/// Result from load solution - unified type for all states (loaded, already_loaded, loading).
/// </summary>
public record LoadSolutionResponse
{
   [Description("Load status: loaded, already_loaded, or loading.")]
   public required string Status { get; init; }

   [Description("Human-readable status message.")]
   public string? Message { get; init; }

   [Description("Absolute path to the solution.")]
   public required string SolutionPath { get; init; }

   [Description("Number of solutions currently loaded.")]
   public required int TotalSolutions { get; init; }

   [Description("When this solution was loaded (UTC).")]
   public DateTime? LoadedAt { get; init; }

   [Description("Number of projects in the solution.")]
   public int? ProjectCount { get; init; }

   [Description("Total number of source documents.")]
   public int? DocumentCount { get; init; }

   [Description("Suggested action if not yet ready.")]
   public string? Hint { get; init; }
}

#endregion

[McpServerToolType]
[DotLensServerTool]
public class LoadSolutionTool
{
   private readonly IMultiWorkerManager _workerManager;
   private readonly IOptions<DotLensOptions> _options;
   private readonly ILogger<LoadSolutionTool> _logger;

   public LoadSolutionTool(IMultiWorkerManager workerManager, IOptions<DotLensOptions> options, ILogger<LoadSolutionTool> logger)
   {
      _workerManager = workerManager;
      _options = options;
      _logger = logger;
   }

   [McpServerTool(Name = "load_solution", Destructive = true, Idempotent = true, OpenWorld = false)]
   [Description("Load a .NET solution for analysis. Must be called before any other tools. The loaded solution becomes active.")]
   public async Task<ToolResult<LoadSolutionResponse>> LoadSolutionAsync(
       [Description("Absolute path to .sln or .slnx file")] string solutionPath,
       [Description("Optional timeout in seconds. Overrides default config.")]
       int? timeoutSeconds = null,
       CancellationToken cancellationToken = default)
   {
      if (!File.Exists(solutionPath))
      {
         throw new ToolException(
             ErrorCodes.FileNotFound,
             $"Solution file not found: {solutionPath}",
             "Verify the path is correct and the file exists");
      }

      // Determine if a load is already in progress for this path (e.g., background auto-load)
      var isAlreadyLoading = _workerManager.IsLoading(solutionPath);

      // User-provided timeout always takes priority.
      // Otherwise: short wait when joining an existing load, full timeout for new loads.
      var effectiveTimeoutSeconds = timeoutSeconds
          ?? (isAlreadyLoading
              ? _options.Value.LoadWaitTimeoutSeconds
              : _options.Value.TimeoutSeconds);

      using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      timeoutCts.CancelAfter(TimeSpan.FromSeconds(effectiveTimeoutSeconds));

      try
      {
         var result = await _workerManager.LoadSolutionAsync(solutionPath, timeoutCts.Token);

         if (result.WasAlreadyLoaded)
         {
            _logger.LogInformation("Solution already loaded, switched active: {SolutionPath}", solutionPath);
            return ToolResult.Create(
               new LoadSolutionResponse
               {
                  Status = "already_loaded",
                  Message = "Solution already loaded, switched to active",
                  SolutionPath = solutionPath,
                  LoadedAt = result.Instance.LoadedAt,
                  TotalSolutions = _workerManager.WorkerCount,
                  ProjectCount = result.Instance.ProjectCount,
                  DocumentCount = result.Instance.DocumentCount
               });
         }

         // Freshly loaded
         _logger.LogInformation("Solution loaded: {SolutionPath}, total: {Count}",
             solutionPath, _workerManager.WorkerCount);

         return ToolResult.Create(
            new LoadSolutionResponse
            {
               Status = "loaded",
               SolutionPath = solutionPath,
               TotalSolutions = _workerManager.WorkerCount,
               LoadedAt = result.Instance.LoadedAt,
               ProjectCount = result.Instance.ProjectCount,
               DocumentCount = result.Instance.DocumentCount
            });
      }
      catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
      {
         // Our timeout expired, not the caller's token.
         // If a load is still in progress, report it as "loading" instead of an error.
         if (_workerManager.IsLoading(solutionPath))
         {
            _logger.LogInformation(
                "Solution load in progress, waited {Timeout}s: {SolutionPath}",
                effectiveTimeoutSeconds, solutionPath);

            return ToolResult.Create(
               new LoadSolutionResponse
               {
                  Status = "loading",
                  Message = $"Solution is loading in the background. Waited {effectiveTimeoutSeconds}s. Try again in a few seconds.",
                  SolutionPath = solutionPath,
                  TotalSolutions = _workerManager.WorkerCount,
                  Hint = "Call load_solution again or use health_check to monitor progress."
               });
         }

         // Timeout expired but load is no longer in progress — it may have failed
         _logger.LogError("Timed out loading solution after {Timeout}s: {SolutionPath}",
             effectiveTimeoutSeconds, solutionPath);
         throw new ToolException(
             ErrorCodes.Timeout,
             $"Timed out loading solution after {effectiveTimeoutSeconds}s",
             "Try increasing timeoutSeconds for large solutions");
      }
      catch (OperationCanceledException)
      {
         throw;
      }
      catch (TimeoutException ex)
      {
         _logger.LogError(ex, "Solution load timed out: {SolutionPath}", solutionPath);
         throw new ToolException(
             ErrorCodes.Timeout,
             ex.Message,
             "The solution load exceeded the maximum time. Consider increasing DOTLENS_MaxLoadTimeoutSeconds for very large solutions.");
      }
      catch (Exception ex)
      {
         _logger.LogError(ex, "Failed to load solution: {SolutionPath}", solutionPath);
         throw new ToolException(
             ErrorCodes.AnalysisFailed,
             $"Failed to load solution: {ex.Message}",
             "Check if the solution file is valid and all project references exist");
      }
   }
}
