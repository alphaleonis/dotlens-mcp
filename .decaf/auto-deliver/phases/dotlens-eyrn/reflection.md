# dotlens-eyrn — compiler-generated callers (2026-11-19)

Filed from the dotlens-mudz review (its test pinned the wrong caller name).

## Acceptance results

- [run] ReferenceSearchFixtureTests 21/21 (auto-property, static, field keyword, lambda, field-like event, record initializers name the declared member), TopLevelStatementsFixtureTests 10/10, ReferenceSearchTests 20/20.
- PositionalRecordLookupTests 25/25 with SquareEdge's caller named SquareArea.
- No compiler-generated member named as caller or containing member: find_callers and get_containing_member fixed for backing fields and <Main>$; find_references and analyze_method name no calling symbol.
- [run] full suite (`--max-threads 8`): 1810/1810, exit 0.

Commit 398f9b0. Reviews: bugs (NEEDS_CHANGES, 1H <Main>$ + 2L, all fixed), bugs re-review (NEEDS_CHANGES, 1H+1M regressions from documentation-ID grouping, fixed), fix-verifier: both ADDRESSED, no breakage.

## Deviations

- Response contract: CallerInfo.CallingSymbol and GetContainingMemberResponse.Member are nullable and omitted from JSON for top-level statements (the convention find_unused_code's referrers already use).
- Grouping by declaration also merges a member's per-target-framework and linked-project copies into one caller.

## Follow-ups

None filed. Awareness: declaration matching compares paths case-insensitively, so two Linux files differing only in case with a same-named member at the same span would merge (pre-existing convention from GetDocumentIdsWithFilePath). find_callers omits nameof uses that find_references lists.

## Open questions

### Callers in top-level statements are reported without a calling member
- **Kind:** confirm
- **From:** dotlens-eyrn; `ReferenceSearch`, `FindCallersTool`, `GetContainingMemberTool`
- **What happened:** Code in top-level statements has no declared member; Roslyn's is the generated `<Main>$`. find_callers and get_containing_member now leave the caller / member field out of the JSON for it. A client that assumed the field is always present must handle its absence.
- **Options:**
  - A. Keep the field absent — matches find_unused_code's existing convention; no invented symbol.
  - B. Report a placeholder (e.g. kind "TopLevelStatements" with the file) — always-present field; a new response shape to document.
- **Recommendation:** A, for consistency with find_unused_code. Not checked: whether any MCP client in use relies on the field.
