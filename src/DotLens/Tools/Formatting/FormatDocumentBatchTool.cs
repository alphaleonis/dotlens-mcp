using System.ComponentModel;
using System.Diagnostics;
using DotLens.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Formatting;

[McpServerToolType]
[DotLensWorkerTool]
public class FormatDocumentBatchTool
{
   private static readonly string[] PreviewSuggestedTools = ["format_document_batch with preview=false to apply changes"];
   private static readonly string[] AppliedSuggestedTools = ["get_diagnostics to verify no issues introduced"];

   private readonly IWorkspace _workspaceManager;

   public FormatDocumentBatchTool(IWorkspace workspaceManager)
   {
      _workspaceManager = workspaceManager;
   }

   [McpServerTool(Name = "format_document_batch", Destructive = true, Idempotent = true, OpenWorld = false)]
   [Description("Format documents using Roslyn's Formatter API. Respects .editorconfig settings.")]
   public async Task<ToolResult<FormatDocumentBatchResponse>> FormatDocumentBatchAsync(
       [Description("Search scope. Defaults to entire solution.")]
       SearchScope? scope = null,
       [Description("Glob pattern to filter files by relative path from solution root (e.g., '*Service*.cs', 'src/Controllers/*.cs').")]
       string? filePattern = null,
       [Description("Preview mode (default: true). Set to false to apply changes to disk.")]
       bool preview = true,
       [Description("Output verbosity: summary (counts only), compact (file paths), full (includes formatted content).")]
       ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
       CancellationToken cancellationToken = default)
   {
      using var writeLock = await _workspaceManager.WriteLock.AcquireAsync(cancellationToken);

      var solution = _workspaceManager.EnsureSolutionLoaded();

      // Default to solution scope
      scope ??= new SolutionScope();

      // Validate: filePattern doesn't make sense with FileScope
      if (scope is FileScope && !string.IsNullOrEmpty(filePattern))
      {
         throw new ToolException(
             ErrorCodes.InvalidParameter,
             "filePattern is ignored when scope is a single file",
             "Remove filePattern or use ProjectScope/SolutionScope");
      }

      // Get documents to process based on scope
      var documentsToProcess = await GetDocumentsToProcessAsync(solution, scope, filePattern, cancellationToken);

      var processedFiles = new List<FormattedFileInfo>();
      var totalFiles = 0;
      var filesFormatted = 0;
      var errorCount = 0;
      var changedSolution = solution;

      foreach (var (document, project) in documentsToProcess)
      {
         if (document.FilePath == null)
            continue;
         totalFiles++;

         try
         {
            cancellationToken.ThrowIfCancellationRequested();

            // Get the document from the potentially updated solution
            var currentDoc = changedSolution.GetDocument(document.Id) ?? document;

            // Get original text for comparison
            var originalText = await currentDoc.GetTextAsync(cancellationToken);

            // Format using Roslyn's Formatter API (respects .editorconfig from workspace)
            var formattedDocument = await Formatter.FormatAsync(currentDoc, cancellationToken: cancellationToken);
            var formattedText = await formattedDocument.GetTextAsync(cancellationToken);

            // Check if anything changed
            var hasChanges = !originalText.ContentEquals(formattedText);

            if (!hasChanges)
               continue;

            filesFormatted++;

            // Track the changed solution
            changedSolution = formattedDocument.Project.Solution;

            // Only include file details for Compact and Full verbosity
            if (verbosity != ToolResultVerbosity.Summary)
            {
               // Include formatted content in Full verbosity (both preview and apply modes)
               var includeContent = verbosity == ToolResultVerbosity.Full;

               processedFiles.Add(new FormattedFileInfo
               {
                  FilePath = _workspaceManager.FormatPath(document.FilePath),
                  ProjectName = project.Name,
                  NewText = includeContent ? formattedText.ToString() : null
               });
            }
         }
         catch (Exception ex)
         {
            errorCount++;
            // Only include error details for Compact and Full verbosity
            if (verbosity != ToolResultVerbosity.Summary)
            {
               processedFiles.Add(new FormattedFileInfo
               {
                  FilePath = _workspaceManager.FormatPath(document.FilePath),
                  ProjectName = project.Name,
                  Error = ex.Message
               });
            }
         }
      }

      // Apply changes to workspace (writes files and updates in-memory solution)
      if (!preview && changedSolution != solution)
      {
         if (!_workspaceManager.ApplyChanges(changedSolution))
         {
            throw new ToolException(
               ErrorCodes.ApplyChangesFailed,
               $"Failed to apply formatting changes to workspace after formatting {filesFormatted} file(s)");
         }
      }

      return ToolResult.Create(
          new FormatDocumentBatchResponse
          {
             Applied = !preview,
             FilesScanned = totalFiles,
             FilesFormatted = filesFormatted,
             ErrorCount = errorCount,
             Files = verbosity == ToolResultVerbosity.Summary ? null : processedFiles
          },
          suggestedNextTools: preview ? PreviewSuggestedTools : AppliedSuggestedTools,
          totalCount: totalFiles,
          returnedCount: processedFiles.Count);
   }

