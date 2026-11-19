# dotlens-0zzn — indirect recursion in cognitive complexity (2026-11-19)

Added mid-run at the operator's request after they refined the draft split from dotlens-w6b1.

## Acceptance results

- [run] `--filter-class "*.CognitiveComplexityTests"`: 14/14, exit 0.
- [run] `*Cognitive_DirectRecursionAddsOnePerMethod`: 1/1, exit 0 (expectations unchanged since a5f2dce).
- [run] `grep -n "Only direct recursion" AnalysisHelpers.cs`: no output.
- [run] `just test` (run as `dotnet test --max-threads 8`): first 1759/1762 (fixture collisions), then 1762/1762, exit 0.
- [manual → executed] red run: the implementer ran the new tests before each fix; values recorded in the commit message.

Commit 7430e81. Reviews: bugs (NEEDS_CHANGES, 1H accessors not nodes + 1M implicit calls + 1L cost), bugs re-review (APPROVED, 2M false/missed cycles + 1L), fix-verifier (#2 NOT ADDRESSED: synthetic private constructor broke 3-level chains), then one targeted repair past the iteration cap under the correctness rule.

## Deviations

- Scope grew beyond the nib's "What to build": accessors are nodes (the nib listed them but pointed at a predicate that excludes them) and compiler-inserted calls are edges; both were review findings under the correctness rule.
- Not followed (documented in the tool description): using-statement Dispose, collection-expression Add, the awaits of await foreach — Roslyn's operation tree does not hold their targets.
- Two FindUnusedCodeToolTests cases now pass maxResults: 1000; they assumed UnusedCode.cs symbols appear within the first 50 results over the whole CoreTestProject.

## Follow-ups

None filed. Raised with the operator: position lookup at a record's positional parameter resolves the parameter, not the property search_symbols reports there (found when a fixture's `record R(int Id)` broke FindCallers_OnProperty_FindsUsages).

## Open questions

### Cognitive complexity builds a whole-compilation call graph on first use
- **Kind:** confirm
- **From:** dotlens-0zzn; `AnalysisHelpers.FindRecursionCycleMembers`, `GetImplicitBaseConstructorCalls`
- **What happened:** The first cognitive-complexity request against a compilation walks every callable body in it, and also builds one modified copy of the compilation to bind implicit base() calls; results are cached per compilation, so any edit rebuilds them. The reviewer measured about 1.5–3.4 s for a generated project of 6,000 methods, then 0 ms while cached. This is the nib's decided design; the cost was not measured on a real large solution.
- **Options:**
  - A. Keep it — correct cycle counts; first-call latency on large projects.
  - B. Bound it — e.g. only build the graph for the measured method's reachable call set; more code, same results.
- **Recommendation:** A until a real solution shows the latency matters. Not checked: timing on this repo's own solution.
