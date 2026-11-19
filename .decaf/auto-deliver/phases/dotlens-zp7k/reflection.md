# Directive-split using lists — dotlens-zp7k, dotlens-s5uh (2026-11-19)

Both refined by the operator mid-run (zp7k from the draft filed during dotlens-2hzp; s5uh split from it).

## Acceptance results

- [run] `--filter-method "*OrganizeUsings*"`: 219/219, exit 0 (44 split shapes × 4 option combinations equal Formatter.OrganizeImportsAsync).
- [run] `--filter-method "*AddUsing*"`: 148/148, exit 0 (21 split shapes; existing usings keep order; output a fixed point of Organize Imports).
- [run] `rg -n "roslynSortsInput" tests/DotLens.Tests`: no output.
- [manual] organize_usings description: inspected — "A list that a preprocessor directive splits keeps its order, as the IDE's Organize Usings leaves it." Held below for the wording call.
- [run] full suite (`--max-threads 8`): 1875/1875, exit 0.

Commit 08625a5. Review: bugs (APPROVED; 33 extra probe shapes all matched Roslyn; 1 Medium test cost fixed with a class fixture, 2m 04s → 7.9 s); fix-verifier ADDRESSED.

## Deviations

- None from the nibs. The implementer's pre-coding Roslyn harness found two split-rule cases (emptied run, directive inside a using) before any review did.

## Follow-ups

None. Awareness: with separate groups on, Roslyn's own Organize Imports is not a fixed point for usings sharing a line (a second pass adds another blank line); DotLens matches its first pass.

## Open questions (filed: dotlens-5he7)

### Wording of the organize_usings sentence on directive-split lists
- **Kind:** manual-check
- **From:** dotlens-zp7k; `src/DotLens/Tools/Formatting/OrganizeUsingsTool.cs:27`
- **What happened:** The nib's [manual] criterion asks for one sentence saying that a list a directive splits keeps its order, and calls the wording a judgment call. The run added: "A list that a preprocessor directive splits keeps its order, as the IDE's Organize Usings leaves it."
- **How to check:** Read the organize_usings description in OrganizeUsingsTool.cs.
- **Passing looks like:** The sentence tells an agent that the tool will not reorder such a list, in wording you accept.
