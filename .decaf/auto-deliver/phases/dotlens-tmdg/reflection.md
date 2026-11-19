# dotlens-tmdg — find_references misses the indexer in a null-conditional assignment (2026-11-17)

## Acceptance results

- [run] `--filter-method "*FindReferences_NullConditionalIndexerAssignment*"`: 5/5, exit 0. `slots?[0] = 9` is returned as `Write`.
- [run] full suite (`--max-threads 8`): 1233/1233, exit 0.

Commit e17a624.

## What was built

The gap is in Roslyn 5.12 (`PropertySymbolReferenceFinder.ComputeConditionalAccessInformation`), also on Roslyn main; no upstream issue found. DotLens now fixes it itself: new `ReferenceSearch` (src/DotLens/Tools/Shared) wraps `SymbolFinder.FindReferencesAsync`/`FindCallersAsync`, drops Roslyn's indexer locations at `?.`/`?[` bindings and adds every element binding that binds to the indexer. find_references, find_callers, analyze_change_impact, analyze_method and find_unused_code all go through it.

## Deviations

- Scope grew past the nib's single case, same root cause: compound/`??=` assignments, `a?[0].M()`, chained `a?.b?[0]`, the wrong location for `[1]` in `a?[0][1]` and a stray duplicate at `.Self` in `a?.Self[1]`; find_unused_code reported an indexer used only as `x?[i] = v` as unused.
- Three review rounds (bugs, bugs, bugs roster=3), each APPROVED with Medium findings, all fixed: scan limited to projects that can refer to the indexer (compiled-copy references excluded, as Roslyn does); multi-target and linked-file projects added to the scope; linked copies match by documentation ID + declaration position, so a stale copy cannot pair two overloads; path comparison ignores case like `GetDocumentIdsWithFilePath`.
- Awareness, not fixed: every indexer search walks the documents of every in-scope project, and find_unused_code repeats it per indexer (review #2, anchor 50, unmeasured). `FindReferences_LinkedIndexer` pins Roslyn's own linkage, not the round-2 fix; the FindCallers theory guards it.

## Follow-ups

dotlens-9zjg (sync_documents leaves linked copies stale), dotlens-cr8j (references duplicated per linked copy), dotlens-6nks (source-generated references counted but not listed). Not filed: VB null-conditional indexers (DotLens has no VB support).

## Open questions (filed: dotlens-0vlu)
### File the Roslyn null-conditional indexer gap upstream?
- **Kind:** decision
- **From:** dotlens-tmdg; `src/DotLens/Tools/Shared/ReferenceSearch.cs`
- **What happened:** Roslyn's find-references misses or misplaces indexers after `?` (`x?[i] = v`, `x?[0][1]`). DotLens works around it in `ReferenceSearch`. No dotnet/roslyn issue exists; the agent did not file one, since it would post publicly under your account.
- **Options:**
  - A. File an issue on dotnet/roslyn (the agent can draft it), and drop the workaround once fixed.
  - B. Keep the workaround only.
- **Recommendation:** A. The workaround scans every element binding in scope on each indexer search; an upstream fix removes that cost.