   private async Task<List<(Document Document, Project Project)>> GetDocumentsToProcessAsync(
       Solution solution,
       SearchScope scope,
       string? filePattern,
       CancellationToken cancellationToken)
   {
      var documents = new List<(Document, Project)>();
      var fileMatcher = new GlobMatcher(filePattern);

      // Get solution directory for computing relative paths
      var solutionDir = !string.IsNullOrEmpty(_workspaceManager.SolutionPath)
          ? Path.GetDirectoryName(_workspaceManager.SolutionPath)
          : null;

      switch (scope)
      {
         case FileScope fileScope:
            var doc = await _workspaceManager.GetDocumentAsync(fileScope.Path, cancellationToken);
            documents.Add((doc, doc.Project));
            break;

         case ProjectScope projectScope:
            var project = solution.Projects.FirstOrDefault(p =>
                p.Name.Equals(projectScope.Name, StringComparison.OrdinalIgnoreCase));

            if (project == null)
            {
               throw new ToolException(
                   ErrorCodes.ProjectNotFound,
                   $"Project not found: {projectScope.Name}",
                   $"Available projects: {string.Join(", ", solution.Projects.Select(p => p.Name))}");
            }

            foreach (var d in project.Documents)
            {
               if (d.FilePath != null && MatchesPattern(d.FilePath, solutionDir, fileMatcher))
               {
                  documents.Add((d, project));
               }
            }
            break;

         case SolutionScope:
            foreach (var proj in solution.Projects)
            {
               foreach (var d in proj.Documents)
               {
                  if (d.FilePath != null && MatchesPattern(d.FilePath, solutionDir, fileMatcher))
                  {
                     documents.Add((d, proj));
                  }
               }
            }
            break;

         default:
            throw new UnreachableException($"Unsupported scope type: {scope.GetType().Name}");
      }

      return documents;
   }

   /// <summary>
   /// Matches a file path against a glob pattern using relative path from solution root.
   /// Falls back to filename-only matching if relative path cannot be computed.
   /// </summary>
   private static bool MatchesPattern(string filePath, string? solutionDir, GlobMatcher matcher)
   {
      // Compute path to match against (relative from solution root, or filename as fallback)
      string pathToMatch;
      if (!string.IsNullOrEmpty(solutionDir))
      {
         try
         {
            var relativePath = Path.GetRelativePath(solutionDir, filePath);
            // Use relative path, normalized to forward slashes for cross-platform glob matching
            pathToMatch = PathUtils.NormalizePath(relativePath);
         }
         catch
         {
            // Fallback if paths are on different drives (Windows)
            pathToMatch = Path.GetFileName(filePath);
         }
      }
      else
      {
         pathToMatch = Path.GetFileName(filePath);
      }

      return matcher.IsMatch(pathToMatch);
   }
}

/// <summary>
/// Response from format document batch operation.
/// </summary>
public record FormatDocumentBatchResponse
{
   [Description("True if changes were written to disk, false if preview only.")]
   public required bool Applied { get; init; }

   [Description("Total number of files scanned.")]
   public required int FilesScanned { get; init; }

   [Description("Number of files that needed formatting changes.")]
   public required int FilesFormatted { get; init; }

   [Description("Number of files that failed to format.")]
   public int ErrorCount { get; init; }

   [Description("Details for each file that had changes. Null when verbosity is Summary.")]
   public IReadOnlyList<FormattedFileInfo>? Files { get; init; }
}

/// <summary>
/// Info about a formatted file.
/// </summary>
public record FormattedFileInfo
{
   [Description("Path to the file.")]
   public required string FilePath { get; init; }

   [Description("Name of the project containing the file.")]
   public required string ProjectName { get; init; }

   [Description("Formatted file content (only included in Full verbosity).")]
   public string? NewText { get; init; }

   [Description("Error message if formatting failed.")]
   public string? Error { get; init; }
}
