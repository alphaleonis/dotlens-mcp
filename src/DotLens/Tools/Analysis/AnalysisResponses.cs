using System.ComponentModel;

namespace DotLens.Tools.Analysis;

/// <summary>
/// Diagnostic severity levels matching Roslyn's DiagnosticSeverity.
/// Shared by multiple analysis tools.
/// </summary>
public enum DiagnosticSeverityDto
{
   Hidden,
   Info,
   Warning,
   Error
}

public static class DiagnosticSeverityExtensions
{
   public static DiagnosticSeverityDto ToDtoSeverity(this DiagnosticSeverity severity) => severity switch
   {
      DiagnosticSeverity.Hidden => DiagnosticSeverityDto.Hidden,
      DiagnosticSeverity.Info => DiagnosticSeverityDto.Info,
      DiagnosticSeverity.Warning => DiagnosticSeverityDto.Warning,
      DiagnosticSeverity.Error => DiagnosticSeverityDto.Error,
      _ => DiagnosticSeverityDto.Warning
   };
}

/// <summary>
/// A changed file from a code fix.
/// Shared by ApplyCodeFixTool and FixAllTool.
/// </summary>
public record CodeFixChangedFile
{
   [Description("File path (absolute or relative based on workspace settings).")]
   public required string FilePath { get; init; }

   [Description("File name without path.")]
   public required string FileName { get; init; }

   [Description("Number of text changes in this file.")]
   public required int ChangeCount { get; init; }

   [Description("Full file content after fix. Only populated in preview mode.")]
   public string? NewText { get; init; }

   [Description("Type of change: Modified, Added, or Removed.")]
   public required string ChangeType { get; init; }
}

/// <summary>
/// Represents a line range in source code.
/// Shared by AnalyzeDataFlowTool and AnalyzeControlFlowTool.
/// </summary>
public record LineRange
{
   [Description("Start line number.")]
   public required int StartLine { get; init; }

   [Description("End line number (inclusive).")]
   public required int EndLine { get; init; }
}
