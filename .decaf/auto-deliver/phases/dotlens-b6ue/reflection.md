# Declaration display group — dotlens-nvro, dotlens-b6ue (2026-11-18)

## Acceptance results

- [run] `*FullDeclaration_Fidelity*`, `*FileOutline_DeclarationFidelity*`: 24/24, exit 0.
- [run] full suite (`--max-threads 8`): 1584/1584, exit 0.

Commit 9022e6f. Reviews: bugs (APPROVED, 2 Low), bugs re-review (APPROVED, 3M+1L regressions from the folded type-declaration work), bugs roster=3 (APPROVED, 1M regression); all fixed except one parked item, filed.

## Deviations

- Folded in type-declaration detail and the search_symbols accessor/operator listing (see the closure summary).

## Follow-ups

dotlens-mh8u (record IEquatable<Self> shown or dropped by name match).
