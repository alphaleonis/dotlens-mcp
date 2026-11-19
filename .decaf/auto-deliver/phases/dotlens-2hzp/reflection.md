# dotlens-2hzp — using tools follow .editorconfig import options (2026-11-19)

## Acceptance results

- [run] `*UsingTools_EditorConfig*`: 97/97, exit 0 (four option combinations; organize_usings equals Formatter.OrganizeImportsAsync, add_using output is its fixed point).
- [run] `rg -n "System\.\* first" src/DotLens`: no output.
- [run] full suite (`--max-threads 8`): 1754/1754, exit 0.

Commit 60c2a04. Reviews: bugs (APPROVED, 2M+1L, all fixed test-first), bugs roster=3 re-review (APPROVED, 2 Minor Roslyn divergences, fixed), fix-verifier: both ADDRESSED, no breakage.

## Deviations

- At default options, an unsorted list holding blank lines now loses them, as Roslyn does; the nib's exact-parity acceptance outranks its "output stays what it is today" sentence (the reviewer agreed).
- Options are read from AnalyzerConfigOptionsProvider, because Document.GetOptionsAsync's GenerationOptions is internal in Roslyn 5.12.

## Follow-ups

- dotlens-zp7k — organize_usings sorts within directive-split using lists that Roslyn leaves unsorted (draft; pre-existing behavior).

## Open questions

### Organize usings drops blank lines from unsorted lists at default options
- **Kind:** confirm
- **From:** dotlens-2hzp; `src/DotLens/Tools/Shared/UsingDirectiveLayout.cs`
- **What happened:** The nib asked both for exact parity with Roslyn's Organize Imports and for unchanged output at default options. These conflict for one input: an out-of-order using list with blank lines between usings. Roslyn drops those blank lines when it sorts; DotLens used to keep them, moving each with its using. The run chose parity, so organize_usings and add_using now drop them too. Sorted lists and lists without blank lines come out exactly as before.
- **Options:**
  - A. Keep parity — DotLens and the IDE produce the same file, so they never reorder each other's usings.
  - B. Keep the blank lines at defaults — closer to the old output, but the IDE's Organize Imports would rewrite DotLens's result.
- **Recommendation:** A. The nib's stated goal is that DotLens and the IDE stop reordering each other's usings, which only A gives.
