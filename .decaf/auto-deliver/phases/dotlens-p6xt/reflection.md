# dotlens-p6xt — Using-directive tools write CRLF into LF files and leave a blank first line (2026-11-17)

## Acceptance results

- [run] `*UsingTools_LineEndings*`: 98/98, exit 0.
- [run] full suite (`--max-threads 8`): 1349/1349, exit 0.

Commit 49df3cd. Reviews: bugs (APPROVED, 1 Medium), bugs re-review (APPROVED, 2 Medium + 2 Low; one Medium a regression from round 1), bugs roster=3 (NEEDS_CHANGES: 1 pre-existing High + 1 Minor regression); all fixed except #2 (anchor 50, filed); fix check ADDRESSED, no breakage.

## Deviations

- Scope grew within the same two tools and the same root cause (trivia around the using list): headers, #nullable/#pragma deletion, directive handling, same-line comments, formatter line endings. See the closure summary.
- New refusal: add_using returns NOT_SUPPORTED when a #define/#undef inside an open #if would follow the new using.

## Follow-ups

dotlens-c293 (duplicate check for static/alias/global/namespace-scoped usings; organize_usings reformats the whole file; whole-file #if placement).

## Open questions (filed: dotlens-0vlu)
### add_using refuses when the new using would precede a #define inside an #if
- **Kind:** confirm
- **From:** dotlens-p6xt; `src/DotLens/Tools/Shared/UsingDirectiveLayout.cs`
- **What happened:** A using placed above an `#if` that contains `#define`/`#undef` would not compile (a `#define` must precede the first token), so add_using now returns NOT_SUPPORTED and leaves the file unchanged.
- **Options:**
  - A. Keep the refusal.
  - B. Place the using after the `#endif` of that block instead.
- **Recommendation:** A for now. B changes where usings land in files with conditional headers, which is rare and needs its own tests.
