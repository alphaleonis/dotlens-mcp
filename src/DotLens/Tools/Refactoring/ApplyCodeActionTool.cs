using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Tools.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Refactoring;

/// <summary>
/// Unified tool for applying code actions (both fixes and refactorings).
/// </summary>
[McpServerToolType]
[DotLensWorkerTool]
public class ApplyCodeActionTool
{
   private static readonly string[] PreviewHints = ["apply_code_action with preview=false to apply changes"];
   private static readonly string[] AppliedHints = ["get_diagnostics to verify changes"];

   private readonly IWorkspace _workspaceManager;

   public ApplyCodeActionTool(IWorkspace workspaceManager)
   {
      _workspaceManager = workspaceManager;
   }

   [McpServerTool(Name = "apply_code_action", Destructive = true, OpenWorld = false)]
   [Description("Apply a Roslyn code action by title (exact or partial match) or by stable key from get_code_actions.")]
   public async Task<ToolResult<ApplyCodeActionResponse>> ApplyCodeActionAsync(
      [Description("Where the code action is located")]
      CodeActionTarget target,
      [Description("How to identify the action to apply")]
      CodeActionSelection selection,
      [Description("Preview mode (default: true). Set to false to apply.")]
      bool preview = true,
      CancellationToken cancellationToken = default)
   {
      using var writeLock = await _workspaceManager.WriteLock.AcquireAsync(cancellationToken);

      return target switch
      {
         PositionTarget pos => await ApplyAtPositionAsync(pos, selection, preview, cancellationToken),
         DiagnosticTarget diag => await ApplyForDiagnosticAsync(diag, selection, preview, cancellationToken),
         _ => throw new ToolException(ErrorCodes.InvalidParameter, $"Unsupported CodeActionTarget type: {target.GetType().Name}. This is a bug - please report it.")
      };
   }

   private async Task<ToolResult<ApplyCodeActionResponse>> ApplyAtPositionAsync(
      PositionTarget pos,
      CodeActionSelection selection,
      bool preview,
      CancellationToken cancellationToken)
   {
      // Position targets require title selection (no stable keys)
      if (selection is not TitleSelection titleSelection)
      {
         throw new ToolException(
            ErrorCodes.InvalidParameter,
            selection is KeySelection
               ? "Key selection only works with diagnostic targets. Use title selection for position targets."
               : $"Unsupported selection type for position targets: {selection.GetType().Name}");
      }

      var document = await _workspaceManager.GetDocumentAsync(pos.FilePath, cancellationToken);

      // Validate EndLine/EndColumn are either both set or both null
      if (pos.EndLine.HasValue != pos.EndColumn.HasValue)
      {
         throw new ToolException(ErrorCodes.InvalidParameter,
            "Both EndLine and EndColumn must be provided together, or neither");
      }

      var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken)
         ?? throw new ToolException(ErrorCodes.AnalysisFailed, "Could not get syntax tree");

      var startPosition = RoslynHelpers.GetPosition(syntaxTree, pos.Line, pos.Column);
      var endPosition = pos.GetEndPosition(syntaxTree);

      var allActions = await RefactoringHelpers.GetAllCodeActionsAtPositionAsync(
         document, startPosition, endPosition, includeCodeFixes: true, includeRefactorings: true, cancellationToken);

      var (matchingAction, kind) = FindActionByTitle(allActions, titleSelection.Title);

      if (matchingAction == null)
      {
         var availableTitles = allActions.Select(a => a.action.Title).Distinct().Take(10).ToList();
         throw new ToolException(
            ErrorCodes.SymbolNotFound,
            $"No code action found with title matching '{titleSelection.Title}'",
            availableTitles.Count > 0
               ? $"Available actions: {string.Join(", ", availableTitles)}"
               : "No actions available. Try get_code_actions first.");
      }

