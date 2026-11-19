# dotlens-mudz — position lookup at a positional record parameter (2026-11-19)

Filed mid-run at the operator's request (found while fixing dotlens-0zzn).

## Acceptance results

- [run] `PositionalRecordLookupTests`: 25/25, exit 0 — round trip of every positional property search_symbols reports (13) through find_callers and find_references; parameter-asking lookups still resolve the parameter.
- Rule for the ambiguous position stated in commit 1fed1c3 and covered by tests.
- [run] full suite (`--max-threads 8`): 1789/1789, exit 0.

Review: bugs (APPROVED, 3 Low: doc fixed, commit-message rule done, backing-field caller name filed).

## Deviations

- Lookups that accept any symbol at the parameter identifier now resolve the property, not the parameter (the brief asked to keep them; acceptance #1 for find_references requires the change). The reviewer probed get_symbol_info, go_to_definition and analyze_change_impact before/after: none worse.
- One existing expectation in SymbolLookupIdentityTests changed: it described this bug.

## Follow-ups

- dotlens-eyrn — find_callers names a compiler-generated backing field as the caller of a read in a property initializer.
- Raised with the operator, not filed (pre-existing): rename_symbol preview reports the whole file as one change at 1:1; get_type_members reports a line but no column; a parameter's symbol response reports kind "unknown".

## Open questions (filed: dotlens-5he7)

### A position lookup at a positional parameter now returns the property
- **Kind:** confirm
- **From:** dotlens-mudz; `SymbolLookupService.FindByPositionAsync`
- **What happened:** At the identifier of a record's positional parameter, the compiler declares two symbols: the parameter and the property synthesized from it. Tools that accept any symbol there (get_symbol_info, go_to_definition, find_references, rename_symbol, analyze_change_impact) used to get the parameter and now get the property, because search_symbols reports the property at that location. References and renames are unchanged because Roslyn links the two; get_symbol_info now shows the property's type and accessors; analyze_change_impact reports the property's kind.
- **Options:**
  - A. Keep the property — every location search_symbols reports round-trips.
  - B. Return the parameter to unfiltered lookups — older behavior; find_references at the position resolves the parameter again.
- **Recommendation:** A. No tool output got worse in the reviewer's before/after probes.
