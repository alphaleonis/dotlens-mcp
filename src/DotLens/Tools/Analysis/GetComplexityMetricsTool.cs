using System.ComponentModel;
using System.Text.Json.Serialization;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelContextProtocol.Server;
namespace DotLens.Tools.Analysis;

#region Input Types

/// <summary>
/// What to analyze: a file (all methods) or a specific method.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "mode")]
[JsonDerivedType(typeof(ComplexityTargetFile), "file")]
[JsonDerivedType(typeof(ComplexityTargetMethod), "method")]
[Description("Target for complexity analysis.")]
public abstract class ComplexityTarget { }

/// <summary>
/// Analyze all methods in a file.
/// </summary>
[Description("Analyze all methods in a file.")]
public class ComplexityTargetFile : ComplexityTarget
{
   [Description("File path (absolute or solution-relative).")]
   public required string FilePath { get; init; }
}

/// <summary>
/// Analyze a specific method.
/// </summary>
[Description("Analyze a specific method.")]
public class ComplexityTargetMethod : ComplexityTarget
{
   [Description("Method to analyze. Accepts 'TypeName.MethodName' or position lookup.")]
   public required SymbolLookup Method { get; init; }
}

#endregion

#region Enums

/// <summary>
/// Complexity metric types that can be calculated for methods.
/// Global camelCase converter handles serialization (Cyclomatic → "cyclomatic").
/// </summary>
public enum MetricType
{
   /// <summary>Cyclomatic complexity - counts decision points.</summary>
   Cyclomatic,
   /// <summary>Maximum nesting depth of control structures.</summary>
   Nesting,
   /// <summary>Lines of code (unique lines containing tokens).</summary>
   Loc,
   /// <summary>Number of parameters in the method signature.</summary>
   Parameters,
   /// <summary>Cognitive complexity (Sonar-style).</summary>
   Cognitive
}

/// <summary>
/// Sort options for complexity metrics results.
/// Global camelCase converter handles serialization (Position → "position").
/// </summary>
public enum ComplexitySortBy
{
   /// <summary>Sort by method position in file (default).</summary>
   Position,
   /// <summary>Sort by cyclomatic complexity (highest first).</summary>
   Cyclomatic,
   /// <summary>Sort by cognitive complexity (highest first).</summary>
   Cognitive,
   /// <summary>Sort by nesting depth (highest first).</summary>
   Nesting,
   /// <summary>Sort by lines of code (highest first).</summary>
   Loc
}

#endregion

/// <summary>
/// Get complexity metrics for a method or entire file.
/// </summary>
[McpServerToolType]
[DotLensWorkerTool]
public class GetComplexityMetricsTool
{
   private static readonly string[] SuggestedNextTools =
   [
      "get_method_source to view complex method code",
      "analyze_method for callers/callees of complex methods"
   ];

   private readonly IReadOnlyWorkspace _workspaceManager;
   private readonly SymbolLookupService _symbolLookup;

