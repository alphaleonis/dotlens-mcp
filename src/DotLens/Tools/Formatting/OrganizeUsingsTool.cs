using System.ComponentModel;
using DotLens.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Formatting;

[McpServerToolType]
[DotLensWorkerTool]
public class OrganizeUsingsTool
{
   private static readonly string[] PreviewSuggestedTools = ["organize_usings with preview=false to apply changes"];
   private static readonly string[] AppliedSuggestedTools = ["get_diagnostics to verify no issues introduced"];

   private readonly IWorkspace _workspaceManager;

   public OrganizeUsingsTool(IWorkspace workspaceManager)
   {
      _workspaceManager = workspaceManager;
   }

   [McpServerTool(Name = "organize_usings", Destructive = true, Idempotent = true, OpenWorld = false)]
   [Description("Sort using and extern alias directives and remove unused usings in a file, project, or solution.")]
   public async Task<ToolResult<OrganizeUsingsResponse>> OrganizeUsingsAsync(
       [Description("Search scope. Defaults to entire solution.")]
       SearchScope? scope = null,
       [Description("Glob pattern to filter files (e.g., '*.cs', 'Services/*.cs'). Matches against file names.")]
       string? filePattern = null,
       [Description("Preview mode (default: true). Set to false to apply changes to disk.")]
       bool preview = true,
       CancellationToken cancellationToken = default)
   {
      using var writeLock = await _workspaceManager.WriteLock.AcquireAsync(cancellationToken);

      var solution = _workspaceManager.EnsureSolutionLoaded();

      // Default to solution scope
      scope ??= new SolutionScope();

      // Get documents to process based on scope
      var documentsToProcess = await GetDocumentsToProcessAsync(
          solution, scope, filePattern, cancellationToken);

      var processedFiles = new List<OrganizedFileInfo>();
      var totalFilesProcessed = 0;
      var filesWithChanges = 0;
      var totalUsingsRemoved = 0;
      var changedSolution = solution;

      foreach (var (document, project) in documentsToProcess)
      {
         if (document.FilePath == null)
            continue;
         totalFilesProcessed++;

         try
         {
            cancellationToken.ThrowIfCancellationRequested();

            // Get the document from the potentially updated solution
            var currentDoc = changedSolution.GetDocument(document.Id) ?? document;

            var result = await ProcessDocumentAsync(currentDoc, cancellationToken);
            if (result == null)
               continue;

            var (organizedDocument, originalCount, removedCount, finalCount) = result.Value;

            // Check if there were any changes
            var originalText = await currentDoc.GetTextAsync(cancellationToken);
            var newText = await organizedDocument.GetTextAsync(cancellationToken);

            if (originalText.ToString() == newText.ToString())
            {
               // No changes needed
               continue;
            }

            filesWithChanges++;
            totalUsingsRemoved += removedCount;

            // Include NewText in preview mode only for single file scope (to save tokens in batch mode)
            var includeNewText = preview && scope is FileScope;

            processedFiles.Add(new OrganizedFileInfo
            {
               FilePath = _workspaceManager.FormatPath(document.FilePath),
               ProjectName = project.Name,
               OriginalCount = originalCount,
               RemovedCount = removedCount,
               FinalCount = finalCount,
               NewText = includeNewText ? newText.ToString() : null
            });

            // Always track cumulative solution changes, regardless of preview mode.
            // This ensures preview shows the same results as apply would produce.
            changedSolution = organizedDocument.Project.Solution;
         }
         catch (Exception ex)
         {
            processedFiles.Add(new OrganizedFileInfo
            {
               FilePath = _workspaceManager.FormatPath(document.FilePath),
               ProjectName = project.Name,
               Error = ex.Message
            });
         }
      }

      // Apply changes to workspace (writes files and updates in-memory solution)
      if (!preview && changedSolution != solution)
      {
         if (!_workspaceManager.ApplyChanges(changedSolution, cancellationToken))
         {
            throw new ToolException(
               ErrorCodes.ApplyChangesFailed,
               "Failed to apply organize usings changes to workspace");
         }
      }

      var message = preview
          ? $"Preview: Would organize {filesWithChanges} file(s), removing {totalUsingsRemoved} unused using(s)"
          : $"Organized {filesWithChanges} file(s), removed {totalUsingsRemoved} unused using(s)";

      return ToolResult.Create(
          new OrganizeUsingsResponse
          {
             Applied = !preview,
             FilesProcessed = totalFilesProcessed,
             FilesChanged = filesWithChanges,
             TotalUsingsRemoved = totalUsingsRemoved,
             Files = processedFiles,
             Message = message
          },
          suggestedNextTools: preview ? PreviewSuggestedTools : AppliedSuggestedTools,
          totalCount: totalFilesProcessed,
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
               if (d.FilePath != null && fileMatcher.IsMatch(Path.GetFileName(d.FilePath)))
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
                  if (d.FilePath != null && fileMatcher.IsMatch(Path.GetFileName(d.FilePath)))
                  {
                     documents.Add((d, proj));
                  }
               }
            }
            break;

