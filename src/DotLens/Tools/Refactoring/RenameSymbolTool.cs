using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
namespace DotLens.Tools.Refactoring;

[McpServerToolType]
[DotLensWorkerTool]
public class RenameSymbolTool
{
   private const int MaxChangesPerFileInPreview = 20;
   private static readonly string[] PreviewHints = ["rename_symbol with preview=false to apply changes"];
   private static readonly string[] AppliedHints = ["get_diagnostics to verify no issues after rename"];

   private readonly IWorkspace _workspace;
   private readonly SymbolLookupService _symbolLookup;

   public RenameSymbolTool(IWorkspace workspace, SymbolLookupService symbolLookup)
   {
      _workspace = workspace;
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "rename_symbol", ReadOnly = false, Destructive = true, OpenWorld = false)]
   [Description("Rename a symbol across the entire solution using Roslyn's semantic analysis.")]
   public async Task<ToolResult<RenameSymbolResponse>> RenameSymbolAsync(
      [Description("Symbol to rename.")] SymbolLookup target,
      [Description("New name for the symbol.")] string newName,
      [Description("Preview changes without applying. Always preview first.")]
      bool preview = true,
      [Description("Max files to show in preview.")]
      [PositiveInteger]
      int maxFiles = 20,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      CancellationToken cancellationToken = default)
   {
      using var writeLock = await _workspace.WriteLock.AcquireAsync(cancellationToken);

      var solution = _workspace.EnsureSolutionLoaded();

      // Find the symbol using the unified lookup service (requires source symbol for refactoring)
      var lookupResult = await _symbolLookup.FindSingleSourceSymbolAsync<ISymbol>(target, cancellationToken);
      var symbol = lookupResult.Symbol;

      if (symbol is INamedTypeSymbol { IsExtension: true } extension)
      {
         throw new ToolException(
            ErrorCodes.NotSupported,
            $"{RoslynHelpers.GetExtensionBlockName(extension)} is an extension block, which has no name to rename. Rename its receiver parameter or its members instead.");
      }

      if (symbol is IMethodSymbol
         {
            MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove,
            AssociatedSymbol: { } owner,
         } accessor)
      {
         throw new ToolException(
            ErrorCodes.NotSupported,
            $"{accessor.Name} is an accessor of {RoslynHelpers.GetName(owner)}, which takes its name from it. Rename {RoslynHelpers.GetName(owner)} instead.");
      }

      // Validate new name
      if (string.IsNullOrWhiteSpace(newName))
      {
         throw new ToolException(
            ErrorCodes.InvalidParameter,
            "New name cannot be empty");
      }

      if (!Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(newName))
      {
         throw new ToolException(
            ErrorCodes.InvalidParameter,
            $"'{newName}' is not a valid C# identifier");
      }

      // Perform the rename
      var renameOptions = new Microsoft.CodeAnalysis.Rename.SymbolRenameOptions();
      var newSolution = await Microsoft.CodeAnalysis.Rename.Renamer.RenameSymbolAsync(
         solution,
         symbol,
         renameOptions,
         newName,
         cancellationToken);

      // Collect changes
      var changes = new List<RenameFileChange>();
      var solutionChanges = newSolution.GetChanges(solution);
      var totalFiles = 0;
      var totalChanges = 0;

      foreach (var projectChanges in solutionChanges.GetProjectChanges())
      {
         cancellationToken.ThrowIfCancellationRequested();
         foreach (var changedDocumentId in projectChanges.GetChangedDocuments())
         {
            cancellationToken.ThrowIfCancellationRequested();

            var oldDocument = solution.GetDocument(changedDocumentId);
            var newDocument = newSolution.GetDocument(changedDocumentId);
            if (oldDocument == null || newDocument == null)
               continue;

            totalFiles++;
            var oldText = await oldDocument.GetTextAsync(cancellationToken);
            var newText = await newDocument.GetTextAsync(cancellationToken);
            var textChanges = newText.GetTextChanges(oldText).ToList();
            totalChanges += textChanges.Count;

            if (changes.Count < maxFiles)
            {
               changes.Add(CreateFileChange(oldDocument, oldText, textChanges, verbosity));
            }
         }
      }

      if (preview)
      {
         return ToolResult.Create(
            new RenameSymbolResponse
            {
               SymbolName = symbol.Name,
               SymbolKind = symbol.ToSymbolKindDto(),
               NewName = newName,
               Verbosity = verbosity,
               Changes = changes,
               Preview = true,
               Applied = false,
               TotalFiles = totalFiles,
               FilesShown = changes.Count
            },
            PreviewHints,
            totalFiles,
            changes.Count);
      }

      // Apply changes to workspace (writes files and updates in-memory solution)
      if (!_workspace.ApplyChanges(newSolution, cancellationToken))
      {
         throw new ToolException(
            ErrorCodes.ApplyChangesFailed,
            "Failed to apply rename changes to workspace");
      }

      return ToolResult.Create(
         new RenameSymbolResponse
         {
            SymbolName = symbol.Name,
            SymbolKind = symbol.ToSymbolKindDto(),
            NewName = newName,
            Verbosity = verbosity,
            Changes = changes,
            Preview = false,
            Applied = true,
            TotalFiles = totalFiles,
            FilesShown = changes.Count
         },
         AppliedHints,
         totalFiles,
         changes.Count);
   }

   private RenameFileChange CreateFileChange(
      Document document,
      Microsoft.CodeAnalysis.Text.SourceText oldText,
      List<Microsoft.CodeAnalysis.Text.TextChange> textChanges,
      ToolResultVerbosity verbosity)
   {
      if (verbosity == ToolResultVerbosity.Summary)
      {
         return new RenameFileChange
         {
            FilePath = _workspace.FormatPath(document.FilePath),
            ChangeCount = textChanges.Count
         };
      }

      var documentChanges = textChanges.Take(MaxChangesPerFileInPreview).Select(tc =>
      {
         var lineSpan = oldText.Lines.GetLinePositionSpan(tc.Span);

         if (verbosity == ToolResultVerbosity.Compact)
         {
            return new RenameChangeLocation
            {
               Line = lineSpan.Start.Line + 1,
               Column = lineSpan.Start.Character + 1
            };
         }

         // Full verbosity
         return new RenameChangeLocation
         {
            Line = lineSpan.Start.Line + 1,
            Column = lineSpan.Start.Character + 1,
            StartLine = lineSpan.Start.Line + 1,
            StartColumn = lineSpan.Start.Character + 1,
            EndLine = lineSpan.End.Line + 1,
            EndColumn = lineSpan.End.Character + 1,
            OldText = tc.Span.Length > 0 ? oldText.ToString(tc.Span) : "",
            NewText = tc.NewText
         };
      }).ToList();

      return new RenameFileChange
      {
         FilePath = _workspace.FormatPath(document.FilePath),
         ChangeCount = textChanges.Count,
         Changes = documentChanges,
         Truncated = textChanges.Count > MaxChangesPerFileInPreview
      };
   }
}