   public GetComplexityMetricsTool(IReadOnlyWorkspace workspaceManager, SymbolLookupService symbolLookup)
   {
      _workspaceManager = workspaceManager;
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "get_complexity_metrics", ReadOnly = true, OpenWorld = false)]
   [Description("Get complexity metrics for methods. Use sortBy and threshold to identify problem areas.")]
   public async Task<ToolResult<IComplexityResponse>> GetComplexityMetricsAsync(
      [Description("Target for complexity analysis.")] ComplexityTarget target,
      [Description("Metrics to calculate (default: all).")]
      List<MetricType>? metrics = null,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      [Description("Sort methods by metric (for file analysis).")]
      ComplexitySortBy sortBy = ComplexitySortBy.Position,
      [Description("Only return methods exceeding this cyclomatic complexity threshold.")]
      [PositiveInteger]
      int? threshold = null,
      [Description("Maximum methods to return (for file analysis).")]
      [PositiveInteger]
      int maxMethods = 100,
      CancellationToken cancellationToken = default)
   {
      var requestedMetrics = metrics ?? [MetricType.Cyclomatic, MetricType.Nesting, MetricType.Loc, MetricType.Parameters, MetricType.Cognitive];

      // Threshold filtering requires Cyclomatic metric - ensure it's included
      if (threshold.HasValue && !requestedMetrics.Contains(MetricType.Cyclomatic))
      {
         requestedMetrics = [MetricType.Cyclomatic, .. requestedMetrics];
      }

      return target switch
      {
         ComplexityTargetFile file => await AnalyzeFileAsync(file.FilePath, requestedMetrics, verbosity, sortBy, threshold, maxMethods, cancellationToken),
         ComplexityTargetMethod method => await AnalyzeMethodAsync(method.Method, requestedMetrics, verbosity, cancellationToken),
         _ => throw new ToolException(ErrorCodes.InvalidParameter, $"Unknown target type: {target.GetType().Name}")
      };
   }

   private async Task<ToolResult<IComplexityResponse>> AnalyzeMethodAsync(
      SymbolLookup target,
      List<MetricType> requestedMetrics,
      ToolResultVerbosity verbosity,
      CancellationToken cancellationToken)
   {
      _workspaceManager.EnsureSolutionLoaded();

      var lookupResult = await _symbolLookup.FindSingleAsync<IMethodSymbol>(
         target,
         m => m.IsCallableMethod(),
         cancellationToken);

      if (lookupResult is not SourceSymbolResult<IMethodSymbol> sourceResult)
      {
         throw new ToolException(
            ErrorCodes.SymbolInMetadata,
            "Cannot analyze complexity for metadata symbol (no source code available)");
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
      var methodMetrics = CalculateMetrics(methodSyntax, requestedMetrics);

      // Include location for Full verbosity
      SymbolLocation? location = null;
      if (verbosity == ToolResultVerbosity.Full)
      {
         var lineSpan = methodSyntax.GetLocation().GetLineSpan();
         location = lineSpan.ToSymbolLocation(_workspaceManager.FormatPath);
      }

      return ToolResult.Create<IComplexityResponse>(
         new MethodComplexityResponse
         {
            Name = methodSymbol.Name,
            ContainingType = methodSymbol.ContainingType is { } containingType ? RoslynHelpers.GetName(containingType) : null,
            Metrics = methodMetrics,
            Location = location
         },
         SuggestedNextTools);
   }

   private async Task<ToolResult<IComplexityResponse>> AnalyzeFileAsync(
      string filePath,
      List<MetricType> requestedMetrics,
      ToolResultVerbosity verbosity,
      ComplexitySortBy sortBy,
      int? threshold,
      int maxMethods,
      CancellationToken cancellationToken)
   {
      var document = await _workspaceManager.GetDocumentAsync(filePath, cancellationToken);

      var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
      var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

      if (syntaxTree == null || semanticModel == null)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            "Could not analyze file");
      }

      var root = await syntaxTree.GetRootAsync(cancellationToken);

      // Calculate metrics for all callable methods (methods, constructors, operators, etc.)
      var allMethods = root.DescendantNodes()
         .Where(n => n.IsCallableMethodSyntax())
         .Select(node =>
         {
            var symbol = semanticModel.GetDeclaredSymbol(node) as IMethodSymbol;
            var methodMetrics = CalculateMetrics(node, requestedMetrics);

            // Get name from symbol, or fallback to extracting from syntax
            var name = symbol?.Name ?? GetMethodName(node);
            var containingType = symbol?.ContainingType is { } type ? RoslynHelpers.GetName(type) : null;
            var line = node.GetLineNumber();

            return new MethodMetrics
            {
               Name = name,
               ContainingType = containingType,
               Line = line,
               Metrics = methodMetrics
            };
         })
         .ToList();

      var totalMethodCount = allMethods.Count;

      // Calculate file-level totals before any filtering (represents entire file)
      var fileTotals = CalculateFileTotals(allMethods, requestedMetrics);

      // Apply threshold filter
      if (threshold.HasValue)
      {
         allMethods = allMethods
            .Where(m => (m.Metrics.Cyclomatic ?? 0) >= threshold.Value)
            .ToList();
      }

      // Apply sorting
      allMethods = sortBy switch
      {
         ComplexitySortBy.Cyclomatic => allMethods.OrderByDescending(m => m.Metrics.Cyclomatic ?? 0).ToList(),
         ComplexitySortBy.Cognitive => allMethods.OrderByDescending(m => m.Metrics.Cognitive ?? 0).ToList(),
         ComplexitySortBy.Nesting => allMethods.OrderByDescending(m => m.Metrics.Nesting ?? 0).ToList(),
         ComplexitySortBy.Loc => allMethods.OrderByDescending(m => m.Metrics.Loc ?? 0).ToList(),
         _ => allMethods // Position - already in source order
      };

      // Apply maxMethods limit
      var methodsToReturn = allMethods.Take(maxMethods).ToList();

      // For Summary verbosity, don't include individual methods
      IReadOnlyList<MethodMetrics>? methods = verbosity == ToolResultVerbosity.Summary
         ? null
         : methodsToReturn;

      return ToolResult.Create<IComplexityResponse>(
         new FileComplexityResponse
         {
            FilePath = _workspaceManager.FormatPath(filePath),
            MethodCount = totalMethodCount,
            FilteredCount = threshold.HasValue ? allMethods.Count : null,
            FileTotals = fileTotals,
            Methods = methods
         },
         SuggestedNextTools,
         totalCount: totalMethodCount,
         returnedCount: methodsToReturn.Count);
   }

   private static ComplexityMetrics CalculateMetrics(SyntaxNode node, List<MetricType> requestedMetrics)
      => AnalysisHelpers.CalculateComplexityMetrics(node, requestedMetrics);

   private static FileComplexityTotals CalculateFileTotals(List<MethodMetrics> methods, List<MetricType> requestedMetrics)
   {
      if (methods.Count == 0)
      {
         return new FileComplexityTotals();
      }

      // Single pass through metrics to calculate all aggregates
      int totalCyclomatic = 0;
      int maxCyclomatic = 0;
      int maxNesting = 0;
      int totalLoc = 0;
      int maxCognitive = 0;
      int totalCognitive = 0;

      foreach (var method in methods)
      {
         var cyclomatic = method.Metrics.Cyclomatic ?? 0;
         totalCyclomatic += cyclomatic;
         maxCyclomatic = Math.Max(maxCyclomatic, cyclomatic);

         maxNesting = Math.Max(maxNesting, method.Metrics.Nesting ?? 0);
         totalLoc += method.Metrics.Loc ?? 0;

         var cognitive = method.Metrics.Cognitive ?? 0;
         totalCognitive += cognitive;
         maxCognitive = Math.Max(maxCognitive, cognitive);
      }

      // Only populate requested metrics
      var requestedSet = requestedMetrics.ToHashSet();

      return new FileComplexityTotals
      {
         AvgCyclomatic = requestedSet.Contains(MetricType.Cyclomatic)
            ? Math.Round((double)totalCyclomatic / methods.Count, 2)
            : null,
         MaxCyclomatic = requestedSet.Contains(MetricType.Cyclomatic) ? maxCyclomatic : null,
         MaxNesting = requestedSet.Contains(MetricType.Nesting) ? maxNesting : null,
         TotalLoc = requestedSet.Contains(MetricType.Loc) ? totalLoc : null,
         AvgCognitive = requestedSet.Contains(MetricType.Cognitive)
            ? Math.Round((double)totalCognitive / methods.Count, 2)
            : null,
         MaxCognitive = requestedSet.Contains(MetricType.Cognitive) ? maxCognitive : null
      };
   }

   /// <summary>
   /// Extracts a method name from syntax node when symbol is not available.
   /// </summary>
   private static string GetMethodName(SyntaxNode node) => node switch
   {
      MethodDeclarationSyntax m => m.Identifier.Text,
      ConstructorDeclarationSyntax c => c.Identifier.Text,
      DestructorDeclarationSyntax d => "~" + d.Identifier.Text,
      OperatorDeclarationSyntax o => "operator " + o.OperatorToken.Text,
      ConversionOperatorDeclarationSyntax c => (c.ImplicitOrExplicitKeyword.Text == "implicit" ? "implicit" : "explicit") + " operator",
      LocalFunctionStatementSyntax l => l.Identifier.Text,
      _ => "<unknown>"
   };
}

