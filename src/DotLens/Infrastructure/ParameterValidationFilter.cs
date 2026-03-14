using System.Diagnostics;
using System.Reflection;
using System.Security;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DotLens.Infrastructure;

/// <summary>
/// Extension methods to add parameter validation to the MCP server pipeline.
/// </summary>
public static class ParameterValidationFilter
{
   /// <summary>
   /// Adds automatic parameter validation and exception formatting to the MCP server pipeline.
   /// </summary>
   public static IMcpServerBuilder AddParameterValidation(this IMcpServerBuilder builder)
   {
      return builder.WithRequestFilters(filters =>
      {
         filters.AddCallToolFilter(next => async (context, cancellationToken) =>
         {
            // Validate [PositiveInteger] constraints before invoking the tool
            if (context.MatchedPrimitive is McpServerTool tool)
            {
               var methodInfo = tool.Metadata.OfType<MethodInfo>().FirstOrDefault();
               if (methodInfo != null && context.Params?.Arguments is { } args)
               {
                  foreach (var param in methodInfo.GetParameters())
                  {
                     if (param.Name is not { } paramName)
                        continue;

                     // Validate [PositiveInteger] (>= 1)
                     if (param.GetCustomAttribute<PositiveIntegerAttribute>() != null
                         && args.TryGetValue(paramName, out var posVal))
                     {
                        if (!posVal.TryGetInt32(out var intVal))
                        {
                           return CreateErrorResult(ErrorCodes.InvalidParameter,
                              $"Parameter '{paramName}' must be an integer",
                              "Value must be a valid integer");
                        }
                        if (intVal < 1)
                        {
                           return CreateErrorResult(ErrorCodes.InvalidParameter,
                              $"Parameter '{paramName}' must be >= 1 (got {intVal})",
                              "Value must be a positive integer");
                        }
                     }

                     // Validate [NonNegative] (>= 0)
                     if (param.GetCustomAttribute<NonNegativeAttribute>() != null
                         && args.TryGetValue(paramName, out var nonNegVal))
                     {
                        if (!nonNegVal.TryGetInt32(out var intVal))
                        {
                           return CreateErrorResult(ErrorCodes.InvalidParameter,
                              $"Parameter '{paramName}' must be an integer",
                              "Value must be a valid integer");
                        }
                        if (intVal < 0)
                        {
                           return CreateErrorResult(ErrorCodes.InvalidParameter,
                              $"Parameter '{paramName}' must be >= 0 (got {intVal})",
                              "Value must be a non-negative integer");
                        }
                     }
                  }
               }
            }

            try
            {
               return await next(context, cancellationToken);
            }
            catch (ArgumentOutOfRangeException ex)
            {
               return CreateErrorResult(ErrorCodes.InvalidParameter, ex.Message,
                  "Check that parameter values are within valid bounds");
            }
            catch (ArgumentException ex)
            {
               return CreateErrorResult(ErrorCodes.InvalidParameter, ex.Message,
                  "Check parameter values are valid");
            }
            catch (SecurityException ex)
            {
               return CreateErrorResult("SECURITY_ERROR", ex.Message,
                  "File path may be outside the solution directory");
            }
            catch (ToolException ex)
            {
               return CreateErrorResult(ex.Code, ex.Message, ex.Hint, ex.Context);
            }
            catch (UnreachableException ex)
            {
               return CreateErrorResult(ErrorCodes.InternalError, ex.Message,
                  "This indicates a bug in DotLens. Please report this issue.");
            }
            catch (Exception ex)
            {
               // Catch-all for any unhandled exception type.
               // Include full exception details to aid debugging.
               return CreateErrorResult(
                  ErrorCodes.InternalError,
                  ex.ToString(),
                  "An unexpected error occurred. The full exception details are included above.");
            }
         });
      });
   }

   private static CallToolResult CreateErrorResult(string errorCode, string message, string? hint)
   {
      return new CallToolResult
      {
         IsError = true,
         Content = [new TextContentBlock { Text = FormatErrorResponse(errorCode, message, hint) }]
      };
   }

   private static CallToolResult CreateErrorResult(string errorCode, string message, string? hint, object? context)
   {
      return new CallToolResult
      {
         IsError = true,
         Content = [new TextContentBlock { Text = FormatErrorResponse(errorCode, message, hint, context) }]
      };
   }

   private static string FormatErrorResponse(string errorCode, string errorMessage, string? hint)
   {
      var response = new
      {
         success = false,
         error = new
         {
            code = errorCode,
            message = errorMessage,
            hint
         }
      };
      return JsonSerializer.Serialize(response);
   }

   private static string FormatErrorResponse(string errorCode, string errorMessage, string? hint, object? context)
   {
      var response = new
      {
         success = false,
         error = new
         {
            code = errorCode,
            message = errorMessage,
            hint,
            context
         }
      };
      return JsonSerializer.Serialize(response);
   }
}
