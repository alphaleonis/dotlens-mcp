# CLAUDE.md

## Project Overview

DotLens is an MCP server providing a comprehensive suite of AI-optimized tools for .NET/C# semantic code analysis using Roslyn. Two-process architecture: server manages worker processes (one per loaded solution), workers hold MSBuildWorkspace file locks.

**Solution file**: `DotLens.slnx`

## Build & Test

**Task runner**: `just` (see `justfile` for all recipes). Common tasks:
- `just build` / `just test` / `just check` (build + test)
- `just format` / `just format-check`
- `just notices` — regenerate `THIRD-PARTY-NOTICES.txt`
- `just pack` — build Release NuGet package

```bash
dotnet build src/DotLens/DotLens.csproj
dotnet test tests/DotLens.Tests/DotLens.Tests.csproj
```

## Key Structure

- `src/DotLens/Program.cs` - Entry point (server mode default, `--worker` flag for worker mode)
- `src/DotLens/Tools/` - Tool classes with `[McpServerTool]` attributes
- `src/DotLens/Infrastructure/MultiWorkerManager.cs` - Manages multiple worker processes
- `src/DotLens/Infrastructure/WorkspaceManager.cs` - MSBuildWorkspace lifecycle (worker-side)
- `build/` - Build scripts (e.g., `generate-notices.cs` run via `dotnet run file.cs`)
- `.config/dotnet-tools.json` - Local dotnet tool manifest (`dotnet tool restore` to install)

## Coding Standards

See `.editorconfig` for formatting rules. Key points:
- 3-space indentation
- Private fields: `_camelCase`
- XML doc comments: Only where they add value (public APIs, non-obvious behavior). Skip for self-explanatory methods.

### Correctness Over Convenience
- **Always favor correctness over the easy way out.** Even rare edge cases matter - they lead to the hardest bugs to find later.
- Don't dismiss edge cases as "unlikely" or "good enough for most uses" - handle them properly.
- When reviewing code, if you identify a potential edge case issue, fix it rather than documenting it as a known limitation.
- Correctness is key. Users trust this tool for accurate code analysis.
- **Never change a test to accommodate a bug in code.** If a test reveals a bug, keep the test and create a task to fix the bug.

### Test Assertions
- **Assert exact expected values, not just existence or minimums.** Weak assertions hide bugs.
  ```csharp
  // Bad: Only checks existence, passes even if wrong data returned
  Assert.NotNull(result.Data);
  Assert.True(derivedTypes.Count >= 1);
  Assert.True(data.GetInt("callerCount") >= 0);  // "non-negative" is not a real assertion
  Assert.Contains("ConcreteClass", derivedTypeNames);  // Passes even with extra/wrong items

  // Good: Asserts exact expected values
  Assert.Equal(2, derivedTypes.Count);
  Assert.Equal(0, data.GetInt("callerCount"));  // Expect exactly 0 callers
  Assert.Equivalent(["ConcreteClass", "DerivedClass"], derivedTypeNames);  // Exact match
  ```

- **Prefer `Assert.Equal` over `Assert.Contains` for strings.** `Assert.Contains` is a code smell - it passes even if the string has unexpected content.
  ```csharp
  // Bad: Passes even if signature is "WRONG_public void Calculate(int a, int b)"
  Assert.Contains("public", signature);
  Assert.Contains("Calculate", signature);

  // Good: Exact match when possible
  Assert.Equal("public int Calculate(int a, int b)", signature);

  // Acceptable: When format varies but key content must exist (e.g., generated code)
  Assert.Contains("NotImplementedException", generatedStubCode);
  ```

- **Use `Assert.Equivalent` for unordered collection comparisons.** This checks that collections contain the same elements regardless of order.
  ```csharp
  // Good: Order-independent collection comparison
  Assert.Equivalent(["Method1", "Method2"], actualMethodNames);
  ```

