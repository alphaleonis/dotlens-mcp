using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotLens.Infrastructure;

/// <summary>
/// Extension methods to register tools that forward to a worker process.
/// </summary>
public static class ForwardingMcpServerToolExtensions
{
   private const string RequiresUnreferencedCodeMessage =
      "The non-generic WithForwardedTools method requires dynamic lookup of method metadata " +
      "and might not work in Native AOT. Use the generic WithForwardedTools<T> method instead.";

   /// <summary>
   /// Registers tools from TToolType but forwards all calls to the worker process.
   /// This allows tools to appear in the server's tool list but be executed by the worker.
   ///
   /// The tool metadata (name, description, schema) is extracted from TToolType's methods
   /// using the SDK's built-in schema generation (via McpServerTool.Create),
   /// but when invoked, calls are forwarded to the worker instead of executing locally.
   /// </summary>
   public static IMcpServerBuilder WithForwardedTools<[DynamicallyAccessedMembers(
      DynamicallyAccessedMemberTypes.PublicMethods |
      DynamicallyAccessedMemberTypes.NonPublicMethods |
      DynamicallyAccessedMemberTypes.PublicConstructors)] TToolType>(
      this IMcpServerBuilder builder,
      JsonSerializerOptions? serializerOptions = null)
   {
      return RegisterForwardedToolsFromType(builder, typeof(TToolType), serializerOptions);
   }

   /// <summary>
   /// Registers tools from the specified types but forwards all calls to the worker process.
   /// </summary>
   [RequiresUnreferencedCode(RequiresUnreferencedCodeMessage)]
   public static IMcpServerBuilder WithForwardedTools(
      this IMcpServerBuilder builder,
      IEnumerable<Type> toolTypes,
      JsonSerializerOptions? serializerOptions = null)
   {
      foreach (var toolType in toolTypes)
      {
         if (toolType is not null)
         {
            RegisterForwardedToolsFromType(builder, toolType, serializerOptions);
         }
      }
      return builder;
   }

   private static IMcpServerBuilder RegisterForwardedToolsFromType(
      IMcpServerBuilder builder,
      Type toolType,
      JsonSerializerOptions? serializerOptions)
   {
      foreach (var method in toolType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
      {
         var toolAttr = method.GetCustomAttribute<McpServerToolAttribute>();
         if (toolAttr == null)
            continue;

         // Use SDK's McpServerTool.Create to get proper schema generation
         // This handles all the complex cases: nullability, arrays, descriptions, etc.
         McpServerTool templateTool;
         if (method.IsStatic)
         {
            templateTool = McpServerTool.Create(method, options: new()
            {
               SerializerOptions = serializerOptions
            });
         }
         else
         {
            // For instance methods, use the factory overload with a dummy creator
            // We only need the schema, never actually invoke this tool
            templateTool = McpServerTool.Create(
               method,
               createTargetFunc: _ => throw new UnreachableException("This should never be called"),
               options: new() { SerializerOptions = serializerOptions });
         }

         var toolName = templateTool.ProtocolTool.Name;

         // Register a forwarding tool that wraps the SDK-generated Tool definition
         // Capture metadata for potential use by filters or other middleware
         var metadata = templateTool.Metadata;
         builder.Services.AddSingleton<McpServerTool>(services =>
         {
            return new ForwardingMcpServerTool(templateTool.ProtocolTool, toolName, metadata, services);
         });
      }

      return builder;
   }
}

/// <summary>
/// An McpServerTool implementation that forwards invocations to a worker process.
/// </summary>
internal sealed class ForwardingMcpServerTool : McpServerTool
{
   private readonly Tool _tool;
   private readonly string _toolName;
   private readonly IReadOnlyList<object> _metadata;
   private readonly IServiceProvider _services;

   public ForwardingMcpServerTool(Tool tool, string toolName, IReadOnlyList<object> metadata, IServiceProvider services)
   {
      _tool = tool;
      _toolName = toolName;
      _metadata = metadata;
      _services = services;
   }

   public override Tool ProtocolTool => _tool;

   public override IReadOnlyList<object> Metadata => _metadata;

   public override async ValueTask<CallToolResult> InvokeAsync(
       RequestContext<CallToolRequestParams> request,
       CancellationToken cancellationToken = default)
   {
      var multiWorkerManager = _services.GetRequiredService<MultiWorkerManager>();

      // Get timeout from configuration
      var options = _services.GetRequiredService<IOptions<DotLensOptions>>().Value;
      var timeoutSeconds = options.TimeoutSeconds;

      // Ensure there's an active worker
      if (!multiWorkerManager.HasActiveWorker)
      {
         if (multiWorkerManager.LoadingPaths.Count > 0)
         {
            return CreateErrorResult(
               ErrorCodes.SolutionLoading,
               "A solution is loading in the background. Please wait a moment and retry.");
         }

         return CreateErrorResult(
            ErrorCodes.NoSolutionLoaded,
            "No solution loaded. Call load_solution first.");
      }

      using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

      try
      {
         // Convert arguments from JsonElement to object
         var arguments = new Dictionary<string, object?>();
         if (request.Params?.Arguments != null)
         {
            foreach (var kvp in request.Params.Arguments)
            {
               arguments[kvp.Key] = ConvertJsonElement(kvp.Value);
            }
         }

         // Forward to active worker
         var client = multiWorkerManager.GetActiveClient();
         return await client.CallToolAsync(_toolName, arguments, cancellationToken: cts.Token);
      }
      catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
      {
         return CreateErrorResult(ErrorCodes.Timeout, $"Operation timed out after {timeoutSeconds}s");
      }
      catch (Exception ex)
      {
         return CreateErrorResult(ErrorCodes.WorkerCommunicationFailed, $"Worker error: {ex.Message}");
      }
   }

   private static CallToolResult CreateErrorResult(string code, string message)
   {
      return new CallToolResult
      {
         Content = [new TextContentBlock { Text = System.Text.Json.JsonSerializer.Serialize(new
            {
                success = false,
                error = new
                {
                    code,
                    message
                }
            })}],
         IsError = true
      };
   }

   private static object? ConvertJsonElement(JsonElement element)
   {
      return element.ValueKind switch
      {
         JsonValueKind.String => element.GetString(),
         JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
         JsonValueKind.True => true,
         JsonValueKind.False => false,
         JsonValueKind.Null => null,
         JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToArray(),
         JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
         _ => element.ToString()
      };
   }
}
