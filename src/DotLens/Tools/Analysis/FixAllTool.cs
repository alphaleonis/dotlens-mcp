using System.Collections.Immutable;
using System.ComponentModel;
using DotLens.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Analysis;

#region Response Types

/// <summary>
/// Result from FixAll tool.
/// </summary>
public record FixAllResponse
{
   [Description("True if changes were written to disk, false if preview mode.")]
   public required bool Applied { get; init; }

   [Description("Diagnostic code that was fixed.")]
   public required string DiagnosticId { get; init; }

   [Description("Scope of the fix: document, project, or solution.")]
   public required string Scope { get; init; }

   [Description("Total number of diagnostics found matching the ID.")]
   public required int DiagnosticsFound { get; init; }

   [Description("Number of diagnostics that were fixed.")]
   public required int DiagnosticsFixed { get; init; }

   [Description("Files modified by the fixes.")]
   public required IReadOnlyList<CodeFixChangedFile> ChangedFiles { get; init; }

   [Description("True if this was a preview, false if changes were applied.")]
   public required bool Preview { get; init; }

   [Description("Summary message describing the operation result.")]
   public required string Message { get; init; }
}

#endregion

/// <summary>
/// Bulk fix tool using Roslyn's FixAllProvider.
/// </summary>
[McpServerToolType]
[DotLensWorkerTool]
public class FixAllTool
{
   private static readonly string[] SuggestedNextToolsVerifyDiagnostics = ["get_diagnostics to verify there are issues to fix"];
   private static readonly string[] SuggestedNextToolsNoChanges = ["The fix may have already been applied or the diagnostics cannot be fixed"];
   private static readonly string[] SuggestedNextToolsPreview = ["fix_all with preview=false to apply changes to disk"];
   private static readonly string[] SuggestedNextToolsApplied = ["get_diagnostics to verify fixes were applied"];

   private readonly IWorkspace _workspaceManager;

   public FixAllTool(IWorkspace workspaceManager)
   {
      _workspaceManager = workspaceManager;
   }

   [McpServerTool(Name = "fix_all", Destructive = true, OpenWorld = false)]
   [Description("Apply a code fix to all occurrences of a diagnostic across a document, project, or solution.")]
   public async Task<ToolResult<FixAllResponse>> FixAllAsync(
      [Description("Diagnostic ID to fix (e.g., CS8019, CS0168)")] string diagnosticId,
      [Description("Scope: 'document', 'project', or 'solution'")] string scope,
      CancellationToken cancellationToken,
      [Description("Path to source file (absolute or relative to solution directory; required for 'document' scope, optional for others)")]
      string? filePath = null,
      [Description("Project name (required for 'project' scope when filePath not provided)")]
      string? projectName = null,
      [Description("Preview mode (default: true). Set to false to apply changes to disk.")]
      bool preview = true)
   {
      using var writeLock = await _workspaceManager.WriteLock.AcquireAsync(cancellationToken);

      var solution = _workspaceManager.EnsureSolutionLoaded();

      // Determine scope
      var fixAllScope = scope.ToLowerInvariant() switch
      {
         "document" => FixAllScope.Document,
         "project" => FixAllScope.Project,
         "solution" => FixAllScope.Solution,
         _ => throw new ToolException(
            ErrorCodes.InvalidParameter,
            $"Invalid scope '{scope}'. Must be 'document', 'project', or 'solution'")
      };

      // Find the document/project
      Document? document = null;
      Project? project = null;

      if (!string.IsNullOrEmpty(filePath))
      {
         document = await _workspaceManager.GetDocumentAsync(filePath, cancellationToken);
         if (document == null)
         {
            throw new ToolException(
               ErrorCodes.FileNotInSolution,
               $"File not found in solution: {filePath}");
         }
         project = document.Project;
      }
      else if (!string.IsNullOrEmpty(projectName))
      {
         project = solution.Projects.FirstOrDefault(p =>
            p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));
         if (project == null)
         {
            throw new ToolException(
               ErrorCodes.InvalidParameter,
               $"Project not found: {projectName}");
         }
         document = project.Documents.FirstOrDefault();
      }
      else if (fixAllScope == FixAllScope.Document)
      {
         throw new ToolException(
            ErrorCodes.InvalidParameter,
            "filePath is required for document scope");
      }
      else
      {
         // For project/solution scope without specific project, use first project
         project = solution.Projects.FirstOrDefault();
         if (project == null)
         {
            throw new ToolException(
               ErrorCodes.AnalysisFailed,
               "No projects found in solution");
         }
         document = project.Documents.FirstOrDefault();
      }