- **Use the JsonNode extension methods** from `DotLens.Tests.Helpers`:
  - `GetString`, `GetInt`, `GetBool`, `GetArray`, `GetObject` - throw if missing (use for required fields)
  - `GetStringOrNull`, `GetIntOrNull`, etc. - return null if missing (use for optional fields)
  - **These methods accept nullable JsonNode** - no null-forgiving operator needed
  ```csharp
  // Good: Clear intent, fails fast if field missing, no ! needed
  var memberCounts = data.GetObject("memberCounts");
  Assert.Equal(5, memberCounts.GetInt("methods"));

  // Good: Works with nullable array elements - no ! needed
  var names = items.Select(t => t.GetString("name")).ToList();

  // Bad: Verbose, unclear if field is required, unnecessary !
  var memberCounts = data["memberCounts"];
  Assert.NotNull(memberCounts);
  var methodCount = memberCounts!["methods"]?.GetValue<int>() ?? 0;
  ```

### Case Sensitivity Consistency
- When a `HashSet` or `Dictionary` uses `StringComparer.OrdinalIgnoreCase`, all lookups against the same domain must also be case-insensitive. Mixing case-insensitive collections with case-sensitive `Contains`/`Equals` creates silent behavioral gaps.

### Exhaustive Switches
- **Always add `default: throw` to switch statements on enums.** Prevents silent no-ops when new enum values are added.
  ```csharp
  default:
     throw new InvalidOperationException($"Unexpected value: {value}");
  ```

### Reflection
- **Cache reflection results** for static type metadata (properties, attributes). Use `static readonly Lazy<T>` fields rather than re-querying on each call.
- **Share instances** when reading default values via reflection -- create one instance outside the loop, not one per property.

### Test Completeness
- **Assert ALL return values** -- never discard the return value of a method under test. Even if the test focuses on a side effect (e.g., stderr output), also assert the return value.
- **Guard against vacuous loops** -- if a test iterates a collection with assertions inside the loop, add `Assert.NotEmpty(collection)` before the loop to prevent silent zero-iteration passes.

### Async/Await
- **NEVER** use `.Result` or `.Wait()` on tasks - causes deadlocks in MCP server context
- Always use `await` for async operations
- If sync access is truly unavoidable, use `GetAwaiter().GetResult()` with a comment explaining why
- **Name `CancellationToken` parameters `cancellationToken`** - not `ct` or other abbreviations. This is the convention in this project for consistency.

### Null Safety
- **Avoid null-forgiving operators (`!`)** - they suppress warnings without ensuring safety. Prefer:
  - Null checks with early returns or exceptions
  - `??` or `??=` for defaults
  - Pattern matching (`is not null`, `is { } value`)
  - Restructuring to avoid nullability
  ```csharp
  // Bad: Suppresses warning, crashes if null
  var name = symbol.ContainingType!.Name;

  // Good: Explicit handling
  var name = symbol.ContainingType?.Name ?? "(global)";

  // Good: Guard with meaningful error
  if (symbol.ContainingType is not { } containingType)
     return ToolResult.Error("Symbol must have a containing type");
  ```

### Global Usings
- **Prefer `GlobalUsings.cs` over per-file usings** for commonly used namespaces. This reduces boilerplate and ensures consistency.
- When you find yourself adding the same `using` statement to multiple files, consider adding it to `GlobalUsings.cs` instead.
- Keep `GlobalUsings.cs` organized with comments grouping related namespaces.

### Raw Strings
- **Prefer raw string literals (`"""`)** for multi-line strings, especially tool descriptions
- Raw strings preserve formatting and avoid escape sequence clutter
  ```csharp
  // Good: Raw string for tool description
  [Description("""
     Searches for symbols matching the query pattern.
     Supports wildcards: * (any chars), ? (single char).
     Returns up to maxResults matches sorted by relevance.
     """)]

  // Bad: Concatenated strings or escaped newlines
  [Description("Searches for symbols matching the query pattern.\n" +
     "Supports wildcards: * (any chars), ? (single char).")]
  ```

## Tool Naming Conventions

- Public API tools (e.g., `find_references`, `search_symbols`) are exposed to MCP clients for code analysis, navigation, refactoring, etc.
- `worker_*` - Internal tools used by server to communicate with worker processes. Not intended for direct client use.

## Tool Attribute Design

All tools should set appropriate `[McpServerTool]` attributes to help clients understand tool behavior. These hints enable clients to make informed decisions about tool usage, auto-approval, and safety.

