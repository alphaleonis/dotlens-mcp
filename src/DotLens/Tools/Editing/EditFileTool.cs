using System.ComponentModel;
using DotLens.Infrastructure;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Editing;

#region Parameter/Response Types

public record EditOperation
{
   [Description("Text to find (must appear exactly once in the file).")]
   public required string Find { get; init; }

   [Description("Replacement text.")]
   public required string Replace { get; init; }
}

public record EditFileResponse
{
   [Description("File path that was edited.")]
   public required string FilePath { get; init; }

   [Description("Number of edits applied.")]
   public required int EditsApplied { get; init; }

   [Description("True if changes were applied to disk and workspace.")]
   public required bool Applied { get; init; }

   [Description("Number of compiler errors after editing.")]
   public required int ErrorCount { get; init; }

   [Description("Number of compiler warnings after editing.")]
   public required int WarningCount { get; init; }

   [Description("Compiler diagnostics for the edited file.")]
   public required IReadOnlyList<DiagnosticEntry> Diagnostics { get; init; }
}

#endregion

[McpServerToolType]
[DotLensWorkerTool]
public class EditFileTool
{
   private static readonly string[] CleanHints = ["No compiler errors - edits applied successfully"];
   private static readonly string[] ErrorHints = ["get_code_actions for a diagnostic to see available fixes", "edit_file to make corrections"];

   private readonly IWorkspace _workspace;

   public EditFileTool(IWorkspace workspace)
   {
      _workspace = workspace;
   }

   [McpServerTool(Name = "edit_file", Destructive = true, OpenWorld = false)]
   [Description("""
      Batch find/replace on a C# file in the loaded solution. Applies all edits atomically,
      updates the Roslyn workspace, and returns compiler diagnostics.
      Each find string must appear exactly once in the file.
      """)]
   public async Task<ToolResult<EditFileResponse>> EditFileAsync(
      [Description("Path to source file (absolute or relative to solution directory).")]
      string filePath,
      [Description("Edits to apply. Each has a 'find' string and its 'replace' string.")]
      EditOperation[] edits,
      CancellationToken cancellationToken = default)
   {
      using var writeLock = await _workspace.WriteLock.AcquireAsync(cancellationToken);

      if (edits is null || edits.Length == 0)
      {
         throw new ToolException(
            ErrorCodes.InvalidParameter,
            "At least one edit is required",
            "Provide an array of {find, replace} objects");
      }

      // Validate find strings are non-empty
      for (int i = 0; i < edits.Length; i++)
      {
         if (string.IsNullOrEmpty(edits[i].Find))
         {
            throw new ToolException(
               ErrorCodes.InvalidParameter,
               $"Edit [{i}]: find string must not be empty");
         }
      }

      var solution = _workspace.EnsureSolutionLoaded();
      var document = await _workspace.GetDocumentAsync(filePath, cancellationToken);

      var sourceText = await document.GetTextAsync(cancellationToken);
      var text = sourceText.ToString();

      // Phase 1: Validate all find strings exist exactly once, collect spans
      var textChanges = new List<TextChange>(edits.Length);
      for (int i = 0; i < edits.Length; i++)
      {
         var find = edits[i].Find;
         var firstIndex = text.IndexOf(find, StringComparison.Ordinal);

         if (firstIndex < 0)
         {
            throw new ToolException(
               ErrorCodes.InvalidParameter,
               $"Edit [{i}]: find text not found in file",
               $"The text to find does not exist in {_workspace.FormatPath(document.FilePath)}");
         }

         var secondIndex = text.IndexOf(find, firstIndex + 1, StringComparison.Ordinal);
         if (secondIndex >= 0)
         {
            var line1 = sourceText.Lines.GetLineFromPosition(firstIndex).LineNumber + 1;
            var line2 = sourceText.Lines.GetLineFromPosition(secondIndex).LineNumber + 1;
            throw new ToolException(
               ErrorCodes.InvalidParameter,
               $"Edit [{i}]: find text appears multiple times (lines {line1} and {line2})",
               "Provide more surrounding context in the find string to make it unique");
         }

         textChanges.Add(new TextChange(new TextSpan(firstIndex, find.Length), edits[i].Replace));
      }

      // Phase 1b: Check for overlapping spans
      var sorted = textChanges.OrderBy(c => c.Span.Start).ToList();
      for (int i = 1; i < sorted.Count; i++)
      {
         if (sorted[i].Span.Start < sorted[i - 1].Span.End)
         {
            throw new ToolException(
               ErrorCodes.InvalidParameter,
               "Edits have overlapping regions",
               "Ensure find strings do not overlap in the source file");
         }
      }

      // Phase 2: Apply atomically
      var newSourceText = sourceText.WithChanges(textChanges);
      var newSolution = solution.WithDocumentText(document.Id, newSourceText);

      if (!_workspace.ApplyChanges(newSolution))
      {
         throw new ToolException(
            ErrorCodes.ApplyChangesFailed,
            "Failed to apply changes to workspace");
      }

      // Phase 3: Collect diagnostics from updated document
      var updatedDocument = _workspace.Solution?.GetDocument(document.Id);
      if (updatedDocument is null)
      {
         throw new ToolException(
            ErrorCodes.InternalError,
            "Document not found after applying changes");
      }

      var diagnosticsResult = await EditingHelpers.GetFileDiagnosticsAsync(
         updatedDocument, _workspace, _workspace.MaxDiagnostics, cancellationToken);

      var response = new EditFileResponse
      {
         FilePath = _workspace.FormatPath(document.FilePath),
         EditsApplied = edits.Length,
         Applied = true,
         ErrorCount = diagnosticsResult.ErrorCount,
         WarningCount = diagnosticsResult.WarningCount,
         Diagnostics = diagnosticsResult.Diagnostics
      };

      return ToolResult.Create(
         response,
         diagnosticsResult.ErrorCount > 0 ? ErrorHints : CleanHints);
   }
}
