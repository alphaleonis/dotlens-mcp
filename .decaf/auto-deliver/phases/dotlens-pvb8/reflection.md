# dotlens-pvb8 — find_unused_code reports static classes with used extension members as unused (2026-11-16)

## Acceptance results

`--filter-method "*FindUnusedCode_ExtensionContainer*"`: 6/6. Full suite: 1206/1206 with `--max-threads 8`.

## Deviations

- Same-tool bugs fixed in this change: self-references (qualified and plain) no longer make a type or member used; entry points and module initializers count as used (`Main` used to be reported); doc-comment and attribute self-references; multi-target file matching (new MultiTargetTestProject fixture).
- Three review rounds (NEEDS_CHANGES with 2 High, re-review, fix check).
- Accepted at the cap: in `[D(nameof(Right))] int Left, Right;` the shared attribute is part of Right's own text, so it does not count as a use of Right — consistent with the rule that an attribute on a declaration belongs to every field in it.

## Follow-ups

dotlens-gsy6 (reflection-invoked members), dotlens-uk3e (mutually recursive members).

## Open questions

### find_unused_code no longer counts a member's or type's references to itself
- **Kind:** confirm
- **From:** dotlens-pvb8; `src/DotLens/Tools/Analysis/FindUnusedCodeTool.cs`
- **What happened:** The tool now reports a method that only calls itself, a class named only inside itself (e.g. `Chain? Next`), and a member cited only by its own doc comment or its own attribute. Before, any reference counted. A doc-comment `cref` from elsewhere in the class still counts as a use. Mutually recursive members (A calls B, B calls A, nothing else) still count as used; that needs a reachability pass (dotlens-uk3e).
- **Options:**
  - A. Keep the self-reference rule.
  - B. Go back to "any reference counts" — fewer reports, but self-recursive dead code is never found.
- **Recommendation:** A. A reference from inside the symbol cannot keep it alive. Not checked: how noisy this is on a large real solution.