### Available Attributes

| Attribute | Default | Description |
|-----------|---------|-------------|
| `Name` | method name | Tool identifier (snake_case) |
| `Title` | - | Human-readable display name |
| `ReadOnly` | `false` | `true` if tool only reads, never modifies |
| `Destructive` | `true` | `false` if tool only adds, never deletes/overwrites |
| `Idempotent` | `false` | `true` if repeated calls with same args have no additional effect |
| `OpenWorld` | `true` | `false` if tool operates on a closed, well-defined domain |

### Setting Attributes for DotLens Tools

**Read-only analysis tools** (most DotLens tools):
```csharp
[McpServerTool(Name = "find_references", ReadOnly = true, OpenWorld = false)]
```
- `ReadOnly = true`: Doesn't modify files or workspace state
- `OpenWorld = false`: Operates only on the loaded solution (closed domain)

**Preview/apply refactoring tools** (e.g., `rename_symbol`, `extract_method`):
```csharp
// When preview = true (default)
[McpServerTool(Name = "rename_symbol", Destructive = false, Idempotent = true, OpenWorld = false)]
```
- `Destructive = false`: Preview mode doesn't delete anything
- `Idempotent = true`: Preview with same args returns same result
- Note: The actual write behavior depends on the `preview` parameter at runtime

**File-modifying tools** (when `preview = false`):
```csharp
[McpServerTool(Name = "format_document_batch", Destructive = true, OpenWorld = false)]
```
- `Destructive = true`: Can overwrite file contents
- `OpenWorld = false`: Only affects loaded solution files

**Solution management tools**:
```csharp
[McpServerTool(Name = "load_solution", ReadOnly = false, OpenWorld = false, Idempotent = true)]
```
- `ReadOnly = false`: Modifies workspace state
- `Idempotent = true`: Loading same solution twice is safe

### Best Practices

1. **Always set `OpenWorld = false`** for DotLens tools - they operate on a closed domain (loaded solutions)
2. **Set `ReadOnly = true`** for all navigation/analysis tools (find_references, get_type_members, etc.)
3. **Set `Idempotent = true`** when safe - helps clients retry on transient failures
4. **Set `Destructive = false`** for additive-only operations
5. **Consider runtime behavior** - if a tool has both preview and apply modes, document the worst-case (apply mode) in attributes

## Tool Parameter Design

Tools are consumed by AI agents, so parameters must generate detailed, self-documenting JSON schemas. Follow these principles:

### Use Proper Types
- **Enums over strings**: When a parameter has a fixed set of valid values, use an enum with `[JsonStringEnumMemberName]` attributes. This provides autocomplete and validation in the schema.
  ```csharp
  // Good: Enum with explicit JSON names
  SymbolKindFilter? kind = null

  // Bad: String with values listed in description
  [Description("Filter by kind: Class, Interface, Method...")]
  string? kind = null
  ```

- **Polymorphic types over mutually exclusive parameters**: When parameters are conditionally required based on a "mode", use `[JsonPolymorphic]` with `[JsonDerivedType]` discriminators (see `SymbolLookup` class).
  ```csharp
  // Good: Polymorphic lookup with discriminator
  SymbolLookup lookup  // {"mode": "position", "filePath": "...", "line": 1}

  // Bad: Mutually exclusive nullable parameters
  string? filePath, int? line, int? column, string? symbolName
  ```

### Use Validation Attributes
Use custom validation attributes for integer constraints:

- `[PositiveInteger]` - For >= 1 integers (maxResults, line numbers, etc.)
- `[NonNegative]` - For >= 0 integers (offset, etc.)

```csharp
[PositiveInteger] int maxResults = 50,
[NonNegative] int offset = 0
```

Note: Required parameter validation is handled automatically by the MCP SDK.

