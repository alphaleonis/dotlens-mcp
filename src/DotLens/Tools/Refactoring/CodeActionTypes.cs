using System.ComponentModel;
using System.Text.Json.Serialization;
using DotLens.Infrastructure;

namespace DotLens.Tools.Refactoring;

#region Target Types (where to find code actions)

/// <summary>
/// Polymorphic base type for code action target specification.
/// Use "mode" discriminator to specify lookup strategy.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "mode")]
[JsonDerivedType(typeof(PositionTarget), "position")]
[JsonDerivedType(typeof(DiagnosticTarget), "diagnostic")]
public abstract class CodeActionTarget { }

/// <summary>
/// Target code actions at a source position (for refactorings).
/// </summary>
[Description("Target code actions at a source position. Use for discovering refactorings.")]
public class PositionTarget : CodeActionTarget
{
   [Description("Path to source file (absolute or relative to solution directory)")]
   public required string FilePath { get; set; }

   [Description("Line number (1-based)")]
   [PositiveInteger]
   public required int Line { get; set; }

   [Description("Column number (1-based)")]
   [PositiveInteger]
   public required int Column { get; set; }

   [Description("End line for selection range (optional). Must be provided together with EndColumn.")]
   [PositiveInteger]
   public int? EndLine { get; set; }

   [Description("End column for selection range (optional). Must be provided together with EndLine.")]
   [PositiveInteger]
   public int? EndColumn { get; set; }
}

/// <summary>
/// Target code actions for a specific diagnostic (for code fixes).
/// </summary>
[Description("Target code actions for a specific diagnostic. Use for discovering code fixes.")]
public class DiagnosticTarget : CodeActionTarget
{
   [Description("Path to source file (absolute or relative to solution directory)")]
   public required string FilePath { get; set; }

   [Description("Line number (1-based)")]
   [PositiveInteger]
   public required int Line { get; set; }

   [Description("Column number (1-based)")]
   [PositiveInteger]
   public required int Column { get; set; }

   [Description("Diagnostic ID (e.g., CS0168, CS0246, CA2007)")]
   public required string DiagnosticId { get; set; }
}

#endregion

#region Selection Types (which action to apply)

/// <summary>
/// Polymorphic base type for code action selection.
/// Use "mode" discriminator to specify how to identify the action.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "mode")]
[JsonDerivedType(typeof(TitleSelection), "title")]
[JsonDerivedType(typeof(KeySelection), "key")]
public abstract class CodeActionSelection { }

/// <summary>
/// Select a code action by its title. Works for both refactorings and fixes.
/// </summary>
[Description("Select a code action by its title. Supports exact and partial matching.")]
public class TitleSelection : CodeActionSelection
{
   [Description("Action title to match (exact match tried first, then partial)")]
   public required string Title { get; set; }
}

/// <summary>
/// Select a code action by its stable key. Only works for diagnostic targets.
/// </summary>
[Description("Select a code action by its stable key. Only works with diagnostic targets (code fixes).")]
public class KeySelection : CodeActionSelection
{
   [Description("Stable key from get_code_actions response (8-char hex)")]
   public required string Key { get; set; }
}

#endregion

#region Enums

/// <summary>
/// The kind of code action available.
/// </summary>
public enum CodeActionKind
{
   /// <summary>A code fix for a diagnostic (compiler error/warning).</summary>
   Fix,

   /// <summary>A refactoring that transforms code structure.</summary>
   Refactoring
}

#endregion

#region Response Types

/// <summary>
/// Information about an available code action (unified for both fixes and refactorings).
/// </summary>
public record CodeActionItemInfo
{
   [Description("Index in the results list")]
   public required int Index { get; init; }

   [Description("Human-readable action title")]
   public required string Title { get; init; }

   [Description("Whether this is a code fix or refactoring")]
   public required CodeActionKind Kind { get; init; }

   [Description("Stable key for applying (only for code fixes from diagnostic targets)")]
   public string? Key { get; init; }

   [Description("Roslyn equivalence key for grouping related actions")]
   public string? EquivalenceKey { get; init; }

   [Description("Number of duplicate actions with same title (grouped)")]
   public required int Count { get; init; }
}

/// <summary>
/// Response from get_code_actions tool.
/// </summary>
public record GetCodeActionsResponse
{
   [Description("Available code actions")]
   public required IReadOnlyList<CodeActionItemInfo> Actions { get; init; }

   [Description("Status message when no actions available")]
   public string? Message { get; init; }

   [Description("Number of code fix actions")]
   public required int FixCount { get; init; }

   [Description("Number of refactoring actions")]
   public required int RefactoringCount { get; init; }

   [Description("Diagnostic info (when using diagnostic target)")]
   public DiagnosticInfo? Diagnostic { get; init; }
}

/// <summary>
/// Diagnostic information for diagnostic target responses.
/// </summary>
public record DiagnosticInfo
{
   [Description("Diagnostic code (e.g., CS0168)")]
   public required string Id { get; init; }

   [Description("Human-readable diagnostic message")]
   public required string Message { get; init; }

   [Description("Severity level")]
   public required string Severity { get; init; }
}

/// <summary>
/// Response from apply_code_action and implement_missing_members tools.
/// </summary>
public record ApplyCodeActionResponse
{
   [Description("True if changes were applied to disk")]
   public required bool Applied { get; init; }

   [Description("Title of the applied action")]
   public required string ActionTitle { get; init; }

   [Description("Kind of action that was applied (for apply_code_action)")]
   public CodeActionKind? ActionKind { get; init; }

   [Description("Files modified by this action")]
   public required IReadOnlyList<ChangedFileInfo> ChangedFiles { get; init; }

   [Description("True if this was a preview (changes not saved)")]
   public required bool Preview { get; init; }

   [Description("Key of the applied action (for code fixes)")]
   public string? ActionKey { get; init; }

   [Description("Diagnostic ID that was fixed (for code fixes)")]
   public string? DiagnosticId { get; init; }
}

#endregion
