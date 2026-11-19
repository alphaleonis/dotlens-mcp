# dotlens-c5qn — removing a document deletes a shared file or throws on SDK-included files (2026-11-18)

## Acceptance results

- [run] `*ApplyChanges_RemoveDocument*` + `*ApplyChanges_AddDocument*`: 30/30, exit 0.
- [run] full suite (`--max-threads 8`, conductor only per the operator's new rule): 1466/1466, exit 0.

Commit f7cabe9. Reviews: bugs (APPROVED, 1M+2L), bugs re-review (APPROVED, 2M+2L; one a regression), bugs roster=3 (NEEDS_CHANGES, 1H+2M, all regressions); final round reworked the plan so every decision precedes any write; fix check ADDRESSED with 2 Low notes (filed).

## Deviations

- Folded in the add-document NETSDK1022 defect (same area).
- New subprocess: `dotnet msbuild -getItem:Compile` per target framework, run by the SDK the workspace loaded, with half the request timeout.
- Reflection write to `Workspace._latestSolution`.
- New refusals (see the closure summary).

## Follow-ups

dotlens-57wx (partial TryApplyChanges failure, no cancellation, placeholder leftovers, SDK outside known installations).

## Open questions (filed: dotlens-0vlu)
### Write tools now run dotnet msbuild and write Workspace._latestSolution by reflection
- **Kind:** confirm
- **From:** dotlens-c5qn; `src/DotLens/Infrastructure/DocumentChangePlan.cs`, `EvaluatedCompileItems.cs`, `WorkspaceManager.cs`
- **What happened:** To know whether a file comes from the SDK glob, an import or the project file, every write that adds or removes a document runs `dotnet msbuild -getItem:Compile` once per target framework (about 0.4 s each on small projects), and placeholder files stand in for added files during that evaluation. To keep removals away from MSBuildWorkspace, ApplyChanges sets the private `Workspace._latestSolution` (checked by name and type; fails with a named error if Roslyn changes it).
- **Options:**
  - A. Keep both.
  - B. Drop the evaluation and refuse every removal of a file the project file does not list, and every addition in an SDK project.
- **Recommendation:** A. B would make code actions that move or add files unusable in SDK projects.

### Removals and additions DotLens cannot express are refused
- **Kind:** confirm
- **From:** dotlens-c5qn; `src/DotLens/Infrastructure/DocumentChangePlan.cs`
- **What happened:** Multi-entry (`Include="A.cs;B.cs"`) and property-based Compile items, files an import includes, removal from only some target frameworks of a project file, and an added file the project file already lists now refuse with APPLY_CHANGES_FAILED instead of being edited by MSBuild.
- **Options:**
  - A. Keep the refusals.
  - B. Let MSBuild edit multi-entry and property-based items as before.
- **Recommendation:** A. MSBuild's edit of those items is the path that threw on glob items; a clean refusal is safer than a partial edit.
