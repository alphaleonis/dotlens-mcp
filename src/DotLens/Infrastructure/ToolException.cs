namespace DotLens.Infrastructure;

/// <summary>
/// Exception thrown by tools to indicate a failure that should be returned to the MCP client
/// with isError = true. This enables proper MCP protocol error signaling.
/// </summary>
public class ToolException : Exception
{
   /// <summary>
   /// The error code (use ErrorCodes constants).
   /// </summary>
   public string Code { get; }

   /// <summary>
   /// Optional recovery hint for AI agents.
   /// </summary>
   public string? Hint { get; }

   /// <summary>
   /// Optional additional context about the error.
   /// </summary>
   public object? Context { get; }

   /// <summary>
   /// Creates a new ToolException with error details.
   /// </summary>
   /// <param name="code">The error code (use ErrorCodes constants).</param>
   /// <param name="message">Human-readable error message.</param>
   /// <param name="hint">Optional recovery hint for AI agents.</param>
   /// <param name="context">Optional additional context about the error.</param>
   public ToolException(string code, string message, string? hint = null, object? context = null)
      : base(message)
   {
      Code = code;
      Hint = hint;
      Context = context;
   }
}
