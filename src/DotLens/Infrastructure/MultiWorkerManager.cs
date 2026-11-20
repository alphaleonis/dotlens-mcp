using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace DotLens.Infrastructure;

/// <summary>
/// Represents a single worker process managing one solution.
/// </summary>
public class WorkerInstance : IAsyncDisposable
{
   public required string SolutionPath { get; init; }
   public required McpClient Client { get; init; }
   public DateTime LoadedAt { get; init; }
   public int? ProjectCount { get; internal set; }
   public int? DocumentCount { get; internal set; }

   /// <summary>
   /// The messages of the problems that the worker's load found, as <see cref="LoadWarning"/> describes them.
   /// </summary>
   public IReadOnlyList<string> LoadWarnings { get; internal set; } = [];

   public async ValueTask DisposeAsync()
   {
      try
      {
         await Client.DisposeAsync();
      }
      catch
      {
         // Expected: Process may already be dead, pipe broken, or already disposed.
         // No logging: disposal errors are normal during cleanup and would create noise.
      }
      GC.SuppressFinalize(this);
   }
}

/// <summary>
/// Manages multiple worker processes, one per loaded solution.
/// Maintains an "active solution" context for routing tool calls.
/// Provides load coalescing: concurrent loads for the same path share one task.
/// </summary>
public class MultiWorkerManager : IMultiWorkerManager
{
   private readonly ILogger<MultiWorkerManager> _logger;
   private readonly DotLensOptions _options;
   private readonly Dictionary<string, WorkerInstance> _workers = new(StringComparer.OrdinalIgnoreCase);
   private readonly Dictionary<string, PendingLoad> _loadingTasks = new(StringComparer.OrdinalIgnoreCase);
   private readonly HashSet<string> _cancelledPaths = new(StringComparer.OrdinalIgnoreCase);
   private readonly Dictionary<string, string> _failedLoads = new(StringComparer.OrdinalIgnoreCase);
   private readonly CancellationTokenSource _shutdownCts = new();
   private readonly object _lock = new();
   private readonly Func<string, CancellationToken, Task<WorkerInstance>> _loadWorker;
   private string? _activeSolutionPath;

   /// <summary>
   /// For each loaded solution, the number of the latest request that named it. See <see cref="TryActivate"/>.
   /// </summary>
   private readonly Dictionary<string, long> _solutionRequests = new(StringComparer.OrdinalIgnoreCase);

   /// <summary>
   /// The number of the latest explicit request.
   /// </summary>
   private long _lastRequest = AutoLoadRequest;

   /// <summary>
   /// The request number of the auto-load, below the number of every explicit request: load_solution, reload_solution and
   /// set_active_solution.
   /// </summary>
   private const long AutoLoadRequest = 0;

   public MultiWorkerManager(ILogger<MultiWorkerManager> logger, IOptions<DotLensOptions> options)
   {
      _logger = logger;
      _options = options.Value;
      _loadWorker = LoadWorkerAsync;
   }

   /// <summary>
   /// Creates a manager whose loads start a worker with <paramref name="loadWorker"/>, so tests control when loads finish.
   /// </summary>
   internal MultiWorkerManager(
      ILogger<MultiWorkerManager> logger,
      IOptions<DotLensOptions> options,
      Func<string, CancellationToken, Task<WorkerInstance>> loadWorker)
   {
      _logger = logger;
      _options = options.Value;
      _loadWorker = loadWorker;
   }

   /// <summary>
   /// Gets the path of the currently active solution, or null if none loaded.
   /// </summary>
   public string? ActiveSolutionPath
   {
      get
      {
         lock (_lock)
         {
            return _activeSolutionPath;
         }
      }
   }

   /// <summary>
   /// Returns true if there is an active solution with a running worker.
   /// </summary>
   public bool HasActiveWorker
   {
      get
      {
         lock (_lock)
         {
            return _activeSolutionPath != null && _workers.ContainsKey(_activeSolutionPath);
         }
      }
   }

