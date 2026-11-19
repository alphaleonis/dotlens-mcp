# dotlens-ljme — Extension value for the symbol kind filter (2026-11-19)

Single-feature plan; the feature was the phase, so BREAKDOWN was skipped.

## Acceptance results

- [run] `dotnet test ... --filter-method "*KindFilter_Extension*"`: 5/5, exit 0. search_symbols (kind) and find_symbols (kinds) with Extension return exactly the fixture's 20 extension blocks (`AllExtensionBlocks`); other kinds return no block, block members still match Method/Property, classic extension-method classes still match Class.
- [run] full suite (`--max-threads 8`): 1881/1881, exit 0.

Review: bugs, APPROVED with one Low (Extension inserted mid-enum renumbers the integer values the JSON converter still accepts). The conductor moved Extension to the end of the enum; acceptance and full suite ran after the move.

## Deviations

- find_unused_code rejects kind Extension with INVALID_PARAMETER (it never reports blocks, only their unused members). See the open question below.
- SymbolFilterHelper.MatchesKind throws on an undefined filter value instead of matching nothing (project rule for enum switches).

## Follow-ups

None filed. Awareness: the implementer saw the first sync_documents report all 254 documents updated (obj/ and NuGet content files included) after a 4-file edit; no wrong results, not reproduced, not filed.

## Open questions (filed: dotlens-embu)

### find_unused_code refuses the Extension kind instead of returning nothing
- **Kind:** confirm
- **From:** dotlens-ljme; `FindUnusedCodeTool` (symbolKindFilter parameter)
- **What happened:** The kind filter is shared by search_symbols, find_symbols and find_unused_code. find_unused_code never reports extension blocks (decided in dotlens-fp1m), only their unused members, so kind Extension could only ever return an empty list. It now returns INVALID_PARAMETER with a hint to filter by method or property. A client that passes Extension gets an error, not an empty result.
- **Options:**
  - A. Keep the error — the caller learns the filter cannot match, rather than reading "no unused code".
  - B. Return an empty result — no error path, but silently reports nothing.
  - C. Make Extension mean "unused members of extension blocks" — useful, but the filter would no longer name the kind of each reported symbol, unlike every other value.
- **Recommendation:** A. An empty answer from a tool users delete code on reads as "all used". Not checked: whether any client sends a kind list taken from search_symbols to find_unused_code unchanged.
