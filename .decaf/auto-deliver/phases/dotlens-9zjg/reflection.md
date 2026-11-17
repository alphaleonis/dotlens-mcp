# dotlens-9zjg — sync_documents leaves the linked copies of a multi-targeted file stale (2026-11-17)

## Acceptance results

- [run] `*SyncDocuments_LinkedCopies*`: 5/5, exit 0.
- [run] full suite (`--max-threads 8`): 1238/1238, exit 0.

Commit cc318e2. One review round (bugs): APPROVED, no findings.

## Deviations

- Deleted files are removed from every project, not just one.
- Conductor removed a now-false remark in `ReferenceSearch.IsSameSymbol` (linked copies out of date).

## Follow-ups

dotlens-jxhi — a document removed by sync is later deleted from the project file by `TryApplyChanges` (pre-existing, found by the review).
Not filed: the path dictionary compares case-sensitively on Linux while `GetDocumentIdsWithFilePath` ignores case; on a case-sensitive file system that is correct.

## Open questions