   /// <summary>
   /// Gets a read-only snapshot of all loaded workers.
   /// </summary>
   public IReadOnlyDictionary<string, WorkerInstance> Workers
   {
      get
      {
         lock (_lock)
         {
            return new Dictionary<string, WorkerInstance>(_workers, StringComparer.OrdinalIgnoreCase);
         }
      }
   }

   /// <summary>
   /// Gets the number of loaded solutions.
   /// </summary>
   public int WorkerCount
   {
      get
      {
         lock (_lock)
         {
            return _workers.Count;
         }
      }
   }

   /// <summary>
   /// Returns true if a solution is currently being loaded.
   /// </summary>
   public bool IsLoading(string solutionPath)
   {
      var normalized = NormalizePath(solutionPath);
      lock (_lock)
      {
         return _loadingTasks.ContainsKey(normalized);
      }
   }

   /// <summary>
   /// Gets the paths of all solutions currently being loaded.
   /// </summary>
   public IReadOnlyCollection<string> LoadingPaths
   {
      get
      {
         lock (_lock)
         {
            return _loadingTasks.Keys.ToList();
         }
      }
   }

   /// <summary>
   /// Gets a snapshot of solutions that failed to load (path -> error message).
   /// Cleared when a new load is started for the same path.
   /// </summary>
   public IReadOnlyDictionary<string, string> FailedLoads
   {
      get
      {
         lock (_lock)
         {
            return new Dictionary<string, string>(_failedLoads, StringComparer.OrdinalIgnoreCase);
         }
      }
   }

   /// <summary>
   /// Gets the MCP client for the active solution.
   /// Throws if no solution is loaded.
   /// </summary>
   public McpClient GetActiveClient()
   {
      lock (_lock)
      {
         if (_activeSolutionPath == null || !_workers.TryGetValue(_activeSolutionPath, out var worker))
         {
            throw new InvalidOperationException(
                "No solution loaded. Call load_solution first.");
         }
         return worker.Client;
      }
   }

   /// <summary>
   /// Gets a specific worker by solution path, or null if not found.
   /// </summary>
   public WorkerInstance? GetWorker(string solutionPath)
   {
      var normalized = NormalizePath(solutionPath);
      lock (_lock)
      {
         return _workers.GetValueOrDefault(normalized);
      }
   }

   /// <summary>
   /// Loads a solution, spawning a new worker process if needed.
   /// Makes the solution active, unless the active solution was named by a later request.
   /// If a load is already in progress for the same path, joins the existing task
   /// (load coalescing). The caller's cancellation token controls how long to wait,
   /// but does not cancel the underlying load.
   /// </summary>
   public Task<LoadSolutionResult> LoadSolutionAsync(string solutionPath, CancellationToken cancellationToken = default) =>
      LoadAsync(solutionPath, explicitRequest: true, cancellationToken);

   /// <summary>
   /// Loads the solution that the server found at startup, as <see cref="LoadSolutionAsync"/> does.
   /// </summary>
   /// <remarks>
   /// The solution becomes active only while no solution is active. See <see cref="TryActivate"/>.
   /// </remarks>
   public Task<LoadSolutionResult> AutoLoadSolutionAsync(string solutionPath, CancellationToken cancellationToken = default) =>
      LoadAsync(solutionPath, explicitRequest: false, cancellationToken);

