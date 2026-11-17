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

## dotlens-y9u9 / ur5r / fkf7 / pvb8 (2026-11-16)
- Bug queues grow when each fix files its neighbors. Folding same-tool or same-root findings into the current fix (and grouping the rest by tool) kept the count from 10 to 12 to 11 instead of compounding.
- Rules about "inside vs outside a declaration" need the full span (doc comment, attributes) and file-path matching; multi-targeted projects give each target its own syntax tree for the same file.
- A source symbol can have no syntax of its own (generated accessors); any "is this source?" check must fall back to its locations.

## dotlens-tmdg — null-conditional indexer references (2026-11-17)
- Roslyn 5.12's find-references and find-callers miss or misplace indexers after `?`. `ReferenceSearch` (src/DotLens/Tools/Shared) is now the single entry point for both searches; a new tool that needs references should call it, not `SymbolFinder`.
- Multi-targeted and linked files give one symbol per project. A scan that supplements Roslyn must scope by every project compiling the declaring file, match copies by documentation ID, and compare paths case-insensitively as `GetDocumentIdsWithFilePath` does. Two re-reviews each found a gap here.
- `sync_documents` updates one document per path, so linked copies can be stale; never let position alone identify a symbol across copies.

## dotlens-9zjg — sync_documents linked copies (2026-11-17)
- A stale-copy test must read every copy's text before editing the file: Roslyn loads document text lazily, so an unread copy picks up the new file on first read and the test passes without the fix.

## dotlens-jxhi — sync changes leaking into later writes (2026-11-17)
- DotLens keeps its own Solution beside the MSBuildWorkspace's; TryApplyChanges diffs against the workspace's, so any in-memory-only change (sync, added file) is re-applied by the next write. UpdateSolution now keeps the two in step.
- Reviews keep turning up pre-existing bugs next to the fix (12 to 15 open over three bugs); file them grouped by tool so the queue grows by area, not by symptom.

## dotlens-p6xt — using-directive layout (2026-11-17)
- Rewriting a SyntaxList with WithUsings drops the trivia of removed nodes: file headers, directives and blank lines go with them. A layout helper has to move leading trivia explicitly and treat directives as group boundaries.
- A parse-without-diagnostics guard in the test helpers (input and output) caught directive placement errors that exact-text assertions alone did not frame.
- Filtered test runs slowed from seconds to minutes late in the session; check machine load before reading a slow run as a regression.
