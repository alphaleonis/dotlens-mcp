using Microsoft.CodeAnalysis;

namespace DotLens.Infrastructure;

/// <summary>
/// Full workspace interface with both read and write operations.
/// Extends <see cref="IReadOnlyWorkspace"/> with mutating operations.
///
/// Tools that modify the workspace (apply refactorings, sync documents, etc.) should
/// depend on this interface. Tests for such tools should use <see cref="IsolatedProjectTestBase"/>
/// to ensure proper isolation.
/// </summary>
public interface IWorkspace : IReadOnlyWorkspace, IDisposable
{
   /// <summary>
   /// Lock for serializing mutating operations. Mutating tools should acquire this
   /// for the duration of their read-modify-write cycle to prevent lost updates.
   /// Self-contained operations like <see cref="SyncDocumentsAsync"/> and
   /// <see cref="LoadSolutionAsync"/> acquire this internally.
   /// </summary>
   AsyncLock WriteLock { get; }

   /// <summary>
   /// Loads a solution from the specified path.
   /// </summary>
   Task<ToolResult<WorkerLoadSolutionResult>> LoadSolutionAsync(string solutionPath, CancellationToken cancellationToken = default);

   /// <summary>
   /// Synchronizes documents from disk into the loaded solution.
   /// </summary>
   Task<ToolResult<SyncDocumentsResult>> SyncDocumentsAsync(List<string>? filePaths, CancellationToken cancellationToken = default);

   /// <summary>
   /// Gets health check information about the workspace.
   /// </summary>
   Task<ToolResult<IWorkerHealthCheckResult>> GetHealthCheckAsync();

   /// <summary>
   /// Applies changes to the workspace and writes modified documents to disk.
   /// The in-memory solution is updated atomically, but file writes are sequential
   /// and not transactional - partial writes may occur if an error happens during writing.
   /// </summary>
   /// <param name="newSolution">The modified solution to apply.</param>
   /// <remarks>
   /// It writes added and removed documents as <see cref="DocumentChangePlan"/> describes.
   /// </remarks>
   /// <returns>True if changes were applied successfully, false otherwise.</returns>
   /// <exception cref="InvalidOperationException">If no workspace is loaded.</exception>
   /// <exception cref="ToolException">
   /// A document change cannot be written to the project files, and nothing was applied. It is also thrown after the
   /// changes were applied, when a write to a project file or the disk fails.
   /// </exception>
   bool ApplyChanges(Solution newSolution);

   /// <summary>
   /// Replaces the in-memory solution without writing it to disk or to the project files.
   /// A later <see cref="ApplyChanges"/> writes only what differs from this solution.
   /// For applying refactorings, use <see cref="ApplyChanges"/> instead.
   /// </summary>
   void UpdateSolution(Solution newSolution);

   /// <summary>
   /// Adds a new document to the workspace. Writes the file to disk, adds it to the
   /// in-memory Roslyn solution (without modifying the .csproj), and returns the new document.
   /// SDK-style projects auto-include *.cs files, so no project file modification is needed.
   /// </summary>
   /// <param name="filePath">Absolute or relative path for the new file.</param>
   /// <param name="content">The file content to write.</param>
   /// <param name="cancellationToken">Cancellation token.</param>
   /// <returns>The newly added Document in the workspace.</returns>
   /// <exception cref="ToolException">If the path is not under any project directory (ErrorCodes.FileNotInAnyProject).</exception>
   /// <exception cref="System.Security.SecurityException">If path traversal is detected.</exception>
   Task<Document> AddDocumentAsync(string filePath, string content, CancellationToken cancellationToken = default);

   /// <summary>
   /// Clears the document cache.
   /// </summary>
   void ClearCache();
}
