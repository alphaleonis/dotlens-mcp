using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace DotLens.Infrastructure;

/// <summary>
/// Read-only view of the workspace. All methods are side-effect free and safe to call
/// concurrently from multiple threads.
///
/// Tools that only need to query the workspace should depend on this interface rather than
/// <see cref="IWorkspace"/>. This enables:
/// - Compile-time safety: read-only tools can't accidentally mutate state
/// - Parallel test execution: tests for read-only tools can safely share a workspace
/// - Clear contracts: tool dependencies declare their side-effect characteristics
/// </summary>
public interface IReadOnlyWorkspace
{
   /// <summary>
   /// Gets the currently loaded solution, or null if none loaded.
   /// </summary>
   Solution? Solution { get; }

   /// <summary>
   /// Gets the path to the loaded solution file.
   /// </summary>
   string? SolutionPath { get; }

   /// <summary>
   /// Gets the directory containing the loaded solution file.
   /// Returns null if no solution is loaded.
   /// </summary>
   string? SolutionDirectory { get; }

   /// <summary>
   /// Gets the number of projects in the loaded solution.
   /// </summary>
   int ProjectCount { get; }

   /// <summary>
   /// Gets the number of documents across all projects.
   /// </summary>
   int DocumentCount { get; }

   /// <summary>
   /// Gets whether a solution is currently loaded.
   /// </summary>
   bool IsSolutionLoaded { get; }

   /// <summary>
   /// Gets whether to use absolute paths in responses.
   /// </summary>
   bool UseAbsolutePaths { get; }

   /// <summary>
   /// Gets the maximum number of diagnostics to return.
   /// </summary>
   int MaxDiagnostics { get; }

   /// <summary>
   /// Gets the timeout in seconds for operations.
   /// </summary>
   int TimeoutSeconds { get; }

   /// <summary>
   /// Gets the maximum number of symbols to collect when sorting search results.
   /// </summary>
   int MaxSortableResults { get; }

   /// <summary>
   /// Ensures a solution is loaded and returns it, throwing if not.
   /// </summary>
   /// <returns>The loaded solution.</returns>
   /// <exception cref="InvalidOperationException">If no solution is loaded.</exception>
   [MemberNotNull(nameof(Solution))]
   Solution EnsureSolutionLoaded();

   /// <summary>
   /// Formats a path according to configuration (relative or absolute).
   /// </summary>
   string FormatPath(string? absolutePath);

   /// <summary>
   /// Gets a document by file path.
   /// </summary>
   /// <exception cref="InvalidOperationException">If no solution is loaded.</exception>
   /// <exception cref="ToolException">If document not found in solution (ErrorCodes.FileNotInSolution).</exception>
   /// <exception cref="ToolException">If path traversal attack detected (ErrorCodes.SecurityError).</exception>
   Task<Document> GetDocumentAsync(string filePath, CancellationToken cancellationToken = default);

   /// <summary>
   /// Tries to get a document by file path, returning null if not found.
   /// </summary>
   /// <exception cref="InvalidOperationException">If no solution is loaded.</exception>
   Task<Document?> TryGetDocumentAsync(string filePath, CancellationToken cancellationToken = default);

   /// <summary>
   /// Finds a type by name across all projects.
   /// </summary>
   Task<INamedTypeSymbol?> FindTypeByNameAsync(string typeName, CancellationToken cancellationToken = default);

   /// <summary>
   /// Finds a type by name across all projects, returning the compilation context.
   /// </summary>
   /// <param name="typeName">The type name to search for.</param>
   /// <param name="requireFullyQualifiedName">
   /// If true, only matches fully-qualified names (e.g., "MyNamespace.MyClass").
   /// If false (default), also matches simple names with case-insensitive matching.
   /// </param>
   /// <param name="projectName">
   /// Optional: limit search to a specific project by name. When provided:
   /// - Only searches that project (faster for large solutions)
   /// - No ambiguity check needed (explicit project selection)
   /// - Returns null if type not found in that project
   /// </param>
   /// <param name="cancellationToken">Cancellation token.</param>
   /// <returns>
   /// A TypeLookupResult containing the type, compilation, and project if found;
   /// null if the type was not found in any project.
   /// </returns>
   /// <exception cref="ToolException">
   /// Thrown with ErrorCodes.AmbiguousType if the type name matches multiple distinct types
   /// in different projects (different assemblies with the same FQN, or different FQNs).
   /// </exception>
   Task<TypeLookupResult?> FindTypeWithCompilationAsync(
      string typeName,
      bool requireFullyQualifiedName = false,
      string? projectName = null,
      CancellationToken cancellationToken = default);
}
