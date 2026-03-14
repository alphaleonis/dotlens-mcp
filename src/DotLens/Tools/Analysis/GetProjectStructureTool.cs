using System.ComponentModel;
using System.Text.Json.Serialization;
using DotLens.Infrastructure;
using ModelContextProtocol.Server;
namespace DotLens.Tools.Analysis;

#region Response Types

/// <summary>
/// Common interface for project structure results.
/// </summary>
[JsonPolymorphic(UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
[JsonDerivedType(typeof(ProjectStructureSummaryResponse), "summary")]
[JsonDerivedType(typeof(ProjectStructureResponse), "full")]
public interface IProjectStructureResponse
{
   [Description("Absolute path to the solution file.")]
   string? SolutionPath { get; }
}

/// <summary>
/// Summary info for a project.
/// </summary>
public record ProjectSummary
{
   [Description("Project name (without path or extension).")]
   public required string Name { get; init; }

   [Description("Number of source documents in the project.")]
   public required int DocumentCount { get; init; }

   [Description("Number of project-to-project references.")]
   public required int ProjectReferenceCount { get; init; }

   [Description("Programming language (e.g., 'C#', 'Visual Basic').")]
   public required string Language { get; init; }
}

/// <summary>
/// Document info for project structure.
/// </summary>
public record ProjectDocument
{
   [Description("File name (e.g., 'Program.cs').")]
   public required string Name { get; init; }

   [Description("File path (absolute or relative to solution, based on workspace settings).")]
   public string? FilePath { get; init; }

   [Description("Logical folder path within the project (e.g., ['Services', 'Auth']).")]
   public required IReadOnlyList<string> Folders { get; init; }
}

/// <summary>
/// Detailed project info.
/// </summary>
public record ProjectDetails
{
   [Description("Project name (without path or extension).")]
   public required string Name { get; init; }

   [Description("Project file path (absolute or relative to solution).")]
   public string? FilePath { get; init; }

   [Description("Programming language (e.g., 'C#', 'Visual Basic').")]
   public required string Language { get; init; }

   [Description("Output assembly path (e.g., 'bin/Debug/net8.0/MyProject.dll').")]
   public string? OutputPath { get; init; }

   [Description("Number of source documents in the project.")]
   public required int DocumentCount { get; init; }

   [Description("Number of NuGet/assembly references.")]
   public required int ReferenceCount { get; init; }

   [Description("NuGet/assembly reference names (limited to 100). Null if includeReferences=false.")]
   public IReadOnlyList<string>? References { get; init; }

   [Description("Names of referenced projects within the solution.")]
   public required IReadOnlyList<string> ProjectReferences { get; init; }

   [Description("Source documents (limited to 500). Null if includeDocuments=false.")]
   public IReadOnlyList<ProjectDocument>? Documents { get; init; }
}

/// <summary>
/// Result from GetProjectStructure with summary only.
/// </summary>
public record ProjectStructureSummaryResponse : IProjectStructureResponse
{
   [Description("Absolute path to the solution file.")]
   public string? SolutionPath { get; init; }

   [Description("List of projects with summary information.")]
   public required IReadOnlyList<ProjectSummary> Projects { get; init; }
}

/// <summary>
/// Result from GetProjectStructure with full details.
/// </summary>
public record ProjectStructureResponse : IProjectStructureResponse
{
   [Description("Absolute path to the solution file.")]
   public string? SolutionPath { get; init; }

   [Description("List of projects with detailed information.")]
   public required IReadOnlyList<ProjectDetails> Projects { get; init; }
}

#endregion

/// <summary>
/// Get solution/project structure.
/// </summary>
[McpServerToolType]
[DotLensWorkerTool]
public class GetProjectStructureTool
{
   private static readonly string[] SuggestedNextToolsSummary = ["get_project_structure with summaryOnly=false for details"];
   private static readonly string[] SuggestedNextToolsDetails = ["get_diagnostics for a project to check for issues", "dependency_graph to visualize dependencies"];

   private readonly IReadOnlyWorkspace _workspaceManager;

   public GetProjectStructureTool(IReadOnlyWorkspace workspaceManager)
   {
      _workspaceManager = workspaceManager;
   }

   [McpServerTool(Name = "get_project_structure", ReadOnly = true, OpenWorld = false)]
   [Description("Get solution/project structure. Use summaryOnly=true or projectNamePattern for large solutions.")]
   public Task<ToolResult<IProjectStructureResponse>> GetProjectStructureAsync(
      [Description("Include package references (default: true, limited to 100 per project)")]
      bool includeReferences = true,
      [Description("Include document lists (default: false, limited to 500 per project)")]
      bool includeDocuments = false,
      [Description("Filter projects by name pattern (supports * and ? wildcards)")]
      string? projectNamePattern = null,
      [Description("Maximum number of projects to return")]
      [PositiveInteger]
      int? maxProjects = null,
      [Description("Return only project names and counts (recommended for large solutions)")]
      bool summaryOnly = false,
      CancellationToken cancellationToken = default)
   {
      var solution = _workspaceManager.EnsureSolutionLoaded();
      var filteredProjects = solution.Projects.AsEnumerable();

      if (!string.IsNullOrEmpty(projectNamePattern))
      {
         var projectMatcher = new GlobMatcher(projectNamePattern);
         filteredProjects = filteredProjects.Where(p => projectMatcher.IsMatch(p.Name));
      }

      if (maxProjects.HasValue && maxProjects.Value > 0)
      {
         filteredProjects = filteredProjects.Take(maxProjects.Value);
      }

      var projectsList = filteredProjects.ToList();

      if (summaryOnly)
      {
         var summary = new List<ProjectSummary>(projectsList.Count);
         foreach (var p in projectsList)
         {
            cancellationToken.ThrowIfCancellationRequested();
            summary.Add(new ProjectSummary
            {
               Name = p.Name,
               DocumentCount = p.DocumentIds.Count,
               ProjectReferenceCount = p.ProjectReferences.Count(),
               Language = p.Language
            });
         }

         return Task.FromResult(ToolResult.Create<IProjectStructureResponse>(
            new ProjectStructureSummaryResponse
            {
               SolutionPath = solution.FilePath,
               Projects = summary
            },
            SuggestedNextToolsSummary));
      }

      var projects = new List<ProjectDetails>(projectsList.Count);
      foreach (var project in projectsList)
      {
         cancellationToken.ThrowIfCancellationRequested();

         var references = includeReferences
            ? project.MetadataReferences.Take(100).Select(r => r.Display ?? "Unknown").ToList()
            : null;

         var projectReferences = project.ProjectReferences
            .Select(pr => solution.GetProject(pr.ProjectId)?.Name ?? "Unknown")
            .ToList();

         var documents = includeDocuments
            ? project.Documents.Take(500).Select(d => new ProjectDocument
            {
               Name = d.Name,
               FilePath = PathUtils.FormatPath(d.FilePath, _workspaceManager.SolutionPath, _workspaceManager.UseAbsolutePaths),
               Folders = d.Folders.ToList()
            }).ToList()
            : null;

         projects.Add(new ProjectDetails
         {
            Name = project.Name,
            FilePath = PathUtils.FormatPath(project.FilePath, _workspaceManager.SolutionPath, _workspaceManager.UseAbsolutePaths),
            Language = project.Language,
            OutputPath = project.OutputFilePath,
            DocumentCount = project.DocumentIds.Count,
            ReferenceCount = project.MetadataReferences.Count,
            References = references,
            ProjectReferences = projectReferences,
            Documents = documents
         });
      }

      return Task.FromResult(ToolResult.Create<IProjectStructureResponse>(
         new ProjectStructureResponse
         {
            SolutionPath = solution.FilePath,
            Projects = projects
         },
         SuggestedNextToolsDetails));
   }
}
