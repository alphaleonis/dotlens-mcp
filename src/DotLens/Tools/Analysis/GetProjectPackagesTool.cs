using System.ComponentModel;
using DotLens.Infrastructure;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Analysis;

/// <summary>
/// List NuGet packages for a project with versions.
/// </summary>
[McpServerToolType]
[DotLensWorkerTool]
public class GetProjectPackagesTool
{
   private static readonly string[] SuggestedNextTools = ["get_project_structure to see project details", "dependency_graph to visualize project dependencies"];

   private readonly IReadOnlyWorkspace _workspaceManager;

   public GetProjectPackagesTool(IReadOnlyWorkspace workspaceManager)
   {
      _workspaceManager = workspaceManager;
   }

   [McpServerTool(Name = "get_project_packages", ReadOnly = true, OpenWorld = false)]
   [Description("List NuGet packages referenced by projects in the solution with their versions.")]
   public async Task<ToolResult<GetProjectPackagesResponse>> GetProjectPackagesAsync(
      CancellationToken cancellationToken,
      [Description("Filter to a specific project by name. If omitted, returns packages from all projects.")]
      string? projectName = null)
   {
      var solution = _workspaceManager.EnsureSolutionLoaded();

      IEnumerable<Microsoft.CodeAnalysis.Project> projects = solution.Projects;
      if (!string.IsNullOrEmpty(projectName))
      {
         projects = projects.Where(p =>
            p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));
      }

      var packageData = new List<(string Name, string Version, string Project)>();
      var parseErrors = new List<string>();

      foreach (var project in projects)
      {
         cancellationToken.ThrowIfCancellationRequested();
         if (string.IsNullOrEmpty(project.FilePath) || !File.Exists(project.FilePath))
            continue;

         try
         {
            var projectFile = await File.ReadAllTextAsync(project.FilePath, cancellationToken);
            var doc = System.Xml.Linq.XDocument.Parse(projectFile);

            // Find all PackageReference elements
            var packageRefs = doc.Descendants()
               .Where(e => e.Name.LocalName == "PackageReference");

            foreach (var pkgRef in packageRefs)
            {
               var name = pkgRef.Attribute("Include")?.Value;
               var version = pkgRef.Attribute("Version")?.Value
                  ?? pkgRef.Element(System.Xml.Linq.XName.Get("Version", pkgRef.Name.NamespaceName))?.Value;

               if (!string.IsNullOrEmpty(name))
               {
                  packageData.Add((name, version ?? "not specified", project.Name));
               }
            }
         }
         catch (Exception ex)
         {
            parseErrors.Add($"{project.Name}: {ex.Message}");
         }
      }

      // Group by package name for summary
      var grouped = packageData
         .GroupBy(p => p.Name)
         .Select(g => new PackageInfo
         {
            Name = g.Key,
            Versions = g.Select(p => new PackageVersion { Version = p.Version, Project = p.Project }).ToList()
         })
         .ToList();

      return ToolResult.Create(
         new GetProjectPackagesResponse
         {
            TotalPackages = grouped.Count,
            Packages = grouped,
            ParseErrors = parseErrors.Count > 0 ? parseErrors : null
         },
         suggestedNextTools: SuggestedNextTools);
   }
}

#region Result Types

/// <summary>
/// Package version info showing which project uses which version.
/// </summary>
public record PackageVersion
{
   [Description("The package version string (e.g., '8.0.0', '1.2.3-preview').")]
   public required string Version { get; init; }

   [Description("The project name that references this package version.")]
   public required string Project { get; init; }
}

/// <summary>
/// Package info grouped by name, showing all versions across projects.
/// </summary>
public record PackageInfo
{
   [Description("The NuGet package name (e.g., 'Newtonsoft.Json', 'Microsoft.Extensions.Logging').")]
   public required string Name { get; init; }

   [Description("List of version/project pairs showing which projects use which versions.")]
   public required IReadOnlyList<PackageVersion> Versions { get; init; }
}

/// <summary>
/// Result from GetProjectPackages tool.
/// </summary>
public record GetProjectPackagesResponse
{
   [Description("Total number of unique packages found across all queried projects.")]
   public required int TotalPackages { get; init; }

   [Description("Packages grouped by name, each containing version info per project.")]
   public required IReadOnlyList<PackageInfo> Packages { get; init; }

   [Description("List of projects that failed to parse, if any. Null if all projects parsed successfully.")]
   public IReadOnlyList<string>? ParseErrors { get; init; }
}

#endregion