#region Result Types

/// <summary>
/// Common interface for complexity result types.
/// </summary>
[JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
[JsonDerivedType(typeof(MethodComplexityResponse), "method")]
[JsonDerivedType(typeof(FileComplexityResponse), "file")]
public interface IComplexityResponse
{
   string Scope { get; }
}

/// <summary>
/// Strongly typed complexity metrics. Null values indicate the metric was not requested.
/// </summary>
public record ComplexityMetrics
{
   [Description("Cyclomatic complexity - number of decision points plus one.")]
   public int? Cyclomatic { get; init; }

   [Description("Maximum nesting depth of control structures.")]
   public int? Nesting { get; init; }

   [Description("Lines of code (unique lines containing tokens).")]
   public int? Loc { get; init; }

   [Description("Number of parameters in the method signature.")]
   public int? Parameters { get; init; }

   [Description("Cognitive complexity (Sonar-style) - accounts for nesting when calculating complexity.")]
   public int? Cognitive { get; init; }
}

/// <summary>
/// File-level aggregated complexity totals.
/// </summary>
public record FileComplexityTotals
{
   [Description("Average cyclomatic complexity across all methods.")]
   public double? AvgCyclomatic { get; init; }

   [Description("Maximum cyclomatic complexity of any method.")]
   public int? MaxCyclomatic { get; init; }

   [Description("Maximum nesting depth of any method.")]
   public int? MaxNesting { get; init; }

   [Description("Total lines of code across all methods.")]
   public int? TotalLoc { get; init; }

   [Description("Average cognitive complexity across all methods.")]
   public double? AvgCognitive { get; init; }

   [Description("Maximum cognitive complexity of any method.")]
   public int? MaxCognitive { get; init; }
}

/// <summary>
/// Metrics for a single method.
/// </summary>
public record MethodMetrics
{
   [Description("Method name.")]
   public required string Name { get; init; }

   [Description("Name of the containing type.")]
   public string? ContainingType { get; init; }

   [Description("Line number where the method is defined (1-based).")]
   public int? Line { get; init; }

   [Description("Complexity metrics for this method.")]
   public required ComplexityMetrics Metrics { get; init; }
}

/// <summary>
/// Result from GetComplexityMetrics for a single method.
/// </summary>
public record MethodComplexityResponse : IComplexityResponse
{
   [Description("Analysis scope: 'method' for single method analysis.")]
   public string Scope => "method";

   [Description("Method name.")]
   public required string Name { get; init; }

   [Description("Name of the containing type.")]
   public string? ContainingType { get; init; }

   [Description("Complexity metrics for this method.")]
   public required ComplexityMetrics Metrics { get; init; }

   [Description("Source location (included when verbosity is 'full').")]
   [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
   public SymbolLocation? Location { get; init; }
}

/// <summary>
/// Result from GetComplexityMetrics for an entire file.
/// </summary>
public record FileComplexityResponse : IComplexityResponse
{
   [Description("Analysis scope: 'file' for file-level analysis.")]
   public string Scope => "file";

   [Description("Path to the analyzed file.")]
   public required string FilePath { get; init; }

   [Description("Total number of methods in the file.")]
   public required int MethodCount { get; init; }

   [Description("Number of methods after applying threshold filter. Null if no threshold was set.")]
   public int? FilteredCount { get; init; }

   [Description("Aggregated complexity statistics for the file.")]
   public required FileComplexityTotals FileTotals { get; init; }

   [Description("Individual method metrics. Null when verbosity is Summary.")]
   public IReadOnlyList<MethodMetrics>? Methods { get; init; }
}

#endregion
