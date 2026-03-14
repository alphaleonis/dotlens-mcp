namespace DotLens.Infrastructure;

/// <summary>
/// Result of loading a solution, including whether it was already loaded.
/// </summary>
public record LoadSolutionResult(WorkerInstance Instance, bool WasAlreadyLoaded);

/// <summary>
/// Interface for managing multiple worker processes, one per loaded solution.
/// </summary>
public interface IMultiWorkerManager : IAsyncDisposable
{
   /// <summary>
   /// Gets the path of the currently active solution, or null if none loaded.
   /// </summary>
   string? ActiveSolutionPath { get; }

   /// <summary>
   /// Returns true if there is an active solution with a running worker.
   /// </summary>
   bool HasActiveWorker { get; }

   /// <summary>
   /// Gets a read-only snapshot of all loaded workers.
   /// </summary>
   IReadOnlyDictionary<string, WorkerInstance> Workers { get; }

   /// <summary>
   /// Gets the number of loaded solutions.
   /// </summary>
   int WorkerCount { get; }

   /// <summary>
   /// Loads a solution, spawning a new worker process if needed.
   /// Sets the solution as active. Returns the instance and whether it was already loaded.
   /// </summary>
   Task<LoadSolutionResult> LoadSolutionAsync(string solutionPath, CancellationToken cancellationToken = default);

   /// <summary>
   /// Unloads a solution and terminates its worker process.
   /// If no path specified, unloads the active solution.
   /// </summary>
   Task UnloadSolutionAsync(string? solutionPath = null, CancellationToken cancellationToken = default);

   /// <summary>
   /// Unloads all solutions and terminates all worker processes.
   /// </summary>
   Task UnloadAllAsync();

   /// <summary>
   /// Sets the active solution. The solution must already be loaded.
   /// </summary>
   void SetActiveSolution(string solutionPath);

   /// <summary>
   /// Forwards a tool call to the active worker process and deserializes the result.
   /// </summary>
   Task<T?> CallToolAsync<T>(
       string toolName,
       Dictionary<string, object?> arguments,
       CancellationToken cancellationToken = default);

   /// <summary>
   /// Gets a specific worker by solution path, or null if not found.
   /// </summary>
   WorkerInstance? GetWorker(string solutionPath);

   /// <summary>
   /// Returns true if a solution is currently being loaded (spawn + workspace open in progress).
   /// </summary>
   bool IsLoading(string solutionPath);

   /// <summary>
   /// Gets the paths of all solutions currently being loaded.
   /// </summary>
   IReadOnlyCollection<string> LoadingPaths { get; }

   /// <summary>
   /// Gets a snapshot of solutions that failed to load (path → error message).
   /// Cleared when a new load is started for the same path.
   /// </summary>
   IReadOnlyDictionary<string, string> FailedLoads { get; }
}
