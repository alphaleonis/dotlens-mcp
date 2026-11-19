# find_unused_code group — dotlens-gsy6, dotlens-uk3e (2026-11-19)

## Acceptance results

- [run] `*FindUnusedCode_ReflectionInvoked*`, `*FindUnusedCode_MutualRecursion*`: 13/13, exit 0.
- [run] full suite (`--max-threads 8`): 1639/1639, exit 0.

Commit ff96186. Reviews: bugs (NEEDS_CHANGES, 1H+1M+1L), bugs re-review (APPROVED, 3M+1L, two regressions), bugs roster=3 (NEEDS_CHANGES, 1H regression + 2 Minor); fix check found one over-match (toward missed reports), narrowed before commit.

## Deviations

- Public members are roots in every project kind (an existing multi-target test expects it).

## Follow-ups

None filed. Known misses, all in the "not reported" direction: a call to a sibling implementation marks every implementation of that member used; `new List<T>()` counts as handing T over; markup-bound members (XAML) are not modeled.

## Open questions

### find_unused_code now reports by reachability from roots
- **Kind:** confirm
- **From:** dotlens-gsy6 / dotlens-uk3e; `src/DotLens/Tools/Analysis/UsageAnalysis.cs`, `FrameworkEntryPoints.cs`
- **What happened:** A member is used only when code reachable from a root references it. Roots: entry points, module initializers, a built-in list of framework attributes (xUnit/NUnit/MSTest, ASP.NET Core, Blazor, serializers, MEF, BenchmarkDotNet) plus the new entryPointAttributes parameter, public API in every project kind, internals when InternalsVisibleTo names an assembly outside the solution, overrides and implementations of used types or called members, and types handed to frameworks by typeof or written generic type arguments. New reports appear for code reachable only from dead code (for example a public member whose only caller is unused).
- **Options:**
  - A. Keep reachability with these roots.
  - B. Treat public members as roots only in libraries (reports more dead public code in executables; changes an existing multi-target expectation).
- **Recommendation:** A. The rules lean toward not reporting when evidence is incomplete, which suits a tool users act on by deleting code.
