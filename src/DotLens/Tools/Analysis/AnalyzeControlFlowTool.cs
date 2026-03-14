using System.ComponentModel;
using System.Text.Json.Serialization;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Analysis;

#region Input Types

/// <summary>
/// Polymorphic target for control flow analysis.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "mode")]
[JsonDerivedType(typeof(ControlFlowTargetByMethod), "method")]
[JsonDerivedType(typeof(ControlFlowTargetByLines), "lines")]
[Description("What to analyze: a method (mode=method) or a line range (mode=lines).")]
public abstract class ControlFlowTarget { }

/// <summary>
/// Analyze the entire body of a method.
/// </summary>
[Description("Analyze the entire body of a method.")]
public class ControlFlowTargetByMethod : ControlFlowTarget
{
   [Description("Method to analyze. Accepts 'TypeName.MethodName' or position lookup.")]
   public required SymbolLookup Method { get; init; }
}

/// <summary>
/// Analyze a specific line range within a file.
/// </summary>
[Description("Analyze a specific line range within a file.")]
public class ControlFlowTargetByLines : ControlFlowTarget
{
   [Description("File path (absolute or solution-relative).")]
   public required string FilePath { get; init; }

   [Description("Start line number.")]
   [PositiveInteger]
   public required int StartLine { get; init; }

   [Description("End line number (inclusive).")]
   [PositiveInteger]
   public required int EndLine { get; init; }
}

#endregion

#region Result Types

/// <summary>
/// A control flow point (entry/exit/return statement).
/// </summary>
public record ControlFlowPoint
{
   [Description("Line number (1-based) where this control flow point occurs.")]
   public required int Line { get; init; }

   [Description("Kind of syntax node (e.g., ReturnStatement, BreakStatement, ThrowStatement).")]
   public required string Kind { get; init; }

   [Description("Source text of the statement (for return statements).")]
   public string? Text { get; init; }
}

/// <summary>
/// Result from AnalyzeControlFlow tool.
/// </summary>
public record ControlFlowResponse
{
   [Description("The analyzed line range (1-based). May differ from input if lines were adjusted.")]
   public required LineRange Region { get; init; }

   [Description("Entry points into the analyzed region (e.g., goto targets, labels).")]
   public required IReadOnlyList<ControlFlowPoint> EntryPoints { get; init; }

   [Description("Exit points from the analyzed region (break, continue, goto, return, throw).")]
   public required IReadOnlyList<ControlFlowPoint> ExitPoints { get; init; }

   [Description("Return statements within the region.")]
   public required IReadOnlyList<ControlFlowPoint> ReturnStatements { get; init; }

   [Description("True if execution can reach the end of the region without returning/throwing.")]
   public required bool EndPointIsReachable { get; init; }

   [Description("True if execution can reach the start of the region (not dead code).")]
   public required bool StartPointIsReachable { get; init; }
}

#endregion

/// <summary>
/// Analyze branching and reachability in a code region.
/// </summary>
[McpServerToolType]
[DotLensWorkerTool]
public class AnalyzeControlFlowTool
{
   private static readonly string[] SuggestedNextTools = ["analyze_data_flow to analyze variable usage in the same region"];

   private readonly IReadOnlyWorkspace _workspaceManager;
   private readonly SymbolLookupService _symbolLookup;

