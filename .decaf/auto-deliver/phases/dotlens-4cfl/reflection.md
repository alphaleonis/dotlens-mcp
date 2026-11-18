# dotlens-4cfl — using tools: culture-sensitive sort, unvalidated namespace, extern alias collision, U+2028 line breaks (2026-11-18)

## Acceptance results

- [run] `*UsingTools_Leftovers*`: 37/37, exit 0.
- [inspect, executed] organize_usings calls only `semanticModel.GetDiagnostics` (grep of OrganizeUsingsTool.cs); a test pins that a global using used only by another file is kept.
- [run] full suite (`--max-threads 8`): 1425/1425, exit 0.

Commit 3f6876a. Reviews: bugs (APPROVED, 1 Medium@50 + 1 Low), bugs re-review (APPROVED, 1 Medium + 1 Low); all fixed, plus three sort-key leftovers the implementer reported; fix check ADDRESSED, no breakage.

## Deviations

- Sort matches Roslyn's Organize Imports rather than plain ordinal (the nib said "ordinal"; review 1 showed ordinal fights the IDE).
- The U+2028 defect was in EndsWithLineBreak, not GetNewLineAsync.

## Follow-ups

dotlens-zwvv (static and alias usings not grouped as Roslyn groups them).

## Open questions

### Using order follows Roslyn's Organize Imports, not ordinal
- **Kind:** confirm
- **From:** dotlens-4cfl; `src/DotLens/Tools/Shared/UsingDirectiveLayout.cs`
- **What happened:** The nib asked for an ordinal comparison. Ordinal sorts `NUnit` before `Newtonsoft`, unlike the IDE, so the comparer now mirrors Roslyn: identifier by identifier in the invariant culture, ignoring case, accents and width first, then case. A `global::` qualifier is left out of the key (Roslyn sorts it under "global").
- **Options:**
  - A. Keep the Roslyn order.
  - B. Use plain ordinal as the nib said.
- **Recommendation:** A. Matching the IDE avoids organize_usings and Organize Imports reordering each other's output.