   private async Task<LoadSolutionResult> LoadAsync(string solutionPath, bool explicitRequest, CancellationToken cancellationToken)
   {
      var normalized = NormalizePath(solutionPath);
      Task<LoadSolutionResult> loadTask;

      lock (_lock)
      {
         var request = explicitRequest ? ++_lastRequest : AutoLoadRequest;

         // Already loaded? Return immediately.
         if (_workers.TryGetValue(normalized, out var existing))
         {
            var activated = TryActivate(normalized, request);
            _logger.LogInformation("Solution already loaded: {Path}, active: {Active}", normalized, activated);
            return new LoadSolutionResult(existing, WasAlreadyLoaded: true, _activeSolutionPath);
         }

         // Load in progress? Join the existing task.
         if (_loadingTasks.TryGetValue(normalized, out var pending))
         {
            _logger.LogInformation("Joining existing load for: {Path}", normalized);
            pending.Request = Math.Max(pending.Request, request);
         }
         else
         {
            // Start new load. The core task has its own finite timeout
            // (MaxLoadTimeoutSeconds) but is not tied to the caller's token,
            // so a caller timing out does not cancel the underlying load.
            _logger.LogInformation("Starting new load for: {Path}", normalized);
            _failedLoads.Remove(normalized);
            pending = new PendingLoad(request, load => LoadSolutionCoreAsync(normalized, load));
            _loadingTasks[normalized] = pending;
         }
         loadTask = pending.Task;
      }

      // Wait for the load with the caller's cancellation token.
      // If the caller times out, the underlying load continues in the background.
      return await loadTask.WaitAsync(cancellationToken);
   }

   /// <summary>
   /// Notes that a request named the loaded solution, and makes the solution active unless the active solution was named by a
   /// later request. Call it under <see cref="_lock"/>.
   /// </summary>
   /// <remarks>
   /// Explicit requests take increasing numbers when they start, so the latest request wins, not the load that finishes last.
   /// The auto-load has <see cref="AutoLoadRequest"/>, so it wins only while no solution is active. A request whose load fails
   /// never calls this, so the active solution stays as it was. When the active solution is unloaded,
   /// <see cref="LatestNamedSolution"/> picks the replacement by the same numbers, so the outcome does not depend on whether a
   /// load finishes before or after the unload.
   /// </remarks>
   /// <returns>Whether the solution became active.</returns>
   private bool TryActivate(string normalizedPath, long request)
   {
      _solutionRequests[normalizedPath] = Math.Max(_solutionRequests.GetValueOrDefault(normalizedPath, AutoLoadRequest), request);

      if (_activeSolutionPath is { } activePath
         && !string.Equals(activePath, normalizedPath, StringComparison.OrdinalIgnoreCase)
         && _solutionRequests[activePath] >= request)
      {
         return false;
      }

      _activeSolutionPath = normalizedPath;
      return true;
   }

   /// <summary>
   /// Returns the loaded solution that the latest request named, with ties broken by ordinal path. Call it under
   /// <see cref="_lock"/>.
   /// </summary>
   /// <returns>The solution's path, or null when no solution is loaded.</returns>
   private string? LatestNamedSolution() =>
      _solutionRequests
         .OrderByDescending(solution => solution.Value)
         .ThenBy(solution => solution.Key, StringComparer.Ordinal)
         .Select(solution => solution.Key)
         .FirstOrDefault();

