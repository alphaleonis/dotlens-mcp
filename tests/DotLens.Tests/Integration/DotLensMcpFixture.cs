using ModelContextProtocol.Client;

namespace DotLens.Tests.Integration;

/// <summary>
/// Provides a reusable fixture for E2E tests that need to communicate with a DotLens MCP server.
/// Uses the MCP SDK's StdioClientTransport for proper protocol handling.
/// </summary>
public sealed class DotLensMcpFixture : IAsyncDisposable
{
   private readonly StdioClientTransport _transport;

   /// <summary>
   /// Gets the connected MCP client.
   /// </summary>
   public McpClient Client { get; }

   private DotLensMcpFixture(StdioClientTransport transport, McpClient client)
   {
      _transport = transport;
      Client = client;
   }

   /// <summary>
   /// Creates a new fixture connected to a DotLens process.
   /// </summary>
   /// <param name="workerMode">If true, spawns in worker mode (--worker flag). Otherwise spawns in server mode.</param>
   /// <param name="cancellationToken">Cancellation token.</param>
   /// <returns>A connected fixture ready for use.</returns>
   public static async Task<DotLensMcpFixture> CreateAsync(
      bool workerMode = false,
      CancellationToken cancellationToken = default)
   {
      var executablePath = FindExecutablePath();

      if (!File.Exists(executablePath))
      {
         throw new SkipException($"DotLens executable not found at {executablePath}. Build the project first.");
      }

      var arguments = new List<string>();
      if (workerMode)
      {
         arguments.Add("--worker");
      }

      // Determine command and arguments based on whether it's .exe or .dll
      string command;
      if (executablePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
      {
         command = "dotnet";
         arguments.Insert(0, executablePath);
      }
      else
      {
         command = executablePath;
      }

      var options = new StdioClientTransportOptions
      {
         Command = command,
         Arguments = arguments.Count > 0 ? arguments : null,
         Name = workerMode ? "DotLens-Worker" : "DotLens-Server",
         ShutdownTimeout = TimeSpan.FromSeconds(5)
      };

      var transport = new StdioClientTransport(options);

      var clientOptions = new McpClientOptions
      {
         ClientInfo = new ModelContextProtocol.Protocol.Implementation
         {
            Name = "DotLens-E2E-Tests",
            Version = "1.0.0"
         }
      };

      var client = await McpClient.CreateAsync(transport, clientOptions, cancellationToken: cancellationToken);

      return new DotLensMcpFixture(transport, client);
   }

   /// <summary>
   /// Finds the DotLens executable path based on build configuration.
   /// </summary>
   private static string FindExecutablePath()
   {
      var projectDir = FindProjectDirectory();
      var config = "Debug";
#if RELEASE
      config = "Release";
#endif
      // Try .exe first (Windows), then .dll (cross-platform)
      var exePath = Path.Combine(projectDir, "src", "DotLens", "bin", config, "net10.0", "DotLens.exe");
      if (File.Exists(exePath))
      {
         return exePath;
      }

      return Path.Combine(projectDir, "src", "DotLens", "bin", config, "net10.0", "DotLens.dll");
   }

   /// <summary>
   /// Finds the solution root directory by searching for DotLens.slnx.
   /// </summary>
   private static string FindProjectDirectory()
   {
      var dir = Directory.GetCurrentDirectory();
      while (dir != null)
      {
         if (File.Exists(Path.Combine(dir, "DotLens.slnx")))
            return dir;
         dir = Directory.GetParent(dir)?.FullName;
      }
      throw new InvalidOperationException("Could not find project directory (looking for DotLens.slnx)");
   }

   /// <inheritdoc />
   public async ValueTask DisposeAsync()
   {
      await Client.DisposeAsync();
   }
}
