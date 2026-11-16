# dotlens-rok2 — Ambiguity error for multiple extension-block indexers (2026-11-16)

## Acceptance results

- `--filter-method "*AmbiguousExtensionIndexer*"`: 11/11.
- Full suite: 1108/1108 with `--max-threads 8`.

## Deviations

- Same root cause, wider fix: candidate listing, `parameterTypes`/`returnType` for indexers, extension-block and type-parameter name segments, bracket-aware dotted-name splitting. Ordinary lookups were checked against the old code by two reviewers; no regressions found.
- Three review rounds (initial, re-review after a 110-line repair, fix check after a 49-line repair).
- The error message format changed: candidates separated by `; `, metadata-style names, optional ` returns T` and ` at File:line:col`. The hint text is the public const `SymbolLookupService.AmbiguityHint`.

## Follow-ups

dotlens-k6gs (type-parameter-count-only differences not selectable by name; the hint says so), dotlens-1mt4 (bare extension block by short path). Note on dotlens-fkf7 about compiler-generated extension implementation methods in lookup results.
