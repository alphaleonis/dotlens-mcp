# dotlens-fkf7 — search_symbols '*' returns the global namespace and generated accessors (2026-11-16)

## Acceptance results

`--filter-method "*SearchSymbols_Wildcard*"`: 2/2. Full suite: 1185/1185 with `--max-threads 8`.

## Deviations

- Same-area bugs fixed in this change instead of filed: accessor names by short container (every type), generated accessors classified as metadata, rename_symbol on an accessor (now refused).
- Rule chosen: listing tools show only explicitly declared source symbols; name lookup still resolves generated members code calls by name (e.g. a record's Deconstruct) but never extension implementation methods.
