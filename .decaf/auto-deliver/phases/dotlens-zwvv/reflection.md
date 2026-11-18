# dotlens-zwvv — static and alias usings not grouped as Roslyn groups them (2026-11-18)

## Acceptance results

- [run] `*UsingTools_Grouping*`: 11/11, exit 0.
- [run] full suite (`--max-threads 8`): 1436/1436, exit 0.

Commit c9443a7. Reviews: bugs (APPROVED, 1 Low), bugs re-review (APPROVED, no findings). The Low plus two same-root leftovers (generic static usings, extern alias order) fixed in scope.

## Deviations

- A plain and a generic type of the same name sort plain first; Roslyn's own order there depends on input order.
- organize_usings keeps a blank line between extern aliases and usings that Roslyn removes when it reorders extern aliases.

## Follow-ups

None.