      return await ApplyActionAsync(matchingAction, kind, null, null, preview, cancellationToken);
   }

   private async Task<ToolResult<ApplyCodeActionResponse>> ApplyForDiagnosticAsync(
      DiagnosticTarget diag,
      CodeActionSelection selection,
      bool preview,
      CancellationToken cancellationToken)
   {
      var document = await _workspaceManager.GetDocumentAsync(diag.FilePath, cancellationToken);

      var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
      var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);

      if (semanticModel == null || syntaxTree == null)
      {
         throw new ToolException(ErrorCodes.AnalysisFailed, "Could not get semantic model or syntax tree");
      }

      var allDiagnostics = semanticModel.GetDiagnostics(cancellationToken: cancellationToken)
         .Concat(syntaxTree.GetDiagnostics(cancellationToken))
         .ToList();

      var position = RoslynHelpers.GetPosition(syntaxTree, diag.Line, diag.Column);
      var diagnostic = AnalysisHelpers.FindDiagnosticByIdAndPosition(allDiagnostics, diag.DiagnosticId, position);

      if (diagnostic == null)
      {
         throw new ToolException(
            ErrorCodes.SymbolNotFound,
            $"No diagnostic with ID '{diag.DiagnosticId}' found at line {diag.Line}, column {diag.Column}",
            "Run get_code_actions first to verify the diagnostic exists.");
      }

      var codeActions = await AnalysisHelpers.GetCodeActionsForDiagnosticAsync(document, diagnostic, cancellationToken);

      if (codeActions.Count == 0)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            $"No code fixes available for diagnostic '{diag.DiagnosticId}'",
            "This diagnostic may not have automated fixes. Use get_code_actions to verify.");
      }

      (CodeFixProvider Provider, CodeAction Action) matchingAction;
      string? actionKey = null;

      switch (selection)
      {
         case TitleSelection titleSel:
            matchingAction = FindFixByTitle(codeActions, titleSel.Title);
            if (matchingAction.Action != null)
            {
               actionKey = AnalysisHelpers.ComputeCodeActionKey(matchingAction.Provider, matchingAction.Action);
            }
            break;

         case KeySelection keySel:
            matchingAction = FindFixByKey(codeActions, keySel.Key);
            actionKey = keySel.Key;
            break;

         default:
            throw new ToolException(ErrorCodes.InvalidParameter, $"Unknown selection mode: {selection.GetType().Name}");
      }

      if (matchingAction.Action == null)
      {
         var availableKeys = codeActions.Select(x => AnalysisHelpers.ComputeCodeActionKey(x.Provider, x.Action)).ToList();
         var availableTitles = codeActions.Select(x => x.Action.Title).ToList();

         var hint = selection is KeySelection keySel
            ? $"Available keys: {string.Join(", ", availableKeys)}"
            : $"Available actions: {string.Join(", ", availableTitles)}";

         throw new ToolException(
            ErrorCodes.SymbolNotFound,
            selection is KeySelection ks
               ? $"No fix found with key '{ks.Key}'"
               : $"No fix found with title matching '{((TitleSelection)selection).Title}'",
            hint);
      }

      return await ApplyActionAsync(matchingAction.Action, CodeActionKind.Fix, actionKey, diag.DiagnosticId, preview, cancellationToken);
   }

   private async Task<ToolResult<ApplyCodeActionResponse>> ApplyActionAsync(
      CodeAction action,
      CodeActionKind kind,
      string? actionKey,
      string? diagnosticId,
      bool preview,
      CancellationToken cancellationToken)
   {
      var operations = await action.GetOperationsAsync(cancellationToken);
      var solution = _workspaceManager.EnsureSolutionLoaded();
      var changedSolution = solution;

      // Roslyn code actions typically have one ApplyChangesOperation containing all changes.
      // If multiple exist, they represent the same logical change - use the first one.
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
         throw new ToolException(ErrorCodes.AnalysisFailed, "Code action did not produce any changes");
      }

      var changedFiles = await RefactoringHelpers.GetChangedFilesAsync(solution, changedSolution, preview, cancellationToken);

      if (!preview)
      {
         if (!_workspaceManager.ApplyChanges(changedSolution, cancellationToken))
         {
            throw new ToolException(ErrorCodes.ApplyChangesFailed, "Failed to apply code action changes to workspace");
         }
      }

      return ToolResult.Create(
         new ApplyCodeActionResponse
         {
            Applied = !preview,
            ActionTitle = action.Title,
            ActionKind = kind,
            ChangedFiles = changedFiles.Select(f => new ChangedFileInfo
            {
               FilePath = _workspaceManager.FormatPath(f.FilePath) ?? f.FilePath ?? "",
               ChangeCount = f.ChangeCount,
               NewText = preview ? f.NewText : null
            }).ToList(),
            Preview = preview,
            ActionKey = actionKey,
            DiagnosticId = diagnosticId
         },
         preview ? PreviewHints : AppliedHints,
         changedFiles.Count,
         changedFiles.Count);
   }

   private static (CodeAction? action, CodeActionKind kind) FindActionByTitle(
      List<(CodeAction action, string kind)> actions,
      string title)
   {
      // Try exact match first
      var match = actions.FirstOrDefault(a =>
         a.action.Title.Equals(title, StringComparison.OrdinalIgnoreCase));

      // Fall back to partial match with ambiguity detection
      if (match.action == null)
      {
         var partialMatches = actions
            .Where(a => a.action.Title.Contains(title, StringComparison.OrdinalIgnoreCase))
            .ToList();

         if (partialMatches.Count > 1)
         {
            var matchingTitles = partialMatches.Select(m => m.action.Title).ToList();
            throw new ToolException(
               ErrorCodes.AmbiguousSymbol,
               $"Partial title '{title}' matches {partialMatches.Count} actions",
               $"Matching actions: {string.Join(", ", matchingTitles.Select(t => $"'{t}'"))}. Use a more specific title or the exact action title.");
         }

         match = partialMatches.FirstOrDefault();
      }

      if (match.action == null)
      {
         return (null, CodeActionKind.Refactoring);
      }

      return (match.action, match.kind == "fix" ? CodeActionKind.Fix : CodeActionKind.Refactoring);
   }

   private static (CodeFixProvider Provider, CodeAction Action) FindFixByTitle(
      List<(CodeFixProvider Provider, CodeAction Action)> actions,
      string title)
   {
      // Try exact match first
      var match = actions.FirstOrDefault(a =>
         a.Action.Title.Equals(title, StringComparison.OrdinalIgnoreCase));

      // Fall back to partial match with ambiguity detection
      if (match.Action == null)
      {
         var partialMatches = actions
            .Where(a => a.Action.Title.Contains(title, StringComparison.OrdinalIgnoreCase))
            .ToList();

         if (partialMatches.Count > 1)
         {
            var matchingTitles = partialMatches.Select(m => m.Action.Title).ToList();
            throw new ToolException(
               ErrorCodes.AmbiguousSymbol,
               $"Partial title '{title}' matches {partialMatches.Count} code fixes",
               $"Matching fixes: {string.Join(", ", matchingTitles.Select(t => $"'{t}'"))}. Use a more specific title or the exact action title.");
         }

         match = partialMatches.FirstOrDefault();
      }

      return match;
   }

   private static (CodeFixProvider Provider, CodeAction Action) FindFixByKey(
      List<(CodeFixProvider Provider, CodeAction Action)> actions,
      string key)
   {
      return actions.FirstOrDefault(x =>
         AnalysisHelpers.ComputeCodeActionKey(x.Provider, x.Action) == key);
   }
}
