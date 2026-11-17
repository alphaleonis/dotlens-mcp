using System.ComponentModel;
using DotLens.Infrastructure;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Worker;

#region Result Types

/// <summary>
/// Error info from document sync.
/// </summary>
public record SyncError
{
   [Description("File path that failed to sync.")]
   public required string Path { get; init; }

   [Description("Error message describing the sync failure.")]
   public required string Error { get; init; }
}

/// <summary>
/// Result from syncing documents.
/// </summary>
public record SyncDocumentsResult
{
   [Description("Number of files whose documents were updated with new content from disk.")]
   public required int Updated { get; init; }

   [Description("Number of new documents added.")]
   public required int Added { get; init; }

   [Description("Number of files whose documents were removed because the file was deleted from disk.")]
   public required int Removed { get; init; }

   [Description("Total files processed (Updated + Added + Removed).")]
   public required int TotalSynced { get; init; }

   [Description("Paths of files that were updated with new content.")]
   public required IReadOnlyList<string> UpdatedFiles { get; init; }

   [Description("Paths of files that were added.")]
   public required IReadOnlyList<string> AddedFiles { get; init; }

   [Description("Paths of files that were removed from the solution.")]
   public required IReadOnlyList<string> RemovedFiles { get; init; }

   [Description("Errors encountered during sync.")]
   public IReadOnlyList<SyncError>? Errors { get; init; }
}

#endregion

[McpServerToolType]
[DotLensWorkerTool]
public class SyncDocumentsTool
{
   private readonly IWorkspace _workspaceManager;

   public SyncDocumentsTool(IWorkspace workspaceManager)
   {
      _workspaceManager = workspaceManager;
   }

   [McpServerTool(Name = "sync_documents", ReadOnly = false, OpenWorld = false)]
   [Description("Synchronize document changes from disk into the loaded solution. Call after file edits. For new files, use reload_solution.")]
   public async Task<ToolResult<SyncDocumentsResult>> SyncDocumentsAsync(
       [Description("Specific file paths to sync. If omitted, syncs ALL documents from disk.")]
       List<string>? filePaths = null,
       CancellationToken cancellationToken = default)
   {
      return await _workspaceManager.SyncDocumentsAsync(filePaths, cancellationToken);
   }
}
