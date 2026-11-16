# dotlens-mmtc — find_references classifies writes through an indexer as Read (2026-11-16)

## Acceptance results

- `--filter-method "*FindReferences_IndexerWrite*"`: 4/4.
- Full suite: 1051/1051 with `--max-threads 8`.
- The criterion's "ReadWrite" kind cannot be expressed: `ReferenceKind` has only Read/Write/Call. Compound assignments and `++`/`--` are asserted as `Write` (see the open question).

## Deviations

- Same root cause, wider fix: property/local deconstruction targets and `?.` assignments are now `Write`; a member initializer with a nested initializer (`Items = { 1 }`) is now `Read`.

## Follow-ups

dotlens-tmdg (Roslyn misses the indexer in `x?[i] = v`), dotlens-piat (ref-taking uses and attribute named arguments still `Read`).

## Open questions (filed: dotlens-qw76)

### Compound assignments and increments are reported as Write, not ReadWrite
- **Kind:** confirm
- **From:** dotlens-mmtc; `src/DotLens/Tools/Navigation/FindReferencesTool.cs`
- **What happened:** The nib's acceptance asked for exact Read/Write/ReadWrite kinds. `find_references` has only three reference kinds (Read, Write, Call), so `x[i] += v` and `x[i]++` are reported as `Write`, matching how the tool already reported them for properties and locals. Adding `ReadWrite` would change the public output enum MCP clients see.
- **Options:**
  - A. Keep Write — no schema change; a client cannot tell `x += 1` from `x = 1`.
  - B. Add a `ReadWrite` kind — more precise, but a schema change, and every compound/increment reference changes kind for existing clients.
- **Recommendation:** A for now. Nothing in the repo needs the distinction; revisit if a client asks for it. Not checked: whether any client filters on Write.
