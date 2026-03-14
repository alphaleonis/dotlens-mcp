using System.Reflection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotLens.Infrastructure;

/// <summary>
/// Configures MCP server options based on DotLens settings.
/// </summary>
public class ConfigureMcpServerOptions : IConfigureOptions<McpServerOptions>
{
   private readonly DotLensOptions _options;

   public ConfigureMcpServerOptions(IOptions<DotLensOptions> options)
   {
      _options = options.Value;
   }

   public void Configure(McpServerOptions options)
   {
      // Set server identity
      var version = Assembly.GetExecutingAssembly()
         .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
         ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
         ?? "1.0.0";

      options.ServerInfo = new Implementation
      {
         Name = "DotLens",
         Version = version
      };

      // Set instructions based on configured level
      options.ServerInstructions = ServerInstructions.GetInstructions(_options.InstructionLevel);
   }
}
