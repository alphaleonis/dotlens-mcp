using System.Text.Json.Serialization;

namespace DotLens.Tools.Worker;

/// <summary>
/// Common interface for worker health check results.
/// </summary>
[JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
[JsonDerivedType(typeof(WorkerHealthCheckReadyResult), "ready")]
[JsonDerivedType(typeof(WorkerHealthCheckNotReadyResult), "notReady")]
public interface IWorkerHealthCheckResult
{
   string Status { get; }
   string Message { get; }
}

#region Load Solution

/// <summary>
/// Result from worker loading a solution.
/// </summary>
public record WorkerLoadSolutionResult
{
   public required string SolutionPath { get; init; }
   public required int ProjectCount { get; init; }
   public required int DocumentCount { get; init; }

   /// <summary>
   /// The messages of the problems that the load found, as <see cref="Infrastructure.LoadWarning"/> describes them.
   /// </summary>
   public required IReadOnlyList<string> Warnings { get; init; }
}

#endregion

#region Health Check

/// <summary>
/// Workspace info in health check.
/// </summary>
public record WorkspaceInfo
{
   public required bool Indexed { get; init; }
   public required int CacheSize { get; init; }
}

/// <summary>
/// Solution info in health check.
/// </summary>
public record WorkerSolutionInfo
{
   public required bool Loaded { get; init; }
   public string? Path { get; init; }
   public required int Projects { get; init; }
   public required int Documents { get; init; }
   public string? LoadedAt { get; init; }
   public required int Errors { get; init; }
   public required int Warnings { get; init; }

   /// <summary>
   /// The messages of the problems that the load found, as <see cref="Infrastructure.LoadWarning"/> describes them.
   /// </summary>
   public required IReadOnlyList<string> LoadWarnings { get; init; }
}

/// <summary>
/// Capabilities info in health check.
/// </summary>
public record CapabilitiesInfo
{
   public required bool FindReferences { get; init; }
   public required bool FindImplementations { get; init; }
   public required bool CodeFixProvider { get; init; }
   public required bool SymbolSearch { get; init; }
   public required bool Diagnostics { get; init; }
}

/// <summary>
/// Configuration info in health check.
/// </summary>
public record ConfigurationInfo
{
   public required int MaxDiagnostics { get; init; }
   public required int TimeoutSeconds { get; init; }
   public required bool SemanticCacheEnabled { get; init; }
}

/// <summary>
/// Result from worker health check when ready.
/// </summary>
public record WorkerHealthCheckReadyResult : IWorkerHealthCheckResult
{
   public string Status => "Ready";
   public required string Message { get; init; }
   public required WorkerSolutionInfo Solution { get; init; }
   public required WorkspaceInfo Workspace { get; init; }
   public required CapabilitiesInfo Capabilities { get; init; }
   public required ConfigurationInfo Configuration { get; init; }
}

/// <summary>
/// Result from worker health check when not ready.
/// </summary>
public record WorkerHealthCheckNotReadyResult : IWorkerHealthCheckResult
{
   public string Status => "Not Ready";
   public required string Message { get; init; }
   public object? Solution { get; init; }
   public object? Workspace { get; init; }
}

#endregion
