using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using ModelContextProtocol.Server;

namespace DotLens.Tools.Intelligence;

[McpServerToolType]
[DotLensWorkerTool]
public class GetFileOverviewTool
{
   private readonly IReadOnlyWorkspace _workspace;

   public GetFileOverviewTool(IReadOnlyWorkspace workspace)
   {
      _workspace = workspace;
   }

   [McpServerTool(Name = "get_file_overview", ReadOnly = true, OpenWorld = false)]
   [Description("Get file metadata and health: namespace, line count, type list, and diagnostic counts. Use get_file_outline for detailed structure.")]
   public async Task<ToolResult<FileOverviewResponse>> GetFileOverviewAsync(
      [Description("File path (absolute or solution-relative).")] string filePath,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      CancellationToken cancellationToken = default)
   {
      var document = await _workspace.GetDocumentAsync(filePath, cancellationToken);

      var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
      var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

      if (syntaxTree == null || semanticModel == null)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            "Could not analyze file");
      }

      var root = await syntaxTree.GetRootAsync(cancellationToken);
      var text = await syntaxTree.GetTextAsync(cancellationToken);

      // Single pass through descendant nodes to collect all information
      BaseNamespaceDeclarationSyntax? namespaceDecl = null;
      var types = new List<SymbolResponse>();
      var typeCount = 0;
      var usingCount = 0;

      foreach (var node in root.DescendantNodes())
      {
         switch (node)
         {
            case BaseNamespaceDeclarationSyntax ns when namespaceDecl == null:
               namespaceDecl = ns;
               break;

            // Enums are not TypeDeclarationSyntax, and delegates are not BaseTypeDeclarationSyntax.
            case BaseTypeDeclarationSyntax or DelegateDeclarationSyntax:
               typeCount++;
               if (verbosity != ToolResultVerbosity.Summary)
               {
                  var symbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
                  if (symbol != null)
                  {
                     types.Add(await RoslynHelpers.CreateSymbolResponseAsync(
                        symbol,
                        verbosity,
                        document.Project.Solution,
                        _workspace.SolutionPath,
                        _workspace.UseAbsolutePaths,
                        cancellationToken));
                  }
               }
               break;

            case UsingDirectiveSyntax:
               usingCount++;
               break;
         }
      }

      var diagnostics = semanticModel.GetDiagnostics(cancellationToken: cancellationToken);
      var errorCount = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
      var warningCount = diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);

      // Build context-aware suggested next tools
      var suggestions = new List<string>();
      if (errorCount > 0 || warningCount > 0)
         suggestions.Add("get_diagnostics for full error details");
      if (typeCount > 0)
         suggestions.Add("get_file_outline for detailed structure with member signatures");
      suggestions.Add("get_method_source to read specific method implementations");

      var formattedPath = document.FilePath != null
         ? _workspace.FormatPath(document.FilePath)
         : filePath;

      return ToolResult.Create(
         new FileOverviewResponse
         {
            FilePath = formattedPath,
            FileName = document.Name,
            Namespace = namespaceDecl?.Name.ToString(),
            LineCount = text.Lines.Count,
            TypeCount = verbosity == ToolResultVerbosity.Summary ? typeCount : null,
            Types = verbosity == ToolResultVerbosity.Summary ? null : types,
            DiagnosticsSummary = new DiagnosticsSummary { ErrorCount = errorCount, WarningCount = warningCount },
            UsingCount = usingCount
         },
         suggestions.ToArray(),
         typeCount > 0 ? typeCount : null,
         typeCount > 0 ? typeCount : null);
   }
}

/// <summary>
/// Result for GetFileOverviewTool.
/// </summary>
public record FileOverviewResponse
{
   [Description("Absolute or relative path to the file.")]
   public required string FilePath { get; init; }

   [Description("File name without path.")]
   public required string FileName { get; init; }

   [Description("Primary namespace declared in the file.")]
   public string? Namespace { get; init; }

   [Description("Total lines in the file.")]
   public required int LineCount { get; init; }

   [Description("Number of type declarations (summary verbosity only).")]
   public int? TypeCount { get; init; }

   [Description("Type declarations with location info (compact/full verbosity).")]
   public IReadOnlyList<SymbolResponse>? Types { get; init; }

   [Description("Error and warning counts.")]
   public required DiagnosticsSummary DiagnosticsSummary { get; init; }

   [Description("Number of using directives.")]
   public required int UsingCount { get; init; }
}

public record DiagnosticsSummary
{
   [Description("Number of compiler errors.")]
   public required int ErrorCount { get; init; }

   [Description("Number of compiler warnings.")]
   public required int WarningCount { get; init; }
}