      if (document == null)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            "No documents found in project");
      }

      // Find diagnostics matching the ID
      var diagnostics = await FindDiagnosticsAsync(solution, diagnosticId, fixAllScope, document, project!, cancellationToken);

      if (diagnostics.Count == 0)
      {
         return ToolResult.Create(
            new FixAllResponse
            {
               Applied = false,
               DiagnosticId = diagnosticId,
               Scope = scope,
               DiagnosticsFound = 0,
               DiagnosticsFixed = 0,
               ChangedFiles = new List<CodeFixChangedFile>(),
               Preview = preview,
               Message = $"No diagnostics with ID '{diagnosticId}' found in {scope}"
            },
            SuggestedNextToolsVerifyDiagnostics);
      }

      // Find a CodeFixProvider that can fix this diagnostic
      var providers = CodeFixHelpers.GetCodeFixProvidersForProject(project!);
      var provider = providers.FirstOrDefault(p => p.FixableDiagnosticIds.Contains(diagnosticId));

      if (provider == null)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            $"No code fix provider found for diagnostic '{diagnosticId}'",
            "This diagnostic may not have automated fixes available");
      }

      // Get the FixAllProvider
      var fixAllProvider = provider.GetFixAllProvider();
      if (fixAllProvider == null)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            $"The code fix provider for '{diagnosticId}' does not support bulk fixes",
            "Use apply_code_action to fix one occurrence at a time");
      }

      // Get a code action from the first diagnostic to get the equivalence key
      var firstDiagnostic = diagnostics.First();
      var firstDiagDoc = solution.GetDocument(firstDiagnostic.Location.SourceTree);
      if (firstDiagDoc == null)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            "Could not find document for diagnostic location");
      }

      var codeActions = new List<CodeAction>();
      var codeFixContext = new CodeFixContext(
         firstDiagDoc,
         firstDiagnostic,
         (action, _) => codeActions.Add(action),
         cancellationToken);

      await provider.RegisterCodeFixesAsync(codeFixContext);

      if (codeActions.Count == 0)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            $"No code actions registered for diagnostic '{diagnosticId}'");
      }

      // Create FixAllContext
      var diagnosticProvider = new DiagnosticProvider(diagnostics);
      var fixAllContext = new FixAllContext(
         document,
         provider,
         fixAllScope,
         codeActions[0].EquivalenceKey ?? "",
         ImmutableArray.Create(diagnosticId),
         diagnosticProvider,
         cancellationToken);

      // Get the fix all code action
      var fixAllAction = await fixAllProvider.GetFixAsync(fixAllContext);
      if (fixAllAction == null)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            "FixAllProvider did not produce a fix action");
      }

      // Apply the fix
      var operations = await fixAllAction.GetOperationsAsync(cancellationToken);
      var changedSolution = solution;

      foreach (var operation in operations)
      {
         if (operation is ApplyChangesOperation applyChangesOp)
         {
            changedSolution = applyChangesOp.ChangedSolution;
            break;
         }
      }

      if (changedSolution == solution)
      {
         return ToolResult.Create(
            new FixAllResponse
            {
               Applied = false,
               DiagnosticId = diagnosticId,
               Scope = scope,
               DiagnosticsFound = diagnostics.Count,
               DiagnosticsFixed = 0,
               ChangedFiles = new List<CodeFixChangedFile>(),
               Preview = preview,
               Message = "Fix all operation did not produce any changes"
            },
            SuggestedNextToolsNoChanges);
      }

      // Collect changed documents
      var changedDocuments = new List<CodeFixChangedFile>();
      var solutionChanges = changedSolution.GetChanges(solution);

      foreach (var projectChanges in solutionChanges.GetProjectChanges())
      {
         foreach (var changedDocId in projectChanges.GetChangedDocuments())
         {
            var oldDoc = solution.GetDocument(changedDocId);
            var newDoc = changedSolution.GetDocument(changedDocId);
            if (oldDoc == null || newDoc == null)
               continue;

            var oldText = await oldDoc.GetTextAsync(cancellationToken);
            var newText = await newDoc.GetTextAsync(cancellationToken);
            var changes = newText.GetTextChanges(oldText).ToList();

            changedDocuments.Add(new CodeFixChangedFile
            {
               FilePath = PathUtils.FormatPath(newDoc.FilePath, _workspaceManager.SolutionPath, _workspaceManager.UseAbsolutePaths),
               FileName = newDoc.Name,
               ChangeCount = changes.Count,
               NewText = preview ? newText.ToString() : null,
               ChangeType = "Modified"
            });
         }
      }

      // Apply changes to workspace (writes files and updates in-memory solution)
      if (!preview)
      {
         if (!_workspaceManager.ApplyChanges(changedSolution))
         {
            throw new ToolException(
               ErrorCodes.ApplyChangesFailed,
               "Failed to apply fix-all changes to workspace");
         }
      }

      return ToolResult.Create(
         new FixAllResponse
         {
            Applied = !preview,
            DiagnosticId = diagnosticId,
            Scope = scope,
            DiagnosticsFound = diagnostics.Count,
            DiagnosticsFixed = diagnostics.Count,
            ChangedFiles = changedDocuments,
            Preview = preview,
            Message = preview
               ? $"Preview: Would fix {diagnostics.Count} occurrence(s) of {diagnosticId} in {changedDocuments.Count} file(s)"
               : $"Fixed {diagnostics.Count} occurrence(s) of {diagnosticId} in {changedDocuments.Count} file(s)"
         },
         preview
            ? SuggestedNextToolsPreview
            : SuggestedNextToolsApplied);
   }

   private static async Task<List<Diagnostic>> FindDiagnosticsAsync(
      Solution solution,
      string diagnosticId,
      FixAllScope scope,
      Document document,
      Project project,
      CancellationToken cancellationToken)
   {
      var diagnostics = new List<Diagnostic>();

      IEnumerable<Document> documents = scope switch
      {
         FixAllScope.Document => new[] { document },
         FixAllScope.Project => project.Documents,
         FixAllScope.Solution => solution.Projects.SelectMany(p => p.Documents),
         _ => new[] { document }
      };

      foreach (var doc in documents)
      {
         cancellationToken.ThrowIfCancellationRequested();

         var semanticModel = await doc.GetSemanticModelAsync(cancellationToken);
         var syntaxTree = await doc.GetSyntaxTreeAsync(cancellationToken);

         if (semanticModel != null)
         {
            var docDiagnostics = semanticModel.GetDiagnostics(cancellationToken: cancellationToken)
               .Where(d => d.Id == diagnosticId)
               .ToList();
            diagnostics.AddRange(docDiagnostics);
         }

         if (syntaxTree != null)
         {
            var syntaxDiagnostics = syntaxTree.GetDiagnostics(cancellationToken)
               .Where(d => d.Id == diagnosticId)
               .ToList();
            diagnostics.AddRange(syntaxDiagnostics);
         }
      }

      return diagnostics;
   }

   /// <summary>
   /// Diagnostic provider for FixAllContext.
   /// </summary>
   private class DiagnosticProvider : FixAllContext.DiagnosticProvider
   {
      private readonly IReadOnlyList<Diagnostic> _diagnostics;

      public DiagnosticProvider(IReadOnlyList<Diagnostic> diagnostics)
      {
         _diagnostics = diagnostics;
      }

      public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
      {
         return Task.FromResult(_diagnostics.Where(d =>
            d.Location.SourceTree != null &&
            project.Documents.Any(doc => doc.FilePath == d.Location.SourceTree.FilePath)));
      }

      public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken)
      {
         return Task.FromResult(_diagnostics.Where(d =>
            d.Location.SourceTree != null &&
            d.Location.SourceTree.FilePath == document.FilePath));
      }

      public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken)
      {
         return GetAllDiagnosticsAsync(project, cancellationToken);
      }
   }
}