   /// <summary>
   /// Core load: starts the worker, stores it, and makes it active for the latest request that waits on it.
   /// This task is shared across all callers loading the same path (coalescing).
   /// Uses a finite timeout (MaxLoadTimeoutSeconds) to prevent indefinite hangs,
   /// but does not accept a caller-specific token to avoid canceling a shared operation.
   /// </summary>
   private async Task<LoadSolutionResult> LoadSolutionCoreAsync(string normalizedPath, PendingLoad pending)
   {
      // Run the rest outside the caller's lock, so the caller registers this load in _loadingTasks before it can finish.
      await Task.Yield();

      // Use a finite timeout to prevent indefinite hangs if the worker process
      // or MSBuild host gets stuck. Without this, a hung worker leaks forever.
      // Linked to _shutdownCts so server disposal cancels in-flight loads promptly.
      using var loadCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
      loadCts.CancelAfter(TimeSpan.FromSeconds(_options.MaxLoadTimeoutSeconds));
      var loadTimeoutToken = loadCts.Token;

      try
      {
         _logger.LogInformation("Loading new solution: {Path}", normalizedPath);
         var instance = await _loadWorker(normalizedPath, loadTimeoutToken);

         // Check if this path was unloaded while we were loading
         bool cancelled;
         var activated = false;
         string? activePath = null;
         lock (_lock)
         {
            cancelled = _cancelledPaths.Remove(normalizedPath);
            if (!cancelled)
            {
               _workers[normalizedPath] = instance;
               activated = TryActivate(normalizedPath, pending.Request);
               activePath = _activeSolutionPath;
            }
         }

         if (cancelled)
         {
            _logger.LogInformation("Load completed but path was unloaded during loading, disposing: {Path}", normalizedPath);
            await instance.DisposeAsync();
            throw new OperationCanceledException($"Solution was unloaded during loading: {normalizedPath}");
         }

         _logger.LogInformation("Solution loaded successfully: {Path}, active: {Active}", normalizedPath, activated);
         return new LoadSolutionResult(instance, WasAlreadyLoaded: false, activePath);
      }
      catch (OperationCanceledException) when (_shutdownCts.IsCancellationRequested)
      {
         // Server is shutting down — don't record as a load failure.
         throw;
      }
      catch (OperationCanceledException) when (loadCts.IsCancellationRequested)
      {
         // The finite load timeout fired — record as a failure so callers get a clear message.
         _logger.LogError("Solution load timed out after {TimeoutSeconds}s: {Path}",
             _options.MaxLoadTimeoutSeconds, normalizedPath);
         var message = $"Solution load timed out after {_options.MaxLoadTimeoutSeconds}s: {normalizedPath}";
         lock (_lock)
         {
            _failedLoads[normalizedPath] = message;
         }
         throw new TimeoutException(message);
      }
      catch (OperationCanceledException)
      {
         // Intentional cancellation (e.g., unload-during-load) — don't record as a failure.
         throw;
      }
      catch (Exception ex)
      {
         _logger.LogError(ex, "Failed to load solution: {Path}", normalizedPath);
         lock (_lock)
         {
            _failedLoads[normalizedPath] = ex.Message;
         }
         throw;
      }
      finally
      {
         lock (_lock)
         {
            _loadingTasks.Remove(normalizedPath);
            _cancelledPaths.Remove(normalizedPath);
         }
      }
   }

   /// <summary>
   /// Spawns a worker process and calls worker_load_solution on it, reading the counts and warnings it returns.
   /// </summary>
   private async Task<WorkerInstance> LoadWorkerAsync(string normalizedPath, CancellationToken cancellationToken)
   {
      var instance = await SpawnWorkerAsync(normalizedPath, cancellationToken);

      try
      {
         var result = await instance.Client.CallToolAsync(
             "worker_load_solution",
             new Dictionary<string, object?> { ["solutionPath"] = normalizedPath },
             cancellationToken: cancellationToken);

         // The MCP SDK does NOT throw on tool execution errors — it returns
         // CallToolResult with IsError = true. We must check this explicitly.
         if (result.IsError == true)
         {
            var errorText = result.Content
                .OfType<ModelContextProtocol.Protocol.TextContentBlock>()
                .FirstOrDefault()?.Text ?? "Unknown error";
            if (ParseWorkerError(errorText) is { } workerError)
               throw workerError;
            throw new InvalidOperationException($"worker_load_solution failed: {errorText}");
         }

         // Extract project/document counts from the worker response
         var textContent = result.Content
             .OfType<ModelContextProtocol.Protocol.TextContentBlock>()
             .FirstOrDefault();

         if (textContent != null)
         {
            using var doc = JsonDocument.Parse(textContent.Text);
            if (doc.RootElement.TryGetProperty("data", out var dataElement))
            {
               if (dataElement.TryGetProperty("projectCount", out var pc))
                  instance.ProjectCount = pc.GetInt32();
               if (dataElement.TryGetProperty("documentCount", out var dc))
                  instance.DocumentCount = dc.GetInt32();
               if (dataElement.TryGetProperty("warnings", out var warnings)
                  && warnings.ValueKind == JsonValueKind.Array)
               {
                  instance.LoadWarnings = [.. warnings.EnumerateArray().Select(w => w.GetString()).OfType<string>()];
               }
            }
         }
      }
      catch (Exception ex)
      {
         _logger.LogError(ex, "worker_load_solution failed for {Path}, disposing spawned worker", normalizedPath);
         await instance.DisposeAsync();
         throw;
      }

      return instance;
   }

