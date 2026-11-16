# dotlens-fp1m — find_unused_code reports an extension block as an unnamed unused class (2026-11-16)

## Acceptance results

- `--filter-method "*FindUnusedCode_Extension*"`: 2/2. The nib's original filter (`*.FindUnusedCode*Extension*`) was invalid for xUnit v3 (middle wildcard, exit 5); rewritten before implementation, same for the other bug nibs.
- Full suite: 1017/1017, both capped at 8 threads and uncapped, on a quiet machine. Two earlier runs failed 3 E2E tests on an MCP initialize timeout while an unrelated `vitest run` in another repo saturated the machine; base and filtered runs during the same period passed, which looked like a regression but was load.

## Deviations

- BREAKDOWN skipped: a bug-sized item runs as one unit.
- Added an `extension` value to the polymorphic SymbolResponse output (MCP schema change). TypeKindDto and find_unused_code's counts already treated blocks as their own kind.

## Follow-ups

dotlens-y9u9 (empty block names in other tools), dotlens-ur5r (outline omits block headers), dotlens-fkf7 (`search_symbols "*"` returns global namespace and generated accessors), dotlens-pvb8 (static extension containers reported unused). Filed outside this run's scope list; not adopted.

## Open questions (filed: dotlens-qw76)

### Extension blocks get a new `extension` kind in tool output
- **Kind:** confirm
- **From:** dotlens-fp1m; `src/DotLens/Tools/Shared/SymbolResponse.cs`, `src/DotLens/Infrastructure/RoslynHelpers.cs`
- **What happened:** Tools reported an extension block as a `class` with an empty name. The fix adds an `ExtensionSymbolResponse` with discriminator `extension` to the polymorphic symbol output, which MCP clients see as a new value in the schema. The alternatives were the existing `unknown` fallback, which is meant for kinds DotLens does not handle, or keeping `class` with a filled-in name, which is wrong.
- **Options:**
  - A. Keep the new `extension` kind.
  - B. Report blocks as `unknown` with a name — no schema change, but `unknown` then covers a fully understood kind.
- **Recommendation:** A. `TypeKindDto` already has `Extension`, so the output now agrees with it. Not checked: whether any MCP client switches exhaustively on the symbol kind.