### Description Quality
- Keep `[Description]` concise but include examples for complex parameters
- Don't duplicate type information that's already in the schema (e.g., don't list enum values in description)

## DotLens Usage During Development

**IMPORTANT: DotLens MCP tools are installed and available. You MUST use them as your primary tools for ALL C# code navigation, analysis, and refactoring during development.** Do not default to Grep/Read/Glob for C# code when DotLens provides a more accurate semantic alternative. DotLens understands overloads, generics, partial classes, and scoped references -- text search does not.

**Always prefer DotLens over text-based tools:**

| Task | Use DotLens | NOT |
|------|-------------|-----|
| Find a symbol | `search_symbols` | Grep |
| Read a method | `get_method_source` | Read (entire file) |
| Find usages | `find_references` | Grep (false positives) |
| Understand a method | `analyze_method` | Manual file reading |
| Explore a type | `get_type_members`, `get_type_overview` | Grep/Read |
| Check for errors | `get_diagnostics` | `dotnet build` |
| Rename a symbol | `rename_symbol` | Find/replace |
| Understand callers | `find_callers` | Grep |
| See type hierarchy | `get_type_hierarchy`, `get_derived_types` | Grep |

**After using Edit/Write tools on .cs files**, always call `sync_documents` to update the in-memory workspace before running further DotLens queries.

**If any DotLens tool returns an unexpected error or behaves incorrectly:**
1. **Immediately stop** what you are doing
2. **Tell the user** exactly what happened (tool name, input, error message or unexpected output)
3. **Offer to create a nib** to track the issue: `nibs new "DotLens: <brief description>" -t bug --parent dotlens-aqnq -d -`, with the detailed error message and reproduction steps on stdin
4. Only then continue with your original task using alternative approaches if needed

This is critical -- we are actively developing DotLens and every bug report helps improve it. Do not silently fall back to text-based tools when DotLens fails.

### Tool Choice Preference for Type/API Queries

When querying library or framework API information (e.g., "What properties does ISymbol have?"), prefer tools in this order based on token efficiency:

| Priority | Tool | Use When | Token Efficiency |
|----------|------|----------|------------------|
| 1 | **DotLens** | Symbol is in the loaded solution | Excellent (targeted) |
| 2 | **Context7** | Querying library/framework docs | Good (focused) |
| 3 | **WebSearch** | General queries, fallback | Moderate |
| 4 | **Firecrawl** | Need full page content | Poor (full scrape) |

**Notes:**
- DotLens cannot query symbols from NuGet packages (only solution code)
- Context7 is ideal for library documentation queries
- Avoid WebSearch for API questions when Context7 is available
- Firecrawl returns entire page content - use only when full context is needed

## Test Fixtures

The `tests/TestFixtures/` directory contains C# projects used by integration tests. Many tests reference specific line numbers in these files.

**NEVER modify existing .cs files in TestFixtures** - this shifts line numbers and breaks tests. Instead:
- **Add new .cs files** for new test scenarios
- **Add new classes/types in new files** rather than appending to existing files
- If a test needs a specific type, create a new file for it (e.g., `AmbiguityTests.cs` instead of adding to `Classes.cs`)

## Nibs (Issue Tracking)

Issues are tracked with `nibs` in `.nibs/`, which is a separate private git repository.

When creating nibs, **assign them to milestone `dotlens-aqnq`** unless otherwise specified:
```bash
nibs new "Title" -t task --parent dotlens-aqnq
```

**Starting work**: Always mark a nib as `in-progress` when you start working on it:
```bash
nibs set <nib-id> --status in-progress
```

**Completing nibs**: Do NOT close a nib without user approval. When you believe a task is done:
1. State that you deem the work complete
2. Ask the user if they want you to close the nib and commit the changes
3. Wait for explicit approval, then close it with a summary: `nibs close <nib-id> --summary -` (summary on stdin)

## Git Workflow

This project uses a topic branch workflow to ensure all commits to `feature/initial` are in a working state.

### Branch Structure

- **`feature/initial`**: Main development branch. All commits here MUST build and pass tests.
- **`work/<nib-id>-<description>`**: Topic branches for individual tasks (e.g., `work/dotlens-hte4-find-callers-review`)

## Important Notes

**Cross-platform paths**: `Path.GetFileName(@"C:\foo\bar.sln")` returns the full string on Linux (backslash is not a separator). Use `Path.Combine` to construct test paths so they use platform-native separators.

**Document sync**: After using Edit/Write on .cs files, call `sync_documents` to update the in-memory workspace.