   /// <summary>
   /// Unloads a solution and terminates its worker process.
   /// If no path specified, unloads the active solution.
   /// The loaded solution that the latest request named replaces an unloaded active solution.
   /// </summary>
   public async Task UnloadSolutionAsync(string? solutionPath = null, CancellationToken cancellationToken = default)
   {
      WorkerInstance? workerToDispose = null;
      Task<LoadSolutionResult>? loadingTask = null;
      string? pathToUnload;

      lock (_lock)
      {
         pathToUnload = solutionPath != null ? NormalizePath(solutionPath) : _activeSolutionPath;

         if (pathToUnload == null)
         {
            _logger.LogInformation("No solution to unload");
            return;
         }

         if (_workers.TryGetValue(pathToUnload, out workerToDispose))
         {
            _workers.Remove(pathToUnload);
            _solutionRequests.Remove(pathToUnload);

            if (string.Equals(_activeSolutionPath, pathToUnload, StringComparison.OrdinalIgnoreCase))
               _activeSolutionPath = LatestNamedSolution();
         }

         // If a load is in progress for this path, mark it as cancelled.
         // LoadSolutionCoreAsync will check _cancelledPaths and dispose instead of storing.
         if (_loadingTasks.TryGetValue(pathToUnload, out var pending))
         {
            _cancelledPaths.Add(pathToUnload);
            loadingTask = pending.Task;
         }
      }

      if (workerToDispose != null)
      {
         _logger.LogInformation("Shutting down worker for solution: {Path}", pathToUnload);
         await workerToDispose.DisposeAsync();
         _logger.LogInformation("Worker shut down: {Path}", pathToUnload);
      }

      // Wait for in-progress load to complete and self-dispose (via _cancelledPaths check)
      if (loadingTask != null)
      {
         _logger.LogInformation("Waiting for in-progress load to cancel: {Path}", pathToUnload);
         try
         {
            await loadingTask;
         }
         catch { /* Expected: cancelled load throws OperationCanceledException */ }
      }
   }

   /// <summary>
   /// Unloads all solutions and terminates all worker processes.
   /// </summary>
   public async Task UnloadAllAsync()
   {
      List<WorkerInstance> workersToDispose;
      List<Task<LoadSolutionResult>> loadingTasksToAwait;

      lock (_lock)
      {
         workersToDispose = _workers.Values.ToList();
         _workers.Clear();
         _solutionRequests.Clear();
         _activeSolutionPath = null;

         // Cancel all in-progress loads so they don't resurrect workers after clear
         foreach (var path in _loadingTasks.Keys)
            _cancelledPaths.Add(path);
         loadingTasksToAwait = _loadingTasks.Values.Select(pending => pending.Task).ToList();
      }

      _logger.LogInformation("Shutting down {Count} workers, cancelling {LoadingCount} in-progress loads",
         workersToDispose.Count, loadingTasksToAwait.Count);

      foreach (var worker in workersToDispose)
      {
         try
         {
            await worker.DisposeAsync();
         }
         catch (Exception ex)
         {
            _logger.LogWarning(ex, "Error disposing worker for {Path}", worker.SolutionPath);
         }
      }

      // Wait for cancelled loads to complete and self-dispose
      foreach (var task in loadingTasksToAwait)
      {
         try
         {
            await task;
         }
         catch { /* Expected: cancelled loads throw OperationCanceledException */ }
      }

      _logger.LogInformation("All workers shut down");
   }