         default:
            throw new InvalidOperationException($"Unsupported scope type: {scope.GetType().Name}");
      }

      return documents;
   }

   private static async Task<(Document OrganizedDocument, int OriginalCount, int RemovedCount, int FinalCount)?> ProcessDocumentAsync(
       Document document,
       CancellationToken cancellationToken)
   {
      var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
      if (syntaxTree == null)
         return null;

      var root = await syntaxTree.GetRootAsync(cancellationToken);
      if (root is not CompilationUnitSyntax compilationUnit)
         return null;

      var originalCount = UsingDirectiveLayout.GetAllUsings(compilationUnit).Count();
      if (originalCount == 0 && !UsingDirectiveLayout.HasExternAliases(compilationUnit))
         return null;

      // Get semantic model to find unused usings (CS8019)
      var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
      if (semanticModel == null)
         return null;

      // CS8019 marks an unnecessary using. A global using that only another file uses gets none, as UsingToolsLeftoversTests pins.
      var spansToRemove = semanticModel.GetDiagnostics(cancellationToken: cancellationToken)
          .Where(d => d.Id == "CS8019")
          .Select(d => d.Location.SourceSpan)
          .ToHashSet();

      var unused = UsingDirectiveLayout.GetAllUsings(compilationUnit)
          .Where(u => spansToRemove.Any(span => u.Span.IntersectsWith(span)))
          .ToHashSet();
      var newLine = await UsingDirectiveLayout.GetNewLineAsync(document, cancellationToken);
      var newRoot = UsingDirectiveLayout.Organize(compilationUnit, u => !unused.Contains(u), newLine);

      return (document.WithSyntaxRoot(newRoot), originalCount, unused.Count, originalCount - unused.Count);
   }
}

/// <summary>
/// Response from organize usings operation.
/// </summary>
public record OrganizeUsingsResponse
{
   [Description("True if changes were written to disk, false if preview only.")]
   public required bool Applied { get; init; }

   [Description("Total number of files scanned.")]
   public required int FilesProcessed { get; init; }

   [Description("Number of files that had changes.")]
   public required int FilesChanged { get; init; }

   [Description("Total unused usings removed across all files.")]
   public required int TotalUsingsRemoved { get; init; }

   [Description("Details for each file that had changes.")]
   public required IReadOnlyList<OrganizedFileInfo> Files { get; init; }

   [Description("Summary message.")]
   public string? Message { get; init; }
}

/// <summary>
/// Info about a file processed for usings organization.
/// </summary>
public record OrganizedFileInfo
{
   [Description("Path to the file.")]
   public required string FilePath { get; init; }

   [Description("Name of the project containing the file.")]
   public required string ProjectName { get; init; }

   [Description("Number of using directives before organizing.")]
   public int OriginalCount { get; init; }

   [Description("Number of unused usings removed.")]
   public int RemovedCount { get; init; }

   [Description("Number of using directives after organizing.")]
   public int FinalCount { get; init; }

   [Description("New file content (only included in preview mode for single file scope).")]
   public string? NewText { get; init; }

   [Description("Error message if processing failed.")]
   public string? Error { get; init; }
}
