# Symbol lookup group — dotlens-k6gs, dotlens-1mt4 (2026-11-18)

## Acceptance results

- [run] `*LookupByName_TypeParameterCountZero*`, `*EachListedCandidateFindsItself*`, `*LookupByName_ExtensionBlockShortPath*`: 65/65, exit 0.
- [run] full suite (`--max-threads 8`): 1556/1556, exit 0.

Commit a2f0f5a. Reviews: bugs (NEEDS_CHANGES, 1 High), bugs re-review (NEEDS_CHANGES, 1 High regression + 1M + 1L), bugs roster=3 (NEEDS_CHANGES, 1 High regression + 1 Minor); all fixed; fix check ADDRESSED with two Lows, the offset-ordering one fixed.

## Deviations

- Deduplication rewritten around one identity rule (see the commit message).
- Unverified: generator output emitted into several projects (no fixture); expected to stay apart and resolve through "in project X".

## Follow-ups

None.

## Open questions (filed: dotlens-0vlu)
### A bare name prefers the non-generic variant
- **Kind:** confirm
- **From:** dotlens-k6gs; `src/DotLens/Infrastructure/SymbolLookup/SymbolLookupService.cs`
- **What happened:** A name without type parameters (`Task`, `Mold.Make`) now selects the non-generic candidate when the candidates differ only in type parameters at the levels the name leaves out, as C# binds `Task`. Before, it reported an ambiguity.
- **Options:**
  - A. Keep the preference.
  - B. Keep reporting an ambiguity and require `Task\`0`-style suffixes for the non-generic one.
- **Recommendation:** A. It matches how C# reads the name, and every other case still reports an ambiguity.
