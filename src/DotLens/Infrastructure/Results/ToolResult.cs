using DotLens.Tools.Shared;

namespace DotLens.Infrastructure.Results;

/// <summary>
/// Base type for all tool responses. Tools can return this when
/// the specific result type is determined at runtime.
/// </summary>
public abstract record ToolResult
{
   public static ToolResult<T> Create<T>(T data, ResponseMetadata? metadata = null)
   {
      return new ToolResult<T> { Data = data, Metadata = metadata };
   }

   public static ToolResult<T> Create<T>(T data, string[]? suggestedNextTools,
      int? totalCount = null, int? returnedCount = null, ToolResultVerbosity? verbosity = null, string? warning = null)
   {
      var verbosityStr = verbosity?.ToString().ToLowerInvariant();
      var hasMetadata = suggestedNextTools != null || totalCount.HasValue || returnedCount.HasValue || verbosityStr != null || warning != null;

      return new ToolResult<T>
      {
         Data = data,
         Metadata = hasMetadata
            ? new ResponseMetadata
            {
               TotalCount = totalCount,
               ReturnedCount = returnedCount,
               Truncated = totalCount.HasValue && returnedCount.HasValue && returnedCount < totalCount,
               SuggestedNextTools = suggestedNextTools,
               Verbosity = verbosityStr,
               Warning = warning
            }
            : null
      };
   }
}

/// <summary>
/// Represents a tool response with typed data.
/// This is the standard return type for all MCP tools.
/// </summary>
/// <typeparam name="TData">The type of the response data.</typeparam>
public sealed record ToolResult<TData> : ToolResult
{
   /// <summary>
   /// The response data.
   /// </summary>
   public required TData Data { get; init; }

   /// <summary>
   /// Optional metadata for AI workflow guidance.
   /// </summary>
   public ResponseMetadata? Metadata { get; init; }
}

