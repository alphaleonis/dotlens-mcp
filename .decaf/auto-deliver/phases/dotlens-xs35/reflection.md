# dotlens-xs35 — late load replaces the active solution (2026-11-20)

## Acceptance results

The nib's criteria are prose; each was settled by execution:
- Latest request names the active solution even when an earlier load finishes after it: `MultiWorkerManagerActiveSolutionTests` rows 1, 3, 5, 12/13 (deterministic fake loader, no timing).
- Auto-load becomes active only when no explicit request has made one active: rows 1, 2, 15; see the open question for the nothing-active case.
- Tests cover an explicit load finishing before the auto-load (row 1) and two explicit loads finishing in reverse order (row 3).
- [run] full suite (`--max-threads 8`): 2066/2066, exit 0 (includes WorkerReplyE2ETests with real workers).

Commit f3edd8f. Reviews: bugs (APPROVED, 1 Medium: unload made activation order-dependent) → fixed; bugs roster=3 re-review on the concurrency trigger (APPROVED, 1 Medium: unload replacement ignored request order, + 1 Minor) → fixed; fix-verifier at the iteration cap: both ADDRESSED, no breakage, lock discipline checked.

## Deviations

- `reload_solution` counts as an explicit request for its path, like `load_solution`.
- `unload_solution` of the active solution now picks the loaded solution named by the latest request (ties by ordinal path) instead of the first key of the worker dictionary.
- `LoadSolutionResult`/`load_solution` gained `isActive`, and a message naming the active solution when the load did not become active.

## Open questions (filed: dotlens-ln2o)

### The auto-load becomes active when nothing else is active
- **Kind:** confirm
- **From:** dotlens-xs35; `MultiWorkerManager.TryActivate`, `UnloadSolutionAsync`
- **What happened:** The nib says "a background auto-load becomes active only when no explicit request has set an active solution since the server started". Read literally, if the client loads B (B active), then unloads B, the auto-loaded solution must stay inactive and every tool call fails with `NO_SOLUTION_LOADED` until `set_active_solution`. But the unload fallback that predates this change already made any remaining solution active, so the literal rule made the outcome depend on whether the auto-load finished before or after the unload (two reviews flagged this). The run chose: while nothing is active, any finished load becomes active, the auto-load included; while a solution is active, only a later request takes over. The same rule makes an older explicit load that finishes after the active solution was unloaded become active.
- **Options:**
  - A. Keep it — order-independent; the auto-load never replaces an explicit choice, it only fills an empty slot.
  - B. Make the literal rule hold — the auto-load never becomes active after an explicit request, and the unload fallback must also skip solutions only the auto-load named; small change in `TryActivate` and `LatestNamedSolution`, plus tests 11/11b.
- **Recommendation:** A. A client that unloads its solution and keeps calling tools gets a working answer instead of `NO_SOLUTION_LOADED`, and each tool response names the solution it ran against. Not checked: whether any client depends on the empty state after an unload.
