# Reference search group — dotlens-cr8j, dotlens-6nks, dotlens-piat (2026-11-18)

## Acceptance results

- [run] `*LinkedSymbol_NoDuplicateReferences*`, `*FindReferences_SourceGenerated*`, `*FindReferences_RefAndAttributeWrites*`: 13/13, exit 0.
- [run] full suite (`--max-threads 8`): 1482/1482, exit 0.

Commit baaf4c9. Review: bugs (APPROVED, 1 Medium) — fixed with a parked same-area item and analyze_change_impact's IsGenerated; fix check ADDRESSED.

## Deviations

- New fixture project ReferenceSearchTestProject (net10.0;net11.0). Three linked-indexer test expectations changed from three uses to one.
- Generated references from a generator whose output differs per target framework appear once per target (their files differ).

## Follow-ups

None. Awareness: same-span deduplication assumes linked copies share text, which sync_documents keeps true since dotlens-9zjg.
