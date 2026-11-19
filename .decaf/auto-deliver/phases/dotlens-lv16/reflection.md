# dotlens-lv16 — bounded cognitive-complexity recursion search (2026-11-19)

Single-task plan; the task was the phase, so BREAKDOWN was skipped.

## Acceptance results

- [run] `just test` (run as `dotnet test --max-threads 8`): 1926/1926, exit 0. No existing test changed.
- [run] harness on DotLens.slnx: exit 0; Cognitive's cost after sync 43.7 / 58.2 ms (DotLens, method / file) and 6.3 / 150.8 ms (DotLens.Tests). Pass.
- [run] harness on Bibbla.slnx: exit 0; 9.8 / 52.2 ms (Bibbla.Api) and 40.2 / 155.3 ms (Bibbla.Api.Tests). Pass after the harness fix; first run read 634 ms for Bibbla.Api method mode.
- [run] harness on Roslyn Compilers.slnf: exit 0; 62.2 ms method, 1545.6 ms file (worst case, not held to 500 ms).
- [run] `nibs get dotlens-lv16 | rg -c "^## Measurements"`: 1; before and after tables for all three solutions.

Commits e1450a3 (harness warm-up) and 69de145 (bounded search). Review: bugs (solo seat), APPROVED with 1 Medium and 1 Low, both in the new tests, applied by the conductor inline (assert the discarded return value; drop the pre-canceled theory, which touched no cache state).

## Fixes applied in VERIFY

- The first harness run failed the Bibbla.Api criterion at 634 ms. A focused fix found the cause in the harness, not the search: tiered JIT left the first measured cell per target slower (`DOTNET_TieredCompilation=0` flattened the cells), because the warm-up never ran the load-and-sync path. The warm-up now runs every cell untimed. Cognitive's cost in Bibbla.Api method mode is then 16 ms in either cell order.
- The before numbers were re-measured with the fixed harness from a worktree at a0e186f, so before and after compare under the same harness. dotlens-ppex got a note that its tables predate the fix.

## Deviations

- The harness change was not in the nib's scope; it was needed to measure the criterion honestly and is a separate commit.
- The implementer's differential check (old vs new answers, 0 mismatches over 128,688 comparisons in TestFixtures, DotLens and Bibbla) was run once and not committed, as the brief said.

## Follow-ups

None filed. Awareness: Roslyn's file mode on the 982 KB generated `BoundNodes.xml.Generated.cs` still costs 1.5 s after a sync, because its 2,213 methods together reach much of the compilation; a file of ordinary size stays well under that.

## Open questions

None.
