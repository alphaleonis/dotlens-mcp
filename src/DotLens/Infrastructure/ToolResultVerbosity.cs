namespace DotLens.Infrastructure;

/// <summary>
/// Controls the level of detail in tool output.
/// </summary>
public enum ToolResultVerbosity
{
   /// <summary>Minimal output - counts and essential identifiers only.</summary>
   Summary,

   /// <summary>Standard output - names, types, and key properties. Default for most tools.</summary>
   Compact,

   /// <summary>Maximum detail - includes documentation, source code, and all metadata.</summary>
   Full
}
