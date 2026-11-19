# dotlens-jxhi — A file removed by sync_documents is later deleted from the project file (2026-11-17)

## Acceptance results

- [run] `*SyncDocuments_DeletedFile_DoesNotRewriteProject*`: 10/10, exit 0.
- [run] full suite (`--max-threads 8`): 1251/1251, exit 0.

Commit a530343. One review round (bugs): APPROVED with one Low (a test pinned Roslyn deleting a shared file), fixed.

## Deviations

- The cause was the MSBuildWorkspace's solution lagging DotLens's after any sync or write_file-added file; fixing it also stopped a later write from overwriting a synced file with stale text.

## Follow-ups

dotlens-p6xt (add_using CRLF, organize_usings blank first line), dotlens-c5qn (removing a document deletes a shared file / throws on SDK-included files). Not filed: `Dispose` clears state without the write lock (shutdown only); duplicated `FindRepoRoot` test helper.

## Open questions (filed: dotlens-0vlu)
### Reflection on Workspace.SetCurrentSolution
- **Kind:** confirm
- **From:** dotlens-jxhi; `src/DotLens/Infrastructure/WorkspaceManager.cs`
- **What happened:** To keep sync results out of later `TryApplyChanges` writes, `UpdateSolution` calls the protected `Workspace.SetCurrentSolution` through reflection. Roslyn 5.12 has no public way to set an `MSBuildWorkspace`'s current solution without writing. A Roslyn upgrade that renames it fails loudly (named exception, 12 tests).
- **Options:**
  - A. Keep the reflection call.
  - B. Avoid it: rebase each write onto the workspace's solution by replaying only the tool's own changes (larger change, no private API).
- **Recommendation:** A. It is one cached lookup, and a break is loud.