   public AnalyzeControlFlowTool(IReadOnlyWorkspace workspaceManager, SymbolLookupService symbolLookup)
   {
      _workspaceManager = workspaceManager;
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "analyze_control_flow", ReadOnly = true, OpenWorld = false)]
   [Description("Analyze branching and reachability in a method or line range. Line range must span complete statements within a single block.")]
   public async Task<ToolResult<ControlFlowResponse>> AnalyzeControlFlowAsync(
      [Description("What to analyze: a method (mode=method) or a line range (mode=lines).")] ControlFlowTarget target,
      CancellationToken cancellationToken = default)
   {
      return target switch
      {
         ControlFlowTargetByMethod byMethod => await AnalyzeMethodBodyAsync(byMethod.Method, cancellationToken),
         ControlFlowTargetByLines byLines => await AnalyzeLineRangeAsync(byLines.FilePath, byLines.StartLine, byLines.EndLine, cancellationToken),
         _ => throw new ToolException(ErrorCodes.InvalidParameter, $"Unknown target type: {target.GetType().Name}")
      };
   }

   private async Task<ToolResult<ControlFlowResponse>> AnalyzeMethodBodyAsync(
      SymbolLookup target,
      CancellationToken cancellationToken)
   {
      var solution = _workspaceManager.EnsureSolutionLoaded();

      // Find the method
      var lookupResult = await _symbolLookup.FindSingleAsync<IMethodSymbol>(target, cancellationToken: cancellationToken);

      if (lookupResult is not SourceSymbolResult<IMethodSymbol> sourceResult)
      {
         throw new ToolException(
            ErrorCodes.SymbolInMetadata,
            "Cannot analyze control flow for metadata symbol (no source code available)");
      }

      var methodSymbol = sourceResult.Symbol;
      var syntaxRef = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
      if (syntaxRef == null)
      {
         throw new ToolException(
            ErrorCodes.SymbolNotFound,
            "Method has no syntax reference");
      }

      var methodSyntax = await syntaxRef.GetSyntaxAsync(cancellationToken);
      var body = AnalysisHelpers.GetMethodBody(methodSyntax);

      if (body == null || body.Statements.Count == 0)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            "Method has no block body or body is empty (may be expression-bodied)",
            "Use line-based input for expression-bodied members");
      }

      var document = solution.GetDocument(syntaxRef.SyntaxTree);
      if (document == null)
      {
         throw new ToolException(
            ErrorCodes.FileNotInSolution,
            "Could not find document for method");
      }

      var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
      if (semanticModel == null)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            "Could not get semantic model");
      }

      var firstStatement = body.Statements.First();
      var lastStatement = body.Statements.Last();

      ControlFlowAnalysis? controlFlow;
      try
      {
         controlFlow = semanticModel.AnalyzeControlFlow(firstStatement, lastStatement);
      }
      catch (Exception ex)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            $"Control flow analysis failed: {ex.Message}");
      }

      if (controlFlow == null || !controlFlow.Succeeded)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            "Control flow analysis did not succeed");
      }

      // Get line numbers (1-based) for the actual analyzed statements
      var text = await syntaxRef.SyntaxTree.GetTextAsync(cancellationToken);
      var startLine = text.Lines.GetLinePosition(firstStatement.SpanStart).Line + 1;
      var endLine = text.Lines.GetLinePosition(lastStatement.Span.End).Line + 1;

      return ToolResult.Create(
         CreateResponse(controlFlow, startLine, endLine),
         SuggestedNextTools);
   }

   private async Task<ToolResult<ControlFlowResponse>> AnalyzeLineRangeAsync(
      string filePath,
      int startLine,
      int endLine,
      CancellationToken cancellationToken)
   {
      _ = _workspaceManager.EnsureSolutionLoaded();

      var document = await _workspaceManager.GetDocumentAsync(filePath, cancellationToken);

      var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
      var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);

      if (semanticModel == null || syntaxTree == null)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            "Could not get semantic model or syntax tree");
      }

      var root = await syntaxTree.GetRootAsync(cancellationToken);
      var text = await syntaxTree.GetTextAsync(cancellationToken);
      var (span, line0, line1) = AnalysisHelpers.NormalizeLineRange(text, startLine, endLine);

      // Find the enclosing block that contains the selected range
      var node = root.FindNode(span);
      var enclosingBlock = node.AncestorsAndSelf().OfType<BlockSyntax>().FirstOrDefault()
         ?? node.DescendantNodesAndSelf().OfType<BlockSyntax>().FirstOrDefault();

      if (enclosingBlock == null)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            "No block found containing the specified region",
            "Ensure the line range is within a method or property body");
      }

      // Get only direct child statements of the enclosing block that overlap with the selected range
      // This avoids mixing statements from different nested blocks (which causes "statements not within the same statement list")
      var statements = enclosingBlock.Statements
         .Where(s => s.Span.IntersectsWith(span))
         .ToList();

      if (statements.Count == 0)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            "No statements found in the specified region",
            "Ensure the line range contains executable statements within the same block");
      }

      var firstStatement = statements.First();
      var lastStatement = statements.Last();

      ControlFlowAnalysis? controlFlow;
      try
      {
         controlFlow = semanticModel.AnalyzeControlFlow(firstStatement, lastStatement);
      }
      catch (Exception ex)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            $"Control flow analysis failed: {ex.Message}",
            "Try selecting a different code region or use method mode to analyze entire method");
      }

      if (controlFlow == null || !controlFlow.Succeeded)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            "Control flow analysis did not succeed");
      }

      // Return adjusted lines (1-based)
      return ToolResult.Create(
         CreateResponse(controlFlow, line0 + 1, line1 + 1),
         SuggestedNextTools);
   }

   private static ControlFlowResponse CreateResponse(ControlFlowAnalysis controlFlow, int startLine, int endLine)
   {
      var entryPoints = controlFlow.EntryPoints.Select(ep =>
      {
         var lineSpan = ep.GetLocation().GetLineSpan();
         return new ControlFlowPoint { Line = lineSpan.StartLinePosition.Line + 1, Kind = ep.Kind().ToString() };
      }).ToList();

      var exitPoints = controlFlow.ExitPoints.Select(ep =>
      {
         var lineSpan = ep.GetLocation().GetLineSpan();
         return new ControlFlowPoint { Line = lineSpan.StartLinePosition.Line + 1, Kind = ep.Kind().ToString() };
      }).ToList();

      var returnStatements = controlFlow.ReturnStatements.Select(rs =>
      {
         var lineSpan = rs.GetLocation().GetLineSpan();
         return new ControlFlowPoint { Line = lineSpan.StartLinePosition.Line + 1, Kind = "ReturnStatement", Text = rs.ToString().Trim() };
      }).ToList();

      return new ControlFlowResponse
      {
         Region = new LineRange { StartLine = startLine, EndLine = endLine },
         EntryPoints = entryPoints,
         ExitPoints = exitPoints,
         ReturnStatements = returnStatements,
         EndPointIsReachable = controlFlow.EndPointIsReachable,
         StartPointIsReachable = controlFlow.StartPointIsReachable
      };
   }
}
