using System.Reflection;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Serilog;
using Serilog.Events;

namespace DotLens;

public static class Program
{
   public static async Task Main(string[] args)
   {
      var action = CommandLineHandler.Process(args);
      switch (action)
      {
         case StartupAction.Exit:
            return;
         case StartupAction.RunWorker:
            await RunWorker(args);
            return;
         case StartupAction.RunServer:
            await RunServer(args);
            return;
         default:
            throw new InvalidOperationException($"Unexpected startup action: {action}");
      }
   }

   internal static string GetVersion()
   {
      var assembly = Assembly.GetExecutingAssembly();
      var informationalVersion = assembly
         .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

      // InformationalVersion from GitVersion includes git hash (e.g., "1.2.3+abc1234")
      // Fall back to assembly version if not available
      return informationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown";
   }

   private static async Task RunServer(string[] args)
   {
      // MCP server mode: spawns worker process, forwards tool calls
      // Note: MSBuildLocator not registered here - worker handles that

      var builder = Host.CreateApplicationBuilder(args);

      // Add DOTLENS_ prefixed environment variables to configuration
      builder.Configuration.AddEnvironmentVariables(prefix: "DOTLENS_");

      // Configure Serilog from configuration (writes to stderr, stdout is for MCP protocol)
      builder.Services.AddSerilog(ConfigureSerilog);

      // Configure DotLensOptions from configuration
      builder.Services.AddDotLensOptions(builder.Configuration);

      // Register MultiWorkerManager as singleton (register both concrete and interface)
      builder.Services.AddSingleton<MultiWorkerManager>();
      builder.Services.AddSingleton<IMultiWorkerManager>(sp => sp.GetRequiredService<MultiWorkerManager>());

      // Register server tool services (classes with [DotLensServerTool])
      builder.Services.AddServerToolServices();

      builder.Services
          .AddMcpServer()
          .WithStdioServerTransport()
          .AddParameterValidation() // Exception formatting for server tools
                                    // Server-side tools - executed directly by SDK (classes with [DotLensServerTool])
          .AddServerTools()
          // Worker tools - forwarded to worker process (classes with [DotLensWorkerTool])
          .AddForwardedWorkerTools();

      var host = builder.Build();

      Log.Information("DotLens MCP Server v{Version}", GetVersion());

      // Start the MCP server immediately so clients can connect without waiting
      // for solution loading. Tools called before load completes return clean errors.
      await host.StartAsync();

      // Auto-discover and load solution in the background (non-blocking)
      var options = host.Services.GetRequiredService<IOptions<DotLensOptions>>().Value;
      var discovery = SolutionDiscovery.Discover(options.SolutionPath, options.AutoLoadSolution, Directory.GetCurrentDirectory());

      switch (discovery)
      {
         case SolutionDiscoveryResult.Load load:
            {
               var multiWorkerManager = host.Services.GetRequiredService<MultiWorkerManager>();
               Log.Information("Auto-loading solution in background: {Path}", load.SolutionPath);

               // Fire-and-forget: a load_solution call for the same path joins this load.
               // The core load has its own finite timeout (MaxLoadTimeoutSeconds).
               _ = Task.Run(async () =>
               {
                  try
                  {
                     var result = await multiWorkerManager.AutoLoadSolutionAsync(load.SolutionPath);
                     Log.Information("Solution auto-loaded successfully, active: {Active}", result.IsActive);
                  }
                  catch (TimeoutException ex)
                  {
                     Log.Error(ex, "Auto-load timed out: {Path}", load.SolutionPath);
                  }
                  catch (Exception ex)
                  {
                     Log.Error(ex, "Failed to auto-load solution: {Path}", load.SolutionPath);
                  }
               });
               break;
            }

         case SolutionDiscoveryResult.MultipleSolutions multiple:
            Log.Information("Multiple solutions found, call load_solution to select: {Solutions}",
                string.Join(", ", multiple.Solutions.Select(Path.GetFileName)));
            break;

         case SolutionDiscoveryResult.ConfiguredPathNotFound notFound:
            Log.Error("Configured SolutionPath not found: {Path}", notFound.Path);
            break;

         case SolutionDiscoveryResult.Disabled:
            Log.Debug("Solution auto-load disabled");
            break;

         case SolutionDiscoveryResult.None:
            Log.Debug("No solutions found in working directory");
            break;

         default:
            throw new InvalidOperationException($"Unexpected discovery result: {discovery}");
      }

      await host.WaitForShutdownAsync();
   }

   private static async Task RunWorker(string[] args)
   {
      // Worker mode: handles Roslyn analysis in a separate process
      // Note: As of Roslyn 4.9+, MSBuildWorkspace uses a separate BuildHost process
      // that handles MSBuild loading internally, so MSBuildLocator is not needed.

      var builder = Host.CreateApplicationBuilder(args);

      // Add DOTLENS_ prefixed environment variables to configuration
      builder.Configuration.AddEnvironmentVariables(prefix: "DOTLENS_");

      // Configure Serilog from configuration (writes to stderr, stdout is for MCP protocol)
      builder.Services.AddSerilog(ConfigureSerilog);

      // Configure DotLensOptions from configuration
      builder.Services.AddDotLensOptions(builder.Configuration);

      // Register WorkspaceManager as singleton (shared by all tools)
      // Register both interfaces explicitly so DI can resolve either
      builder.Services.AddSingleton<WorkspaceManager>();
      builder.Services.AddSingleton<IWorkspace>(sp => sp.GetRequiredService<WorkspaceManager>());
      builder.Services.AddSingleton<IReadOnlyWorkspace>(sp => sp.GetRequiredService<WorkspaceManager>());

      // Register SymbolLookupService (unified symbol lookup for all tools)
      builder.Services.AddSingleton<SymbolLookupService>();

      // Register all worker tool classes (classes with [DotLensWorkerTool])
      builder.Services.AddWorkerToolServices();

      builder.Services
          .AddMcpServer()
          .WithStdioServerTransport()
          .AddParameterValidation() // Automatic validation via attributes
                                    // Register all worker tools (classes with [DotLensWorkerTool])
          .AddWorkerTools();

      var host = builder.Build();
      Log.Information("DotLens Worker v{Version}", GetVersion());
      await host.RunAsync();
   }

   static void ConfigureSerilog(IServiceProvider services, LoggerConfiguration lc)
   {
      var options = services.GetRequiredService<IOptions<DotLensOptions>>();
      var logLevel = options.Value.LogLevel;

#pragma warning disable CA1305 // Serilog Console sink doesn't use IFormatProvider for log output
      lc.MinimumLevel.Is(logLevel)
          .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
          .MinimumLevel.Override("System", LogEventLevel.Information)
          .WriteTo.Console(
              standardErrorFromLevel: LogEventLevel.Verbose,
              outputTemplate: "{Timestamp:HH:mm:ss} {Level:u4}: {Message:lj}{NewLine}{Exception}");
#pragma warning restore CA1305
   }
}
