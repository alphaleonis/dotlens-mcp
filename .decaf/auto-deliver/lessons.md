# auto-deliver lessons

## dotlens-6wmq — Support .NET 11 and C# 15 (2026-11-16)
- The DotLens MCP server in this environment is the installed global tool, not branch code. After a Roslyn upgrade it can be older than the language under test (1.0.1 bundles Roslyn 5.3 and parses `union` as a method). Verify tool behavior on new syntax through tests on branch code, never through the live server.
- `dotnet build` acceptance checks for "0 warnings" must use `--no-incremental`: incremental builds skip the compiler and report 0 warnings for files with real warnings.
- Under SDK 11, MSBuildWorkspace loads `net10.0` fixtures without framework references unless they were restored first; `TestFixtureRestore.EnsureRestoredAsync` handles it. A new fixture loader should call it too.
- Shared-fixture tests (`TestFixtures.sln`) assert counts across projects: put new language-version fixtures in their own solution with a class fixture.
- A fixture file's line numbers are frozen once tests pin them, so warnings in it are fixed in the project file (`NoWarn`) rather than with `#pragma` lines.
