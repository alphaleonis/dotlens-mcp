using System.ComponentModel;

namespace DotLens.Tools.Refactoring;

/// <summary>
/// Represents a file changed by a refactoring operation.
/// Shared by multiple refactoring tools.
/// </summary>
public record ChangedFileInfo
{
   [Description("Path to the changed file")]
   public required string FilePath { get; init; }

   [Description("Number of text changes in this file")]
   public required int ChangeCount { get; init; }

   [Description("New file content (only in preview mode)")]
   public string? NewText { get; init; }
}
