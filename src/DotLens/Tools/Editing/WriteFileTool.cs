using System.ComponentModel;
using System.Security;
using System.Text;
using DotLens.Infrastructure;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Editing;

#region Response Types

public record WriteFileResponse
{
   [Description("File path that was written.")]
   public required string FilePath { get; init; }

   [Description("True if content was written to disk and workspace.")]
   public required bool Applied { get; init; }

   [Description("True if a new file was created (vs updating an existing file).")]
   public required bool IsNewFile { get; init; }

   [Description("Number of compiler errors after writing.")]
   public required int ErrorCount { get; init; }

   [Description("Number of compiler warnings after writing.")]
   public required int WarningCount { get; init; }

   [Description("Compiler diagnostics for the written file.")]
   public required IReadOnlyList<DiagnosticEntry> Diagnostics { get; init; }
}

#endregion

[McpServerToolType]
[DotLensWorkerTool]
public class WriteFileTool
{
   private static readonly string[] CleanHints = ["No compiler errors - file written successfully"];
   private static readonly string[] ErrorHints = ["get_code_actions for a diagnostic to see available fixes", "edit_file to make corrections"];

   private readonly IWorkspace _workspace;

   public WriteFileTool(IWorkspace workspace)
   {
      _workspace = workspace;
   }

   [McpServerTool(Name = "write_file", Destructive = true, OpenWorld = false)]
   [Description("""
      Write or create a C# source file in the loaded solution. If the file exists,
      replaces its content. If it's a new file under a project directory, creates
      it on disk and adds it to the Roslyn workspace. Returns compiler diagnostics.
      """)]
   public async Task<ToolResult<WriteFileResponse>> WriteFileAsync(
      [Description("Path to source file (absolute or relative to solution directory).")]
      string filePath,
      [Description("New file content to write.")]
      string content,
      CancellationToken cancellationToken = default)
   {
      using var writeLock = await _workspace.WriteLock.AcquireAsync(cancellationToken);

      if (string.IsNullOrWhiteSpace(content))
      {
         throw new ToolException(
            ErrorCodes.InvalidParameter,
            "Content must not be empty",
            "Provide the complete file content to write");
      }

      _workspace.EnsureSolutionLoaded();

      // Try to find existing document
      Document? existingDocument;
      try
      {
         existingDocument = await _workspace.TryGetDocumentAsync(filePath, cancellationToken);
      }
      catch (SecurityException ex)
      {
         throw new ToolException(
            ErrorCodes.SecurityError,
            ex.Message,
            "File path may be outside the solution directory");
      }

      Document resultDocument;
      bool isNewFile;

      if (existingDocument is not null)
      {
         // Update existing file
         isNewFile = false;
         var existingText = await existingDocument.GetTextAsync(cancellationToken);
         var encoding = existingText.Encoding ?? Encoding.UTF8;
         var newSourceText = SourceText.From(content, encoding);
         var newSolution = _workspace.Solution!.WithDocumentText(existingDocument.Id, newSourceText);

         if (!_workspace.ApplyChanges(newSolution))
         {
            throw new ToolException(
               ErrorCodes.ApplyChangesFailed,
               "Failed to apply changes to workspace");
         }

         resultDocument = _workspace.Solution?.GetDocument(existingDocument.Id)
            ?? throw new ToolException(ErrorCodes.InternalError, "Document not found after applying changes");
      }
      else
      {
         // Create new file
         isNewFile = true;
         resultDocument = await _workspace.AddDocumentAsync(filePath, content, cancellationToken);
      }

      // Collect diagnostics
      var diagnosticsResult = await EditingHelpers.GetFileDiagnosticsAsync(
         resultDocument, _workspace, _workspace.MaxDiagnostics, cancellationToken);

      var response = new WriteFileResponse
      {
         FilePath = _workspace.FormatPath(resultDocument.FilePath),
         Applied = true,
         IsNewFile = isNewFile,
         ErrorCount = diagnosticsResult.ErrorCount,
         WarningCount = diagnosticsResult.WarningCount,
         Diagnostics = diagnosticsResult.Diagnostics
      };

      return ToolResult.Create(
         response,
         diagnosticsResult.ErrorCount > 0 ? ErrorHints : CleanHints);
   }
}
