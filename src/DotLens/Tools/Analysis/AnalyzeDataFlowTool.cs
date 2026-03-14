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
/// Polymorphic target for data flow analysis.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "mode")]
[JsonDerivedType(typeof(DataFlowTargetByMethod), "method")]
[JsonDerivedType(typeof(DataFlowTargetByLines), "lines")]
[Description("What to analyze: a method (mode=method) or a line range (mode=lines).")]
public abstract class DataFlowTarget { }

/// <summary>
/// Analyze the entire body of a method.
/// </summary>
[Description("Analyze the entire body of a method.")]
public class DataFlowTargetByMethod : DataFlowTarget
{
   [Description("Method to analyze. Accepts 'TypeName.MethodName' or position lookup.")]
   public required SymbolLookup Method { get; set; }
}

/// <summary>
/// Analyze a specific line range within a file.
/// </summary>
[Description("Analyze a specific line range within a file.")]
public class DataFlowTargetByLines : DataFlowTarget
{
   [Description("File path (absolute or solution-relative).")]
   public required string FilePath { get; set; }

   [Description("Start line number.")]
   [PositiveInteger]
   public required int StartLine { get; set; }

   [Description("End line number (inclusive).")]
   [PositiveInteger]
   public required int EndLine { get; set; }
}

#endregion

#region Result Types

/// <summary>
/// Result from AnalyzeDataFlow tool.
/// </summary>
public record DataFlowResponse
{
   [Description("The analyzed line range (1-based). May differ from input if lines were swapped or clamped.")]
   public required LineRange Region { get; init; }

   [Description("Variables declared within the analyzed region.")]
   public required IReadOnlyList<string> VariablesDeclared { get; init; }

   [Description("Variables that are always assigned a value within the region (on all code paths).")]
   public required IReadOnlyList<string> AlwaysAssigned { get; init; }

   [Description("Variables that flow into the region (read before being written, value comes from outside).")]
   public required IReadOnlyList<string> DataFlowsIn { get; init; }

   [Description("Variables that flow out of the region (assigned inside and read outside).")]
   public required IReadOnlyList<string> DataFlowsOut { get; init; }

   [Description("Variables read inside the region.")]
   public required IReadOnlyList<string> ReadInside { get; init; }

   [Description("Variables written inside the region.")]
   public required IReadOnlyList<string> WrittenInside { get; init; }

   [Description("Variables read outside the region (in the containing method).")]
   public required IReadOnlyList<string> ReadOutside { get; init; }

   [Description("Variables written outside the region (in the containing method).")]
   public required IReadOnlyList<string> WrittenOutside { get; init; }

   [Description("Variables captured by lambdas or local functions within the region.")]
   public required IReadOnlyList<string> Captured { get; init; }
}

#endregion

/// <summary>
/// Analyze variable assignments and usage in a code region.
/// </summary>
[McpServerToolType]
[DotLensWorkerTool]
public class AnalyzeDataFlowTool
{
   private static readonly string[] SuggestedNextTools = ["analyze_control_flow to analyze branching in the same region"];

   private readonly IReadOnlyWorkspace _workspaceManager;
   private readonly SymbolLookupService _symbolLookup;

   public AnalyzeDataFlowTool(IReadOnlyWorkspace workspaceManager, SymbolLookupService symbolLookup)
   {
      _workspaceManager = workspaceManager;
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "analyze_data_flow", ReadOnly = true, OpenWorld = false)]
   [Description("Analyze variable assignments and usage in a method or line range. Line range must span complete statements within a single block.")]
   public async Task<ToolResult<DataFlowResponse>> AnalyzeDataFlowAsync(
      [Description("What to analyze: a method (mode=method) or a line range (mode=lines).")] DataFlowTarget target,
      CancellationToken cancellationToken = default)
   {
      return target switch
      {
         DataFlowTargetByMethod byMethod => await AnalyzeMethodBodyAsync(byMethod.Method, cancellationToken),
         DataFlowTargetByLines byLines => await AnalyzeLineRangeAsync(byLines.FilePath, byLines.StartLine, byLines.EndLine, cancellationToken),
         _ => throw new ToolException(ErrorCodes.InvalidParameter, $"Unknown target type: {target.GetType().Name}")
      };
   }

   private async Task<ToolResult<DataFlowResponse>> AnalyzeMethodBodyAsync(
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
            "Cannot analyze data flow for metadata symbol (no source code available)");
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

      DataFlowAnalysis? dataFlow;
      try
      {
         dataFlow = firstStatement == lastStatement
            ? semanticModel.AnalyzeDataFlow(firstStatement)
            : semanticModel.AnalyzeDataFlow(firstStatement, lastStatement);
      }
      catch (Exception ex)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            $"Data flow analysis failed: {ex.Message}");
      }

      if (dataFlow == null || !dataFlow.Succeeded)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            "Data flow analysis did not succeed");
      }

      // Get line numbers (1-based) for the actual analyzed statements
      var text = await syntaxRef.SyntaxTree.GetTextAsync(cancellationToken);
      var startLine = text.Lines.GetLinePosition(firstStatement.SpanStart).Line + 1;
      var endLine = text.Lines.GetLinePosition(lastStatement.Span.End).Line + 1;

      return ToolResult.Create(
         CreateResponse(dataFlow, startLine, endLine),
         SuggestedNextTools);
   }

   private async Task<ToolResult<DataFlowResponse>> AnalyzeLineRangeAsync(
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

      DataFlowAnalysis? dataFlow;
      try
      {
         dataFlow = firstStatement == lastStatement
            ? semanticModel.AnalyzeDataFlow(firstStatement)
            : semanticModel.AnalyzeDataFlow(firstStatement, lastStatement);
      }
      catch (Exception ex)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            $"Data flow analysis failed: {ex.Message}",
            "Try selecting a different code region or use method mode to analyze entire method");
      }

      if (dataFlow == null || !dataFlow.Succeeded)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            "Data flow analysis did not succeed");
      }

      // Return adjusted lines (1-based)
      return ToolResult.Create(
         CreateResponse(dataFlow, line0 + 1, line1 + 1),
         SuggestedNextTools);
   }

   private static DataFlowResponse CreateResponse(DataFlowAnalysis dataFlow, int startLine, int endLine)
   {
      return new DataFlowResponse
      {
         Region = new LineRange { StartLine = startLine, EndLine = endLine },
         VariablesDeclared = dataFlow.VariablesDeclared.Select(s => s.Name).ToList(),
         AlwaysAssigned = dataFlow.AlwaysAssigned.Select(s => s.Name).ToList(),
         DataFlowsIn = dataFlow.DataFlowsIn.Select(s => s.Name).ToList(),
         DataFlowsOut = dataFlow.DataFlowsOut.Select(s => s.Name).ToList(),
         ReadInside = dataFlow.ReadInside.Select(s => s.Name).ToList(),
         WrittenInside = dataFlow.WrittenInside.Select(s => s.Name).ToList(),
         ReadOutside = dataFlow.ReadOutside.Select(s => s.Name).ToList(),
         WrittenOutside = dataFlow.WrittenOutside.Select(s => s.Name).ToList(),
         Captured = dataFlow.Captured.Select(s => s.Name).ToList()
      };
   }
}
