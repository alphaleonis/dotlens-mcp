# dotlens-c293 — add_using misjudges static, alias and global usings; organize_usings reformats the whole file (2026-11-18)

## Acceptance results

- [run] `*UsingTools_Scope*`: 39/39, exit 0.
- [run] full suite (`--max-threads 8`): 1388/1388, exit 0.

Commit 5de4d65. Reviews: bugs (APPROVED, 2 Medium + 1 Low), bugs re-review (APPROVED, 1 Low); all fixed; fix check ADDRESSED, no breakage.

## Deviations

- organize_usings no longer runs the formatter at all; two existing test expectations that encoded formatter side effects changed.
- add_using refuses type names (InvalidParameter).
- Global usings always sort first; namespace-scoped using lists are organized and can receive new usings, `global::`-qualified when the first identifier binds differently there.

## Follow-ups

dotlens-4cfl (culture-sensitive sort, unvalidated namespace argument, extern alias collision, U+2028/U+0085 line breaks, whole-compilation diagnostics per document).

## Open questions

### organize_usings no longer formats anything
- **Kind:** confirm
- **From:** dotlens-c293; `src/DotLens/Tools/Formatting/OrganizeUsingsTool.cs`
- **What happened:** To leave code outside the usings byte-identical, the formatter was removed entirely; a span-limited format re-indented namespace usings with the formatter's options (spaces in a tab-indented file). Layout of the usings themselves (indentation, line breaks) is now handled by `UsingDirectiveLayout`.
- **Options:**
  - A. Keep no formatter.
  - B. Format only the using lists with options read from the file's .editorconfig.
- **Recommendation:** A. The layout helper already keeps the file's indentation and line breaks, and B reintroduces a dependency on formatter options for no visible gain.

### add_using refuses names that bind to a type
- **Kind:** confirm
- **From:** dotlens-c293; `src/DotLens/Tools/Refactoring/AddUsingTool.cs`
- **What happened:** With static usings no longer counting as the namespace, `add_using System.Math` would write `using System.Math;`, which does not compile; the tool now returns InvalidParameter for a type name.
- **Options:**
  - A. Keep the refusal.
  - B. Write `using static` for a type name.
- **Recommendation:** A. The tool is documented as adding a namespace; a static import is a different request a caller should make explicitly.

### A using added inside a namespace may be written global::-qualified
- **Kind:** confirm
- **From:** dotlens-c293; `src/DotLens/Tools/Refactoring/AddUsingTool.cs`
- **What happened:** When a file keeps its usings inside a namespace declaration, add_using adds there, and writes `using global::X;` when `X` would bind to a different symbol inside that namespace.
- **Options:**
  - A. Keep `global::` qualification in that case.
  - B. Put the using at file level instead when the name would collide.
- **Recommendation:** A. It keeps the file's own style (usings inside the namespace) and the result binds as requested.
