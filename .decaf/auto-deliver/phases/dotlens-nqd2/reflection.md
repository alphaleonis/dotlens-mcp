# dotlens-nqd2 — retargeted partial record declarations (2026-11-19)

## Acceptance results

- [run] `*FullDeclaration_RecordEquatable_RetargetedRecord*`: 4/4, exit 0 (red before the fix: Unwritten, Derived, Other ended with IEquatable<Self>).
- [run] `*FullDeclaration_RecordEquatable*`: 11/11, exit 0.
- [run] full suite (`--max-threads 8`): 1763/1763, exit 0.

Commit 15822c9. Review: bugs (APPROVED, no findings).

## Deviations

- `Other` now expects `public record Other(int X) : IEquatable<string>, IDisposable`; the nib said it keeps its current expectation, but that value held the very compiler-added interface the nib removes. It matches the non-retargeted MemberListsFixtureTests expectation.
- The conductor renamed GetFileOverviewTool's `ct` to `cancellationToken` (reviewer note; named-argument call sites in five test files updated).

## Follow-ups

None.
