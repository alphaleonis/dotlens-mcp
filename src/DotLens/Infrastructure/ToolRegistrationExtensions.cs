using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Server;

namespace DotLens.Infrastructure;

/// <summary>
/// Extension methods for automatic tool registration based on custom attributes.
/// </summary>
public static class ToolRegistrationExtensions
{
   private const string RequiresUnreferencedCodeMessage =
      "Tool registration requires dynamic lookup of method metadata and might not work in Native AOT.";

   // Cache reflection results for better startup performance
   private static readonly Lazy<Type[]> _workerToolTypes = new(() =>
      Assembly.GetExecutingAssembly()
         .GetTypes()
         .Where(t => t.IsClass && !t.IsAbstract && t.GetCustomAttribute<DotLensWorkerToolAttribute>() != null)
         .ToArray());

   private static readonly Lazy<Type[]> _serverToolTypes = new(() =>
      Assembly.GetExecutingAssembly()
         .GetTypes()
         .Where(t => t.IsClass && !t.IsAbstract && t.GetCustomAttribute<DotLensServerToolAttribute>() != null)
         .ToArray());

   /// <summary>
   /// Registers all worker tools in the DI container as singletons.
   /// Call this before AddWorkerTools to ensure tools can be resolved.
   /// </summary>
   public static IServiceCollection AddWorkerToolServices(this IServiceCollection services)
   {
      foreach (var toolType in GetWorkerToolTypes())
      {
         services.AddSingleton(toolType);
      }
      return services;
   }

   /// <summary>
   /// Registers all classes marked with [DotLensWorkerTool] for worker mode.
   /// These tools execute directly in the worker process with access to MSBuildWorkspace.
   /// </summary>
   /// <remarks>
   /// We register tools manually rather than using the SDK's WithTools() method because
   /// WithTools(IEnumerable&lt;Type&gt;) doesn't reliably register tools to the DI container.
   /// </remarks>
   [RequiresUnreferencedCode(RequiresUnreferencedCodeMessage)]
   public static IMcpServerBuilder AddWorkerTools(this IMcpServerBuilder builder)
   {
      return RegisterToolsFromTypes(builder, GetWorkerToolTypes());
   }

   /// <summary>
   /// Forwards all public classes marked with [DotLensWorkerTool] to the worker process.
   /// Use this in server mode to expose worker tools while forwarding execution.
   /// Internal tools (marked with Internal = true) are excluded from forwarding.
   /// </summary>
   [RequiresUnreferencedCode(RequiresUnreferencedCodeMessage)]
   public static IMcpServerBuilder AddForwardedWorkerTools(this IMcpServerBuilder builder)
   {
      var publicWorkerTools = GetWorkerToolTypes()
         .Where(t => t.GetCustomAttribute<DotLensWorkerToolAttribute>()?.Internal != true);
      return builder.WithForwardedTools(publicWorkerTools, DotLensJsonOptions.Default);
   }

   /// <summary>
   /// Registers all classes marked with [DotLensServerTool] for server mode.
   /// These tools execute directly in the server process.
   /// </summary>
   public static IServiceCollection AddServerToolServices(this IServiceCollection services)
   {
      foreach (var toolType in GetServerToolTypes())
      {
         services.AddSingleton(toolType);
      }
      return services;
   }

   /// <summary>
   /// Registers all classes marked with [DotLensServerTool] with the MCP server.
   /// </summary>
   /// <remarks>
   /// We register tools manually rather than using the SDK's WithTools() method because
   /// WithTools(IEnumerable&lt;Type&gt;) doesn't reliably register tools to the DI container.
   /// </remarks>
   [RequiresUnreferencedCode(RequiresUnreferencedCodeMessage)]
   public static IMcpServerBuilder AddServerTools(this IMcpServerBuilder builder)
   {
      return RegisterToolsFromTypes(builder, GetServerToolTypes());
   }

   /// <summary>
   /// Schema options that add "minimum" constraints for validation attributes.
   /// </summary>
   /// <remarks>
   /// Note: This only works for properties on classes/records (e.g., SymbolLookup.Line),
   /// not for method parameters. Method parameters don't have property context in the transform.
   /// For method parameters, the runtime validation in ParameterValidationFilter handles enforcement.
   /// </remarks>
   internal static readonly AIJsonSchemaCreateOptions SchemaCreateOptions = new()
   {
      TransformSchemaNode = (context, node) =>
      {
         if (context.TypeInfo.Type == typeof(int) && node is JsonObject jObj)
         {
            // [PositiveInteger] => minimum: 1
            if (context.GetCustomAttribute<PositiveIntegerAttribute>() != null)
            {
               jObj["minimum"] = 1;
            }
            // [NonNegative] => minimum: 0
            else if (context.GetCustomAttribute<NonNegativeAttribute>() != null)
            {
               jObj["minimum"] = 0;
            }
         }
         return node;
      }
   };

   /// <summary>
   /// Registers MCP tools from the given types. This is a workaround for the SDK's
   /// WithTools(IEnumerable&lt;Type&gt;) method which doesn't reliably register tools
   /// due to C# overload resolution picking the wrong AddSingleton overload.
   /// </summary>
   [RequiresUnreferencedCode(RequiresUnreferencedCodeMessage)]
   private static IMcpServerBuilder RegisterToolsFromTypes(IMcpServerBuilder builder, IEnumerable<Type> toolTypes)
   {
      foreach (var toolType in toolTypes)
      {
         var methods = toolType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
         foreach (var toolMethod in methods)
         {
            var attr = toolMethod.GetCustomAttribute<McpServerToolAttribute>();
            if (attr is null)
               continue;

            if (toolMethod.IsStatic)
            {
               builder.Services.AddSingleton<McpServerTool>(services =>
                  McpServerTool.Create(toolMethod, options: new()
                  {
                     Services = services,
                     SerializerOptions = DotLensJsonOptions.Default,
                     SchemaCreateOptions = SchemaCreateOptions
                  }));
            }
            else
            {
               builder.Services.AddSingleton<McpServerTool>(services =>
                  McpServerTool.Create(toolMethod, r => ActivatorUtilities.CreateInstance(r.Services!, toolType),
                     new()
                     {
                        Services = services,
                        SerializerOptions = DotLensJsonOptions.Default,
                        SchemaCreateOptions = SchemaCreateOptions
                     }));
            }
         }
      }

      return builder;
   }

   private static Type[] GetWorkerToolTypes() => _workerToolTypes.Value;

   private static Type[] GetServerToolTypes() => _serverToolTypes.Value;
}