   /// <summary>
   /// Sets the active solution. The solution must already be loaded.
   /// A load that an earlier request started does not replace it when that load finishes.
   /// </summary>
   public void SetActiveSolution(string solutionPath)
   {
      var normalized = NormalizePath(solutionPath);

      // Validate that the path looks like a solution file
      if (!normalized.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) &&
          !normalized.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
      {
         throw new InvalidOperationException(
             $"Invalid solution path: {solutionPath}. Path must end with .sln or .slnx.");
      }

      lock (_lock)
      {
         if (!_workers.ContainsKey(normalized))
         {
            throw new InvalidOperationException(
                $"Solution not loaded: {solutionPath}. Load it first with load_solution.");
         }
         TryActivate(normalized, ++_lastRequest);
      }

      _logger.LogInformation("Active solution switched to: {Path}", normalized);
   }

   /// <summary>
   /// Forwards a tool call to the active worker process and deserializes the result.
   /// </summary>
   public async Task<T?> CallToolAsync<T>(
       string toolName,
       Dictionary<string, object?> arguments,
       CancellationToken cancellationToken = default)
   {
      McpClient client;

      try
      {
         client = GetActiveClient();
      }
      catch (InvalidOperationException)
      {
         throw new ToolException(
             ErrorCodes.NoSolutionLoaded,
             "No solution loaded. Call load_solution first.",
             "Use load_solution to start a worker and load a solution.");
      }

      try
      {
         var result = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);

         // The MCP SDK does NOT throw on tool execution errors — it returns
         // CallToolResult with IsError = true. Check this before parsing content.
         if (result.IsError == true)
            throw WorkerToolError(toolName, result);

         // Extract text content from MCP response
         var textContent = result.Content
             .OfType<ModelContextProtocol.Protocol.TextContentBlock>()
             .FirstOrDefault();

         if (textContent == null)
         {
            throw new ToolException(
                ErrorCodes.WorkerCommunicationFailed,
                "Worker returned empty response",
                "The worker process may have failed. Try calling load_solution again.");
         }

         // Worker tools return ToolResult<T> which serializes as { "data": T, ... }
         // We need to extract the "data" property and deserialize it to T
         using var doc = JsonDocument.Parse(textContent.Text);

         // Check for error responses from the worker (DotLens JSON format)
         if (doc.RootElement.TryGetProperty("isError", out var isErrorProp) && isErrorProp.GetBoolean())
         {
            var errorMsg = doc.RootElement.TryGetProperty("error", out var errProp)
                ? errProp.GetString() ?? "Unknown worker error"
                : "Unknown worker error";
            throw new ToolException(ErrorCodes.WorkerCommunicationFailed, errorMsg);
         }

         if (doc.RootElement.TryGetProperty("data", out var dataElement))
         {
            var deserialized = dataElement.Deserialize<T>(DotLensJsonOptions.Default);
            if (deserialized == null && dataElement.ValueKind != JsonValueKind.Null)
            {
               _logger.LogWarning("Failed to deserialize worker response to {Type}. Raw JSON: {Json}",
                   typeof(T).Name, dataElement.GetRawText());
            }
            return deserialized;
         }

         // Fallback: try deserializing the whole response
         return JsonSerializer.Deserialize<T>(textContent.Text, DotLensJsonOptions.Default);
      }
      catch (JsonException jsonEx)
      {
         _logger.LogError(jsonEx, "Invalid JSON response from worker for tool {ToolName}", toolName);
         throw new ToolException(
             ErrorCodes.WorkerCommunicationFailed,
             $"Worker returned invalid JSON: {jsonEx.Message}",
             "The worker process may have crashed or returned corrupt data.");
      }
      catch (OperationCanceledException)
      {
         throw;
      }
      catch (ToolException)
      {
         throw;
      }
      catch (Exception ex)
      {
         _logger.LogError(ex, "Error calling tool {ToolName} on worker", toolName);
         throw new ToolException(
             ErrorCodes.WorkerCommunicationFailed,
             $"Worker communication failed: {ex.Message}",
             "The worker process may have crashed. Try calling load_solution again.");
      }
   }

   /// <summary>
   /// Returns the exception for a worker reply that has IsError set.
   /// </summary>
   /// <returns>
   /// The worker's own error when the reply is a DotLens error response, or WORKER_COMMUNICATION_FAILED with the reply's
   /// text.
   /// </returns>
   internal static ToolException WorkerToolError(string toolName, ModelContextProtocol.Protocol.CallToolResult result)
   {
      var errorText = result.Content
          .OfType<ModelContextProtocol.Protocol.TextContentBlock>()
          .FirstOrDefault()?.Text ?? "Unknown worker error";
      return ParseWorkerError(errorText) ?? new ToolException(
          ErrorCodes.WorkerCommunicationFailed,
          $"Worker tool '{toolName}' failed: {errorText}",
          "The worker reported an error. Check logs for details.");
   }

   /// <summary>
   /// Returns the error that a worker's DotLens error response describes, with its code, message, hint and context.
   /// </summary>
   /// <returns>The error, or null when the text is not a DotLens error response.</returns>
   internal static ToolException? ParseWorkerError(string text)
   {
      try
      {
         using var doc = JsonDocument.Parse(text);
         if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("error", out var error)
            || error.ValueKind != JsonValueKind.Object
            || !error.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.String
            || !error.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.String)
         {
            return null;
         }

         var hint = error.TryGetProperty("hint", out var hintElement) && hintElement.ValueKind == JsonValueKind.String
            ? hintElement.GetString()
            : null;
         object? context = error.TryGetProperty("context", out var contextElement)
            && contextElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? contextElement.Clone()
            : null;
         return new ToolException(code.GetString() ?? "", message.GetString() ?? "", hint, context);
      }
      catch (JsonException)
      {
         return null;
      }
   }

   private async Task<WorkerInstance> SpawnWorkerAsync(string solutionPath, CancellationToken cancellationToken)
   {
      var exePath = Environment.ProcessPath
          ?? throw new InvalidOperationException("Cannot determine executable path");

      // A server started as "dotnet DotLens.dll" runs in the dotnet host, which needs the assembly to run.
      string[] arguments = string.Equals(Path.GetFileNameWithoutExtension(exePath), "dotnet", StringComparison.OrdinalIgnoreCase)
         ? [typeof(MultiWorkerManager).Assembly.Location, "--worker"]
         : ["--worker"];

      _logger.LogInformation("Starting worker process: {ExePath} {Arguments}", exePath, string.Join(" ", arguments));

      var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
      {
         Command = exePath,
         Arguments = arguments,
         Name = $"DotLens Worker ({Path.GetFileName(solutionPath)})"
      });

      var client = await McpClient.CreateAsync(clientTransport, cancellationToken: cancellationToken);

      _logger.LogInformation("Worker process started for: {Path}", solutionPath);

      return new WorkerInstance
      {
         SolutionPath = solutionPath,
         Client = client,
         LoadedAt = DateTime.UtcNow
      };
   }

   private static string NormalizePath(string path)
   {
      // Normalize to full path with consistent casing for dictionary lookups
      return Path.GetFullPath(path);
   }

   /// <summary>
   /// A load in progress and the request that it makes active when it finishes.
   /// </summary>
   private sealed class PendingLoad
   {
      public PendingLoad(long request, Func<PendingLoad, Task<LoadSolutionResult>> start)
      {
         Request = request;
         Task = start(this);
      }

      public Task<LoadSolutionResult> Task { get; }

      /// <summary>
      /// The highest number of the requests that wait on this load. See <see cref="TryActivate"/>.
      /// </summary>
      public long Request { get; set; }
   }

   public async ValueTask DisposeAsync()
   {
      _shutdownCts.Cancel();
      await UnloadAllAsync();
      _shutdownCts.Dispose();
      GC.SuppressFinalize(this);
   }
}
