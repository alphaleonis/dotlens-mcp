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

## dotlens-c293 — using scope (2026-11-18)
- Removing a whole-document formatter changes the output of tools that leaned on it to clean up trivia (indentation of a using moved off a shared line, line breaks after reordering). Each such cleanup has to be done by the layout code, with a test per indentation style.
- A using inside a namespace binds relative to it; any tool that writes a name into a nested scope has to bind it at the target position, not at file level.
- A reviewer's filtered run can move to the background on a shell timeout and still be running; say so in the review brief, or a second run overlaps it.

## dotlens-4cfl — using sort and name binding (2026-11-18)
- To match an IDE behavior, run Roslyn's own implementation in a test (`Formatter.OrganizeImportsAsync`) and compare against it; reading Roslyn's source got the comparer options wrong twice.
- A culture test proves nothing unless the old comparer orders its inputs differently under that culture; temporarily restore the old code and watch the rows fail.
- An acceptance line about which API a tool calls is not settled by output-equality tests; settle it by inspecting the call site and say so.

## dotlens-zwvv — using grouping (2026-11-18)
- c293, 4cfl and zwvv were one requirement found in three pieces: "order usings as Roslyn's Organize Imports does". When a fix's reviews keep finding the same kind of gap, rewrite the spec to the underlying requirement and its oracle (here Formatter.OrganizeImportsAsync) before filing the next piece.
- Per bug the time split was roughly implementation 11-31 min, review and fix rounds 35-45 min, verification 5 min; the serial one-test-process rule and full-suite runs in every role dominate.

## dotlens-c5qn — workspace document writes (2026-11-18)
- A write that consults MSBuild must decide everything before the workspace writes: two review rounds found regressions where a post-apply evaluation or refusal left disk and the solutions disagreeing. Plan, refuse, then only write.
- `dotnet msbuild -getItem` evaluates the outer build of a multi-targeted project and returns no Compile items; pass -p:TargetFramework per workspace project. Pass -noAutoResponse so a repo's Directory.Build.rsp cannot change the evaluation or drop a binlog.
- Under the operator's new rule, implementers and reviewers ran filtered tests only; the conductor's single full suite (1466) passed first time.

## Reference search group — cr8j, 6nks, piat (2026-11-18)
- First area group: one implementer, one review, one fix round and one full suite closed three bugs in about 25 minutes, against 55-80 minutes per bug before.
- Ref-ness of an argument comes from the parameter (IArgumentOperation.Parameter.RefKind), not the argument's keyword: ref passed to an in or ref readonly parameter reads.

## Symbol lookup group — k6gs, 1mt4 (2026-11-18)
- Identity rules for "same symbol" are the regression magnet: each round fixed one case and broke another (cross-project hiding, BCL duplicates per framework, linked files). Enumerate the identity cases first (same declaration in several projects, same name in different declarations, source vs metadata copy, framework builds) and test all of them before choosing a key.
- A string minimum over "PATH:offset" orders offsets as text; compare structured keys.

## Declaration display group — nvro, b6ue (2026-11-18)
- Folding an implementer's out-of-scope finding into the fix round is cheap only when it is small: the folded type-declaration work (base lists, constructed generics, metadata interfaces) caused every regression the later reviews found. Fold mechanical leftovers; file design-sized ones.
- Metadata lists a base class's interface on a derived type only when the derived type re-implements it; filtering by the base's AllInterfaces hides real declarations.

## WorkspaceManager group — 33p3, 57wx (2026-11-18)
- A "project is broken" heuristic needs a positive signal of the broken state (no assets file naming the project), not only the symptom (no System.Object), or by-design projects get false warnings.
- A single review plus a cheap fix check sufficed when the fix round was narrow predicates and docs; the costly multi-round groups were the ones where the fix round added new behavior.

## Type members group — iqdg, p0yq, ynf5, mh8u (2026-11-19)
- Check a nib's stated premise against the source of truth before briefing it as a decision: the brief repeated the nib's wrong reading of the C# spec, and the implementer rightly refused it.
- When a heuristic keeps producing review findings round after round, replace it with the simplest rule that never loses information and record the trade-off as an open question.

## find_unused_code group — gsy6, uk3e (2026-11-19)
- For a tool whose output users delete code on, state the bias up front ("a false unused report is worse than a missed one") in every brief: two rounds of narrowing a rule for precision each brought back false positives until the brief said so.
- Rules about "is this type handed to a framework" belong to the semantic model, but the syntax walk that finds the owning construct must stop at the reference's own type-name path, or every enclosing generic call matches.

## dotlens-jadz (2026-11-19)
- An end-to-end test through the real server and worker found a startup bug (worker spawn under `dotnet DotLens.dll`) that every in-process test missed; the server/worker boundary needs at least one such test per reply shape.

## dotlens-w6b1 — cognitive complexity (2026-11-19)
- A spec-conformance bug goes fastest when the implementer reads the reference implementation (SonarC# via gh api) beside the spec: it settled two open decisions and exposed where the reference itself departs from the spec. One implementer pass and one approving review, about 20 minutes.
