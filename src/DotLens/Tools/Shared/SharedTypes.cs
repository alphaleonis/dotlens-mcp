using System.ComponentModel;

namespace DotLens.Tools.Shared;

/// <summary>
/// Represents a span of source code with start and end positions.
/// Use this for ranges (e.g., method definitions). Use SymbolLocation for single points.
/// </summary>
public readonly record struct SourceSpan
{
   [Description("Absolute or relative path.")]
   public required string FilePath { get; init; }

   public required int StartLine { get; init; }
   public required int StartColumn { get; init; }
   public required int EndLine { get; init; }
   public required int EndColumn { get; init; }
}

/// <summary>
/// Simple location info (file + line + column).
/// </summary>
public record SymbolLocation
{
   [Description("Absolute or relative path.")]
   public required string FilePath { get; init; }

   public required int Line { get; init; }
   public required int Column { get; init; }
}

/// <summary>
/// Information about a reference to a symbol.
/// </summary>
public record ReferenceInfo
{
   public required string FilePath { get; init; }
   public required int Line { get; init; }
   public required int Column { get; init; }
   public string? LineText { get; init; }
   public required ReferenceKind Kind { get; init; }
}

/// <summary>
/// Method parameter info with full details.
/// </summary>
public record MethodParameterInfo
{
   public required string Name { get; init; }
   public required string Type { get; init; }
   public ParameterModifiers? Modifiers { get; init; }
   public string? DefaultValue { get; init; }
}

/// <summary>
/// Simple parameter info (for overloads list).
/// </summary>
public record ParameterInfo
{
   public required string Name { get; init; }
   public required string Type { get; init; }
}

/// <summary>
/// Caller location with line text.
/// </summary>
public record CallerLocationInfo
{
   public required string FilePath { get; init; }
   public required int Line { get; init; }
   public required int Column { get; init; }
   public string? LineText { get; init; }
}

/// <summary>
/// Generic wrapper for paginated responses.
/// Does NOT include Query/Offset (caller knows these).
/// TotalCount is conveyed via ResponseMetadata for AI workflow guidance.
/// </summary>
public record PagedResponse<T>
{
   public required IReadOnlyList<T> Items { get; init; }
   public required bool HasMore { get; init; }
   public int? NextOffset { get; init; }
}

/// <summary>
/// Result type for batch operations that process multiple items.
/// </summary>
/// <typeparam name="TItem">The type of successfully processed items.</typeparam>
/// <typeparam name="TRequest">The type of the original request for error tracking.</typeparam>
public record BatchResult<TItem, TRequest>
{
   public required int Requested { get; init; }
   public required int Processed { get; init; }
   public required int Succeeded { get; init; }
   public required int Failed { get; init; }
   public required IReadOnlyList<TItem> Results { get; init; }
   public required IReadOnlyList<BatchError<TRequest>> Errors { get; init; }
}

/// <summary>
/// Information about a failed item in a batch operation.
/// </summary>
/// <typeparam name="TRequest">The type of the original request.</typeparam>
public record BatchError<TRequest>
{
   public required TRequest Request { get; init; }
   public required string Error { get; init; }
}
