# dotlens-w6b1 — cognitive complexity vs the SonarSource white paper (2026-11-19)

## Acceptance results

- [run] `*Cognitive*` + `*LabeledBreakAndContinue*`: 11/11, exit 0. LabeledLoops.cs methods report 7.
- [run] full suite (`--max-threads 8`): 1655/1655, exit 0.
- Each deviation has a new fixture file under tests/TestFixtures/CoreTestProject/Analysis/Cognitive*.cs and an exact-value test the implementer saw fail before the fix.
- goto case / goto default decision recorded in the commit message.

Commit a5f2dce. Review: bugs (APPROVED, 1 Medium: file the indirect-recursion nib — done).

## Deviations

- Indirect recursion not counted; split to dotlens-0zzn (draft: needs a decision on a solution-wide call graph).
- Two extra fixes beyond the nib: nested ternaries raise nesting, and deconstructing foreach is counted as a loop.

## Follow-ups

- dotlens-0zzn — Cognitive complexity does not count indirect recursion (draft).

## Open questions

### Cognitive complexity follows the white paper where SonarC# differs
- **Kind:** confirm
- **From:** dotlens-w6b1; `src/DotLens/Tools/Analysis/AnalysisHelpers.cs`
- **What happened:** The implementer compared against SonarC#'s own analyzer (CSharpCognitiveComplexityMetric.cs). Where the white paper and SonarC# disagree, DotLens follows the white paper: goto (including goto case/default) adds +1 with no nesting penalty (SonarC# adds nesting); static local functions count toward the containing method (SonarC# skips them); anonymous methods raise nesting (SonarC# does not); recursion is detected through the semantic model (SonarC# matches by name and argument count). A user comparing DotLens with SonarQube numbers will see differences on such methods.
- **Options:**
  - A. Keep the white paper as the reference — numbers are defensible from the published spec.
  - B. Match SonarC# on each point — numbers agree with SonarQube; the nib's LabeledLoops expectation of 7 would change for goto-heavy code only if nesting applies.
- **Recommendation:** A. The nib names the white paper as the spec; SonarC#'s divergences look like implementation choices, not spec readings. Not checked: whether SonarQube's docs state a reason for the goto nesting penalty.
