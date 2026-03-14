using System.ComponentModel;
using System.Text.Json.Serialization;

namespace DotLens.Infrastructure;

/// <summary>
/// Polymorphic base type for search scope parameters.
/// Use "type" discriminator to specify scope: "solution", "project", or "file".
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SolutionScope), "solution")]
[JsonDerivedType(typeof(ProjectScope), "project")]
[JsonDerivedType(typeof(FileScope), "file")]
public abstract class SearchScope { }

/// <summary>
/// Search across entire solution.
/// </summary>
[Description("Search entire solution (default)")]
public class SolutionScope : SearchScope { }

/// <summary>
/// Search within a specific project.
/// </summary>
[Description("Search within a specific project")]
public class ProjectScope : SearchScope
{
   [Description("Project name")]
   public required string Name { get; set; }
}

/// <summary>
/// Search within a specific file.
/// </summary>
[Description("Search within a specific file")]
public class FileScope : SearchScope
{
   [Description("File path or name (can be partial, e.g., 'MyClass.cs')")]
   public required string Path { get; set; }
}