#region Result Types

/// <summary>
/// Result for RenameSymbolTool.
/// </summary>
public record RenameSymbolResponse
{
   [Description("Original name of the symbol.")]
   public required string SymbolName { get; init; }

   [Description("Kind of the symbol (class, method, property, etc.).")]
   public required SymbolKindDto SymbolKind { get; init; }

   [Description("New name for the symbol.")]
   public required string NewName { get; init; }

   [Description("Response detail level used.")]
   public ToolResultVerbosity? Verbosity { get; init; }

   [Description("Files affected by the rename.")]
   public required IReadOnlyList<RenameFileChange> Changes { get; init; }

   [Description("Whether this is a preview (changes not applied).")]
   public required bool Preview { get; init; }

   [Description("Whether changes were applied to files.")]
   public required bool Applied { get; init; }

   [Description("Total number of files affected.")]
   public required int TotalFiles { get; init; }

   [Description("Number of files included in Changes (may be limited by maxFiles).")]
   public int? FilesShown { get; init; }
}

/// <summary>
/// File change info for rename operation.
/// </summary>
public record RenameFileChange
{
   [Description("File path affected by the rename.")]
   public required string FilePath { get; init; }

   [Description("Number of text changes in this file.")]
   public required int ChangeCount { get; init; }

   [Description("Individual change locations. Only included at compact/full verbosity.")]
   public IReadOnlyList<RenameChangeLocation>? Changes { get; init; }

   [Description("Whether the changes list was truncated.")]
   public bool? Truncated { get; init; }
}

/// <summary>
/// Individual change location within a file.
/// </summary>
public record RenameChangeLocation
{
   [Description("Line number.")]
   public required int Line { get; init; }

   [Description("Column number.")]
   public required int Column { get; init; }

   [Description("Start line of the change span. Only at full verbosity.")]
   public int? StartLine { get; init; }

   [Description("Start column of the change span. Only at full verbosity.")]
   public int? StartColumn { get; init; }

   [Description("End line of the change span. Only at full verbosity.")]
   public int? EndLine { get; init; }

   [Description("End column of the change span. Only at full verbosity.")]
   public int? EndColumn { get; init; }

   [Description("Original text being replaced. Only at full verbosity.")]
   public string? OldText { get; init; }

   [Description("New text after rename. Only at full verbosity.")]
   public string? NewText { get; init; }
}

#endregion
