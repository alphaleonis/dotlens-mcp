using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Refactoring;

[McpServerToolType]
[DotLensWorkerTool]
public class ImplementMissingMembersTool
{
   private static readonly string[] PreviewHints = ["implement_missing_members with preview=false to apply changes"];
   private static readonly string[] AppliedHints = ["get_diagnostics to verify changes"];

   private readonly IWorkspace _workspaceManager;
   private readonly SymbolLookupService _symbolLookup;

   public ImplementMissingMembersTool(IWorkspace workspaceManager, SymbolLookupService symbolLookup)
   {
      _workspaceManager = workspaceManager;
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "implement_missing_members", Destructive = true, Idempotent = true, OpenWorld = false)]
   [Description("Generate stub implementations for all missing interface and abstract members of a type.")]
   public async Task<ToolResult<ApplyCodeActionResponse>> ImplementMissingMembersAsync(
      [Description("Type to implement members for.")] SymbolLookup target,
      [Description("Preview mode (default: true). Set to false to apply changes.")]
      bool preview = true,
      CancellationToken cancellationToken = default)
   {
      using var writeLock = await _workspaceManager.WriteLock.AcquireAsync(cancellationToken);

      var solution = _workspaceManager.EnsureSolutionLoaded();

      // Find the type symbol
      var lookupResult = await _symbolLookup.FindSingleAsync<INamedTypeSymbol>(
         target,
         t => t.TypeKind is TypeKind.Class or TypeKind.Struct,
         cancellationToken);

      if (lookupResult is not SourceSymbolResult<INamedTypeSymbol> sourceResult)
      {
         throw new ToolException(
            ErrorCodes.SymbolInMetadata,
            $"Cannot implement members for metadata symbol '{lookupResult.Symbol.Name}'");
      }

      var typeSymbol = sourceResult.Symbol;
      var document = sourceResult.Document;

      // Get the type's declaration syntax to span the full declaration including base list
      var syntaxReference = typeSymbol.DeclaringSyntaxReferences.FirstOrDefault();
      if (syntaxReference == null)
      {
         throw new ToolException(
            ErrorCodes.SymbolNotFound,
            $"Could not find syntax reference for type '{RoslynHelpers.GetName(typeSymbol)}'");
      }

      var declarationSyntax = await syntaxReference.GetSyntaxAsync(cancellationToken);

      // Use the full declaration span to intersect with all diagnostics on the type
      var startPosition = declarationSyntax.SpanStart;
      var endPosition = declarationSyntax.Span.End;

      // Try to get code actions - first "Implement interface", then "Implement abstract"
      // Note: We match by Title rather than EquivalenceKey because Roslyn's code fix providers
      // use nullable/optional EquivalenceKey values that vary by version. Title-based matching
      // is more reliable for English-language deployments. See: https://github.com/dotnet/roslyn/issues/5975
      var allActions = await RefactoringHelpers.GetAllCodeActionsAtPositionAsync(
         document, startPosition, endPosition, includeCodeFixes: true, includeRefactorings: true, cancellationToken);

      var matchingAction = allActions.FirstOrDefault(a =>
         a.action.Title.Contains("Implement interface", StringComparison.OrdinalIgnoreCase));

      matchingAction = matchingAction.action != null ? matchingAction : allActions.FirstOrDefault(a =>
         a.action.Title.Contains("Implement abstract", StringComparison.OrdinalIgnoreCase));

      if (matchingAction.action == null)
      {
         // Check if there are any missing members to implement
         if (!MissingMemberAnalyzer.HasMissingMembers(typeSymbol))
         {
            throw new ToolException(
               ErrorCodes.NotSupported,
               $"Type '{RoslynHelpers.GetName(typeSymbol)}' already implements all required members");
         }

         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            $"No 'Implement interface' or 'Implement abstract' action available for '{RoslynHelpers.GetName(typeSymbol)}'",
            "Ensure the cursor is on the type declaration, not inside a member");
      }

      // Apply the code action
      var selectedAction = matchingAction.action;
      var operations = await selectedAction.GetOperationsAsync(cancellationToken);
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
         throw new ToolException(ErrorCodes.AnalysisFailed, "Code action did not produce any changes");
      }

      // When applying, we need the text content for writing to files
      var changedFiles = await RefactoringHelpers.GetChangedFilesAsync(
         solution, changedSolution, includeText: true, cancellationToken);

      if (!preview)
      {
         if (!_workspaceManager.ApplyChanges(changedSolution))
         {
            throw new ToolException(
               ErrorCodes.ApplyChangesFailed,
               "Failed to apply implement members changes to workspace");
         }
      }

      return ToolResult.Create(
         new ApplyCodeActionResponse
         {
            Applied = !preview,
            ActionTitle = selectedAction.Title,
            ChangedFiles = changedFiles.Select(f => new ChangedFileInfo
            {
               FilePath = _workspaceManager.FormatPath(f.FilePath) ?? f.FilePath ?? "",
               ChangeCount = f.ChangeCount,
               NewText = preview ? f.NewText : null
            }).ToList(),
            Preview = preview
         },
         preview ? PreviewHints : AppliedHints,
         changedFiles.Count,
         changedFiles.Count);
   }

}
