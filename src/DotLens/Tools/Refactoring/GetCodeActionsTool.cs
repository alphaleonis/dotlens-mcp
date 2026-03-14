using System.ComponentModel;
using System.Globalization;
using DotLens.Infrastructure;
using DotLens.Tools.Analysis;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Refactoring;

/// <summary>
/// Unified tool for discovering code actions (both fixes and refactorings).
/// </summary>
[McpServerToolType]
[DotLensWorkerTool]
public class GetCodeActionsTool
{
   private static readonly string[] NoActionsHints = ["Try a different position or selection", "get_diagnostics to check for issues"];

   private readonly IReadOnlyWorkspace _workspaceManager;

   public GetCodeActionsTool(IReadOnlyWorkspace workspaceManager)
   {
      _workspaceManager = workspaceManager;
   }

   [McpServerTool(Name = "get_code_actions", ReadOnly = true, OpenWorld = false)]
   [Description("Get available Roslyn code actions (refactorings or fixes) at a file position or for a specific diagnostic.")]
   public async Task<ToolResult<GetCodeActionsResponse>> GetCodeActionsAsync(
      [Description("Where to look for code actions")]
      CodeActionTarget target,
      [Description("Filter actions by title pattern (glob: * any chars, ? single char)")]
      string? titlePattern = null,
      CancellationToken cancellationToken = default)
   {
      return target switch
      {
         PositionTarget pos => await GetActionsAtPositionAsync(pos, titlePattern, cancellationToken),
         DiagnosticTarget diag => await GetActionsForDiagnosticAsync(diag, titlePattern, cancellationToken),
         _ => throw new ToolException(ErrorCodes.InvalidParameter, $"Unsupported CodeActionTarget type: {target.GetType().Name}. This is a bug - please report it.")
      };
   }

   private async Task<ToolResult<GetCodeActionsResponse>> GetActionsAtPositionAsync(
      PositionTarget pos,
      string? titlePattern,
      CancellationToken cancellationToken)
   {
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

      // Apply title pattern filter if specified
      GlobMatcher? titleMatcher = null;
      if (!string.IsNullOrEmpty(titlePattern))
      {
         titleMatcher = new GlobMatcher(titlePattern);
         allActions = allActions.Where(a => titleMatcher.IsMatch(a.action.Title)).ToList();
      }

      if (allActions.Count == 0)
      {
         var message = titleMatcher != null
            ? $"No code actions matching '{titlePattern}' at this position"
            : "No code actions available at this position";

         return ToolResult.Create(
            new GetCodeActionsResponse
            {
               Actions = [],
               Message = message,
               FixCount = 0,
               RefactoringCount = 0
            },
            NoActionsHints,
            0, 0);
      }

      var actions = allActions
         .GroupBy(a => a.action.Title)
         .Select(g => (
            Title: g.Key,
            Kind: g.First().kind == "fix" ? CodeActionKind.Fix : CodeActionKind.Refactoring,
            EquivalenceKey: g.First().action.EquivalenceKey,
            Count: g.Count()))
         .OrderBy(a => a.Kind)
         .ThenBy(a => a.Title)
         .Select((a, index) => new CodeActionItemInfo
         {
            Index = index,
            Title = a.Title,
            Kind = a.Kind,
            Key = null, // No stable keys for position-based actions
            EquivalenceKey = a.EquivalenceKey,
            Count = a.Count
         })
         .ToList();

      return ToolResult.Create(
         new GetCodeActionsResponse
         {
            Actions = actions,
            FixCount = actions.Count(a => a.Kind == CodeActionKind.Fix),
            RefactoringCount = actions.Count(a => a.Kind == CodeActionKind.Refactoring)
         },
         actions.Count > 0
            ? [$"apply_code_action with title=\"{actions[0].Title}\" to apply"]
            : [],
         actions.Count,
         actions.Count);
   }

   private async Task<ToolResult<GetCodeActionsResponse>> GetActionsForDiagnosticAsync(
      DiagnosticTarget diag,
      string? titlePattern,
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
         var nearbyCount = AnalysisHelpers.CountDiagnosticsNearPosition(allDiagnostics, position);

         throw new ToolException(
            ErrorCodes.SymbolNotFound,
            $"No diagnostic with ID '{diag.DiagnosticId}' found",
            nearbyCount > 0
               ? $"Found {nearbyCount} other diagnostic(s) near this position."
               : "No diagnostics at this position. Run get_diagnostics to see all.");
      }

      var codeActions = await AnalysisHelpers.GetCodeActionsForDiagnosticAsync(document, diagnostic, cancellationToken);

      // Apply title pattern filter if specified
      if (!string.IsNullOrEmpty(titlePattern))
      {
         var titleMatcher = new GlobMatcher(titlePattern);
         codeActions = codeActions.Where(a => titleMatcher.IsMatch(a.Action.Title)).ToList();
      }

      var actions = codeActions.Select((x, index) => new CodeActionItemInfo
      {
         Index = index,
         Title = x.Action.Title,
         Kind = CodeActionKind.Fix,
         Key = AnalysisHelpers.ComputeCodeActionKey(x.Provider, x.Action),
         EquivalenceKey = x.Action.EquivalenceKey,
         Count = 1
      }).ToList();

      var diagnosticInfo = new DiagnosticInfo
      {
         Id = diagnostic.Id,
         Message = diagnostic.GetMessage(CultureInfo.InvariantCulture),
         Severity = diagnostic.Severity.ToString()
      };

      if (actions.Count == 0)
      {
         return ToolResult.Create(
            new GetCodeActionsResponse
            {
               Actions = [],
               Message = $"No code fixes available for {diag.DiagnosticId}",
               FixCount = 0,
               RefactoringCount = 0,
               Diagnostic = diagnosticInfo
            },
            ["No automated fixes available. Manual intervention required.", "get_diagnostics to see other issues"],
            0, 0);
      }

      return ToolResult.Create(
         new GetCodeActionsResponse
         {
            Actions = actions,
            FixCount = actions.Count,
            RefactoringCount = 0,
            Diagnostic = diagnosticInfo
         },
         [$"apply_code_action with key=\"{actions[0].Key}\" to apply"],
         actions.Count,
         actions.Count);
   }
}
