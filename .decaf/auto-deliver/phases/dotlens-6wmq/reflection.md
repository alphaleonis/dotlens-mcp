# dotlens-6wmq — Support .NET 11 and C# 15 (2026-11-16)

## Acceptance results

All 11 `[run]` criteria pass on `deliver/dotlens-6wmq` (raw output in `verify.log`):

| Criterion | Result |
|---|---|
| `dotnet --version` | `11.0.100` |
| workflow `dotnet-version` grep | 3 matches, all `'11.0.x'` |
| `dotnet build tests/TestFixtures/CSharp15TestProject` | 0 warnings, 0 errors — **after a fix** (see below) |
| `CSharp15LanguageVersionTests` | 1/1 |
| `Diagnostics_ReportsNoErrors`, `FileOutline_IncludesUnion`, `TypeOverview_UnionDoesNotThrow`, `ControlFlow_LabeledBreak`, `FindReferences_ExtensionIndexer` | each 1/1 |
| `just pack` + runtimeconfig grep | `1` |
| full suite | 1007/1007, 0 skipped |

Fix applied in VERIFY: a clean (`--no-incremental`) build of the fixture showed CS9113 from
`PrimaryConstructors.cs` (an unread primary-constructor parameter, which is the point of that
outline case). Earlier task checks used incremental builds and missed it. Suppressed CS9113 in the
fixture project file (commit after a153de6); a `#pragma` would shift pinned line numbers.

## What was built

| Task | Commit | Notes |
|---|---|---|
| dotlens-vw3o | d6e7e30 | Roslyn 5.12.0, SDK 11.0.100, CI `11.0.x`, `RollForward=Major` on tool **and test project**; fixture solutions restored before load (`TestFixtureRestore`); CA1830 fix; notices regenerated |
| dotlens-rkq7 | 631c092 | `CSharp15TestProject` (net11.0) with its own `.slnx` and class fixture |
| dotlens-me13 | fed638f | Outline: union visit, body-less declarations end in `;`, primary-constructor parameters shown for every type kind |
| dotlens-8vcs | a153de6 | Tests only — Roslyn 5.12 already follows labels; SymbolFinder already finds extension-indexer uses |

## Deviations

- **The earlier probe results were wrong-environment.** The DotLens MCP server running in this
  environment is the installed global tool 1.0.1 (Roslyn 5.3), which parses `union` as a method.
  Its SYMBOL_NOT_FOUND / 0-references / missing-exit-point results did not reflect branch code.
  Only the outline needed a source fix; type overview, control flow and references already worked.
- **Scope widened slightly in me13:** the review found the outline dropped class/struct
  primary-constructor parameters (pre-existing), which the new `;` rendering made look valid.
  Fixed in-task per the project's "fix edge cases" rule.
- **batch-dev Phase-2 explore fan-out skipped:** the declared chain fixed the order.

## Follow-ups

See the RECONCILE section (filed by close-out).

## Open questions (filed: dotlens-u7mq)

### The file outline prints a union as `union Shape(Circle, Square);`
- **Kind:** confirm
- **From:** dotlens-me13; `src/DotLens/Tools/Search/GetFileOutlineTool.cs`
- **What happened:** The task's scope was "don't throw on or omit unions"; union-specific output
  (a `union` keyword, case types) was assigned to dotlens-r1yu. The outline is a source skeleton,
  so the implementer printed the declaration as written — `union` keyword and case list — rather
  than as `struct`. Every other tool still reports a union as a struct. If kept, r1yu should treat
  the outline as already done; if reversed, the outline would print text that is not in the source.
- **Options:**
  - A. Keep it — the outline matches the source; r1yu skips the outline.
  - B. Print it as a struct until r1yu — consistent with other tools, but the outline then shows a declaration that does not exist.
- **Recommendation:** A. The outline's contract is to mirror source text, and B would make it lie.

### The test project rolls forward to a newer major runtime
- **Kind:** confirm
- **From:** dotlens-vw3o; `tests/DotLens.Tests/DotLens.Tests.csproj`
- **What happened:** CI now installs only SDK 11 (`setup-dotnet` `11.0.x`), but the test project
  targets `net10.0`. Whether GitHub's runner images also carry a .NET 10 runtime was not checked.
  `RollForward=Major` was added to the test project so the tests start either way. When a .NET 10
  runtime is present, tests still run on it; on a machine with only .NET 11 they run on 11.
- **Options:**
  - A. Keep it — tests start on any machine with .NET 10 or newer.
  - B. Remove it and install both SDKs in CI — tests always run on the TFM's runtime, but the parent's workflow-grep criterion (every `dotnet-version` is `'11.0.x'`) would need to change.
- **Recommendation:** A. It costs nothing when .NET 10 is present and keeps CI to one SDK. Not checked: the actual runner images.

### CI has not run on the SDK 11 workflows
- **Kind:** manual-check
- **From:** dotlens-vw3o; `.github/workflows/ci.yml`, `.github/workflows/release.yml`
- **What happened:** Every check ran locally on Linux (WSL). The workflow changes have not run on GitHub, and nothing was pushed.
- **How to check:** Push `deliver/dotlens-6wmq` (or open a PR) and watch the CI workflow.
- **Passing looks like:** every CI job green, including the third-party-notices check and the full test run.

## Reconcile

Closed dotlens-6wmq: 11/11 acceptance criteria met, 4 deviations recorded. Follow-ups filed under
dotlens-t76f, ordered after dotlens-6wmq: dotlens-fp1m, dotlens-3cdx, dotlens-mmtc, dotlens-rok2,
dotlens-9csy (bugs) and dotlens-y3c0 (research). No phase injected: each follow-up is a single
bug-sized item.
