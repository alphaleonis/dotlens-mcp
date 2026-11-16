# auto-deliver lessons

## dotlens-6wmq — Support .NET 11 and C# 15 (2026-11-16)
- The DotLens MCP server in this environment is the installed global tool, not branch code. After a Roslyn upgrade it can be older than the language under test (1.0.1 bundles Roslyn 5.3 and parses `union` as a method). Verify tool behavior on new syntax through tests on branch code, never through the live server.
- `dotnet build` acceptance checks for "0 warnings" must use `--no-incremental`: incremental builds skip the compiler and report 0 warnings for files with real warnings.
- Under SDK 11, MSBuildWorkspace loads `net10.0` fixtures without framework references unless they were restored first; `TestFixtureRestore.EnsureRestoredAsync` handles it. A new fixture loader should call it too.
- Shared-fixture tests (`TestFixtures.sln`) assert counts across projects: put new language-version fixtures in their own solution with a class fixture.
- A fixture file's line numbers are frozen once tests pin them, so warnings in it are fixed in the project file (`NoWarn`) rather than with `#pragma` lines.

## dotlens-fp1m — extension blocks in tool output (2026-11-16)
- xUnit v3 test filters accept wildcards only at the start or end (`*Name*`); a middle wildcard fails with exit 5 before any test runs. Write acceptance filters as `*Prefix_Name*`.
- Run one test process at a time and check machine load first. A heavy job elsewhere (another repo's vitest) made E2E MCP handshakes time out and looked like a regression. Cap with `dotnet test --max-threads 8`.
- Roslyn models an `extension(...)` block as an unnamed `INamedTypeSymbol` with `TypeKind.Extension`. Any symbol-mapping switch without that case misreports it; `RoslynHelpers.GetExtensionBlockName` gives the display name.

## dotlens-3cdx / dotlens-mmtc — references at an indexer (2026-11-16)
- Roslyn reports an indexer reference at the `[` of the argument list, so `FindNode` returns the `BracketedArgumentListSyntax`, not the element access. Code that classifies or resolves a reference must widen to the owning expression first.
- `FindNode(span)` returns the outermost node on a tie; a bare name in a tuple ties with its `ArgumentSyntax`. Use `getInnermostNodeForTie: true` when the identifier is what matters.
- A review orchestrator can end its turn while its own reviewer still runs; resume it with "do not end your turn while the reviewer runs", and stop it once its report is in so no stray test run remains.

## dotlens-rok2 / dotlens-9csy / dotlens-mvyj (2026-11-16)
- A re-review is worth it when a repair round rewrites a message format or a shared path: rok2's second review caught two listed forms that did not round-trip. A test that re-reads every listed candidate turns that claim into a check.
- When a new test would pin a known defect (a delegate declaration without its signature), fix the defect in scope instead of asserting it.
- An `Assert.Skip` on "diagnostic not generated" hid a project that loaded with no framework references. MSBuildWorkspace raises no `WorkspaceFailed` for that; check `MetadataReferences` or the corlib instead.
