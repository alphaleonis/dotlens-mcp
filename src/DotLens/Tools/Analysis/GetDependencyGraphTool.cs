using System.ComponentModel;
using System.Text.Json.Serialization;
using DotLens.Infrastructure;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Analysis;

#region Response Types

/// <summary>
/// Output format for dependency graph visualization.
/// </summary>
public enum DependencyGraphFormat
{
   /// <summary>Structured data with nodes and edges.</summary>
   Json,
   /// <summary>Mermaid diagram syntax for visualization.</summary>
   Mermaid
}

/// <summary>
/// Common interface for dependency graph results.
/// </summary>
[JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
[JsonDerivedType(typeof(DependencyGraphJsonResponse), "json")]
[JsonDerivedType(typeof(DependencyGraphMermaidResponse), "mermaid")]
public interface IDependencyGraphResponse
{
   [Description("True if circular dependencies were detected.")]
   bool HasCycles { get; }

   [Description("List of detected dependency cycles (e.g., 'A -> B -> C -> A').")]
   IReadOnlyList<string> Cycles { get; }
}

/// <summary>
/// Result from GetDependencyGraph in JSON format.
/// </summary>
public record DependencyGraphJsonResponse : IDependencyGraphResponse
{
   [Description("Map of project names to their project dependencies.")]
   public required IReadOnlyDictionary<string, List<string>> Dependencies { get; init; }

   [Description("True if circular dependencies were detected.")]
   public required bool HasCycles { get; init; }

   [Description("List of detected dependency cycles (e.g., 'A -> B -> C -> A').")]
   public required IReadOnlyList<string> Cycles { get; init; }
}

/// <summary>
/// Result from GetDependencyGraph in Mermaid format.
/// </summary>
public record DependencyGraphMermaidResponse : IDependencyGraphResponse
{
   [Description("Mermaid diagram syntax representing the dependency graph.")]
   public required string Graph { get; init; }

   [Description("True if circular dependencies were detected.")]
   public required bool HasCycles { get; init; }

   [Description("List of detected dependency cycles (e.g., 'A -> B -> C -> A').")]
   public required IReadOnlyList<string> Cycles { get; init; }
}

#endregion

/// <summary>
/// Visualize project dependencies as a graph.
/// </summary>
[McpServerToolType]
[DotLensWorkerTool]
public class GetDependencyGraphTool
{
   private static readonly string[] SuggestedNextToolsWithCycles = ["Resolve circular dependencies before building"];
   private static readonly string[] SuggestedNextToolsNoCycles = ["get_project_structure for detailed project info"];

   private readonly IReadOnlyWorkspace _workspaceManager;

   public GetDependencyGraphTool(IReadOnlyWorkspace workspaceManager)
   {
      _workspaceManager = workspaceManager;
   }

   [McpServerTool(Name = "dependency_graph", ReadOnly = true, OpenWorld = false)]
   [Description("Visualize project dependencies as a graph. Shows which projects reference which, detects circular dependencies.")]
   public Task<ToolResult<IDependencyGraphResponse>> GetDependencyGraphAsync(
      [Description("Output format for the dependency graph.")]
      DependencyGraphFormat format = DependencyGraphFormat.Json,
      CancellationToken cancellationToken = default)
   {
      var solution = _workspaceManager.EnsureSolutionLoaded();
      var projectGraph = new Dictionary<string, List<string>>();

      foreach (var project in solution.Projects)
      {
         cancellationToken.ThrowIfCancellationRequested();
         var dependencies = project.ProjectReferences
            .Select(pr => solution.GetProject(pr.ProjectId)?.Name)
            .Where(name => name != null)
            .Cast<string>()
            .ToList();

         projectGraph[project.Name] = dependencies;
      }

      var cycles = AnalysisHelpers.DetectCycles(projectGraph);

      if (format == DependencyGraphFormat.Mermaid)
      {
         var mermaid = AnalysisHelpers.GenerateMermaidGraph(projectGraph);
         return Task.FromResult(ToolResult.Create<IDependencyGraphResponse>(
            new DependencyGraphMermaidResponse
            {
               Graph = mermaid,
               HasCycles = cycles.Count > 0,
               Cycles = cycles
            },
            cycles.Count > 0 ? SuggestedNextToolsWithCycles : SuggestedNextToolsNoCycles));
      }

      return Task.FromResult(ToolResult.Create<IDependencyGraphResponse>(
         new DependencyGraphJsonResponse
         {
            Dependencies = projectGraph,
            HasCycles = cycles.Count > 0,
            Cycles = cycles
         },
         cycles.Count > 0 ? SuggestedNextToolsWithCycles : SuggestedNextToolsNoCycles));
   }
}
