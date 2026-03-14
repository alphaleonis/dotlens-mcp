using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Serilog.Events;

namespace DotLens.Infrastructure;

/// <summary>
/// Configuration options for DotLens MCP server.
/// Configure via:
/// - Environment variables with DOTLENS_ prefix: DOTLENS_MaxCacheSize=1000
/// - Command-line arguments: --MaxCacheSize=1000
/// - appsettings.json (if present)
/// </summary>
public class DotLensOptions
{
   /// <summary>
   /// The level of server instructions to provide to AI clients.
   /// Default is Detailed.
   /// </summary>
   [Description("Server instructions level (None, Brief, Detailed)")]
   public InstructionLevel InstructionLevel { get; set; } = InstructionLevel.Detailed;

   /// <summary>
   /// Log level for the application.
   /// Default: Warning. Valid values: Trace, Debug, Information, Warning, Error.
   /// </summary>
   [Description("Log level (Trace, Debug, Information, Warning, Error)")]
   public LogEventLevel LogLevel { get; set; } = LogEventLevel.Warning;

   /// <summary>
   /// Path to solution file or directory to auto-load on startup (server mode only).
   /// If a file path: loads that specific solution.
   /// If a directory: searches that directory for solutions.
   /// Relative paths are resolved relative to the current working directory.
   /// </summary>
   [Description("Solution path (.sln/.slnx file) or directory to search")]
   public string? SolutionPath { get; set; }

   /// <summary>
   /// Whether to auto-detect and load a solution from the current working directory.
   /// Only applies when SolutionPath is not set. Loads if exactly one .sln/.slnx exists.
   /// Default: true
   /// </summary>
   [Description("Auto-detect and load solution from working directory")]
   public bool AutoLoadSolution { get; set; } = true;

   /// <summary>
   /// Maximum number of diagnostics to return from analysis operations.
   /// Default: 100. Valid range: 1-10000.
   /// </summary>
   [Description("Max diagnostics to return (1-10000)")]
   [Range(1, 10000)]
   public int MaxDiagnostics { get; set; } = 100;

   /// <summary>
   /// Timeout in seconds for long-running operations.
   /// Default: 30. Valid range: 1-600.
   /// </summary>
   [Description("Timeout in seconds for operations (1-600)")]
   [Range(1, 600)]
   public int TimeoutSeconds { get; set; } = 30;

   /// <summary>
   /// Whether to use absolute paths in responses instead of solution-relative paths.
   /// Default: false
   /// </summary>
   [Description("Use absolute paths instead of solution-relative")]
   public bool UseAbsolutePaths { get; set; } = false;

   /// <summary>
   /// Maximum number of documents to cache.
   /// When limit is reached, least recently used documents are evicted.
   /// Default: 500. Valid range: 1-10000.
   /// </summary>
   [Description("Max documents to cache (1-10000)")]
   [Range(1, 10000)]
   public int MaxCacheSize { get; set; } = 500;

   /// <summary>
   /// Sliding expiration for cached documents in minutes.
   /// Documents not accessed within this period are evicted.
   /// Default: 30. Valid range: 1-1440 (24 hours).
   /// </summary>
   [Description("Cache expiration in minutes (1-1440)")]
   [Range(1, 1440)]
   public int CacheExpirationMinutes { get; set; } = 30;

   /// <summary>
   /// Whether to enable semantic caching of documents.
   /// Set to false to disable caching entirely.
   /// Default: true
   /// </summary>
   [Description("Enable semantic document caching")]
   public bool EnableSemanticCache { get; set; } = true;

   /// <summary>
   /// Timeout in seconds for waiting on an in-progress solution load.
   /// When load_solution is called while a background load is already in progress,
   /// it waits this long before returning a "loading" status.
   /// Default: 5. Valid range: 1-60.
   /// </summary>
   [Description("Seconds to wait for in-progress load before returning 'loading' status (1-60)")]
   [Range(1, 60)]
   public int LoadWaitTimeoutSeconds { get; set; } = 5;

   /// <summary>
   /// Maximum timeout in seconds for the entire solution load operation
   /// (spawning worker + loading MSBuildWorkspace). If the load exceeds this,
   /// the worker is cancelled and disposed to prevent leaked processes.
   /// Default: 300 (5 minutes). Valid range: 30-1800 (30 seconds to 30 minutes).
   /// </summary>
   [Description("Max seconds for solution load before timeout (30-1800)")]
   [Range(30, 1800)]
   public int MaxLoadTimeoutSeconds { get; set; } = 300;

   /// <summary>
   /// Maximum number of symbols to collect when sorting search results.
   /// When exceeded, partial results are returned with a warning.
   /// Only applies when sortBy is Name or Kind (which require collecting all results).
   /// Default: 10000. Valid range: 100-100000.
   /// </summary>
   [Description("Max symbols for sorted results (100-100000)")]
   [Range(100, 100000)]
   public int MaxSortableResults { get; set; } = 10000;
}

/// <summary>
/// Extension methods for registering DotLens options.
/// </summary>
public static class DotLensOptionsExtensions
{
   /// <summary>
   /// Adds DotLensOptions to the service collection, binding from configuration.
   /// Expects DOTLENS_ prefixed environment variables (e.g., DOTLENS_MaxCacheSize=1000).
   /// Validates options using data annotations.
   /// Also registers the MCP server options configurator.
   /// </summary>
   public static IServiceCollection AddDotLensOptions(this IServiceCollection services, IConfiguration configuration)
   {
      services.AddOptions<DotLensOptions>()
          .Bind(configuration)
          .ValidateDataAnnotations()
          .ValidateOnStart();

      // Register the MCP server options configurator
      services.AddSingleton<IConfigureOptions<McpServerOptions>, ConfigureMcpServerOptions>();

      return services;
   }
}
