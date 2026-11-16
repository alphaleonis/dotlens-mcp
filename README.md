# DotLens

[![NuGet](https://img.shields.io/nuget/v/DotLens.MCP.svg)](https://www.nuget.org/packages/DotLens.MCP)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A [Model Context Protocol](https://modelcontextprotocol.io/) (MCP) server that gives AI coding agents deep .NET/C# code analysis capabilities — impact analysis, dead code detection, complexity metrics, data flow analysis, and more — built on [Microsoft Roslyn](https://github.com/dotnet/roslyn).

Complements your agent's built-in LSP support with semantic analysis tools that go beyond standard navigation.

## Quick Start

1. **Install**:
   ```bash
   dotnet tool install -g DotLens.MCP
   ```

2. **Add `.mcp.json` to your project root**:
   ```json
   {
     "mcpServers": {
       "dotlens": {
         "type": "stdio",
         "command": "dotlens",
         "args": []
       }
     }
   }
   ```

3. **Restart your MCP client** (e.g., Claude Code) to load the server.

If your project root contains exactly one `.sln` or `.slnx` file, DotLens auto-loads it on startup — no additional configuration needed.

### What Does DotLens Add Over Native LSP?

AI agents like Claude Code already have built-in LSP support for C# (navigation, diagnostics, references). Both DotLens and the native LSP use Roslyn under the hood. The difference is what they expose:

**LSP** provides standard editor operations — go-to-definition, find references, diagnostics after edits, rename. These run automatically and are tightly integrated.

**DotLens** adds analysis and refactoring tools that the LSP protocol doesn't define:

| Capability | What it does |
|------------|-------------|
| Impact analysis | What breaks if I change this symbol? (`analyze_change_impact`) |
| Dead code detection | Find unused types, methods, and fields across the solution (`find_unused_code`) |
| Complexity metrics | Cyclomatic, cognitive, nesting depth, LOC per method (`get_complexity_metrics`) |
| Data flow analysis | Track variable assignments and usage through a method (`analyze_data_flow`) |
| Control flow analysis | Branching paths and reachability (`analyze_control_flow`) |
| Dependency graph | Project-level dependency visualization with cycle detection (`dependency_graph`) |
| Semantic search | Find symbols by accessibility, modifiers, return type, attributes (`find_symbols`) |
| Compound queries | Signature + callers + outgoing calls in one call (`analyze_method`) |
| Code generation | Generate interface stubs, null checks, apply bulk fixes (`implement_missing_members`, `fix_all`) |

DotLens also overlaps with LSP for navigation (find references, go-to-definition, type hierarchy, etc.). These overlapping tools return **AI-optimized structured JSON** with verbosity control and pagination, which can be more token-efficient than LSP responses for large results.

## Installation

### Via NuGet (Recommended)

```bash
dotnet tool install -g DotLens.MCP
```

Then run with:
```bash
dotlens
```

### Build from Source

```bash
git clone https://github.com/alphaleonis/dotlens-mcp.git
cd dotlens-mcp
dotnet build -c Release
dotnet test
dotnet publish src/DotLens/DotLens.csproj -c Release -o ./publish
```

## Configuration

All options use the `DOTLENS_` prefix for environment variables:

| Environment Variable | Description | Default | Range |
|---------------------|-------------|---------|-------|
| `DOTLENS_SolutionPath` | Solution file (`.sln`/`.slnx`) or directory to search | None | - |
| `DOTLENS_AutoLoadSolution` | Auto-detect solution from working directory | `true` | - |
| `DOTLENS_LogLevel` | Logging verbosity | `Warning` | Verbose, Debug, Information, Warning, Error |
| `DOTLENS_TimeoutSeconds` | Timeout for long-running operations | `30` | 1-600 |
| `DOTLENS_LoadWaitTimeoutSeconds` | Seconds to wait for in-progress load before returning "loading" status | `5` | 1-60 |
| `DOTLENS_MaxDiagnostics` | Maximum diagnostics to return | `100` | 1-10000 |
| `DOTLENS_UseAbsolutePaths` | Use absolute paths instead of relative | `false` | - |
| `DOTLENS_MaxCacheSize` | Maximum documents to cache | `500` | 1-10000 |
| `DOTLENS_CacheExpirationMinutes` | Cache sliding expiration | `30` | 1-1440 |
| `DOTLENS_EnableSemanticCache` | Enable document caching | `true` | - |

Options can also be set via command-line arguments: `dotlens --LogLevel=Debug`

### Solution Auto-Loading

DotLens automatically discovers and loads a solution **in the background** on startup. The MCP server starts accepting connections immediately — no waiting for large solutions to load.

| Condition | Behavior |
|-----------|----------|
| `DOTLENS_SolutionPath` points to a file | Loads that solution directly |
| `DOTLENS_SolutionPath` points to a directory | Searches that directory for solutions |
| One `.sln`/`.slnx` in working directory | Auto-loads it |
| Multiple solutions in working directory | Logs which were found; call `load_solution` to choose |
| No solutions found | Call `load_solution` manually |

If a tool is called before loading completes, it returns a "No solution loaded" error. If `load_solution` is called while auto-load is in progress, it waits up to `DOTLENS_LoadWaitTimeoutSeconds` (default 5s) and returns a "loading" status if still in progress. Use `health_check` to monitor loading progress.

Relative `SolutionPath` values are resolved relative to the working directory. Set `DOTLENS_AutoLoadSolution=false` to disable auto-discovery.

## Tools

### Navigation
| Tool | Description |
|------|-------------|
| `go_to_definition` | Jump to symbol definition |
| `find_references` | All references across solution |
| `find_implementations` | Interface/abstract implementations |
| `find_callers` | Impact analysis — who calls this? |
| `find_overrides` | Find all overrides of a virtual/abstract member |
| `find_base_member` | Find the base/interface member that a method overrides or implements |
| `get_type_hierarchy` | Inheritance chain |
| `get_symbol_info` | Semantic info at position |

### Search
| Tool | Description |
|------|-------------|
| `search_symbols` | Glob pattern search (`*Handler`, `Get*`) |
| `find_symbols` | Find symbols by name and/or semantic filters (kind, accessibility, async, etc.) |
| `get_type_members` | All members by type name |
| `get_derived_types` | Find all subclasses |
| `get_method_info` | Method signatures for all overloads |
| `get_file_outline` | File skeleton with type declarations and member signatures |
| `get_containing_member` | Get enclosing method/property/type at a position |
| `get_attributes` | Find all symbols decorated with a specific attribute |
| `find_string_literals` | Find string literals across the solution |

### Compound Analysis
| Tool | Description |
|------|-------------|
| `get_type_overview` | Full type info in one call |
| `analyze_method` | Signature + callers + outgoing calls + location |
| `get_file_overview` | File summary with diagnostics |
| `get_method_source` | Source code by name (use instead of reading entire files) |
| `get_instantiation_options` | How to create a type |
| `check_type_compatibility` | Can A assign to B? |
| `get_outgoing_calls` | What does this method call? |
| `analyze_change_impact` | What breaks if changed? |
| `get_missing_members` | Interface/abstract members that must be implemented |
| `get_type_members_batch` | Multiple types in one call |

### Analysis
| Tool | Description |
|------|-------------|
| `get_diagnostics` | Compiler errors/warnings |
| `analyze_data_flow` | Variable assignments and usage |
| `analyze_control_flow` | Branching/reachability |
| `get_complexity_metrics` | Cyclomatic, nesting, LOC, cognitive |
| `find_unused_code` | Dead code detection |
| `get_project_structure` | Solution structure |
| `dependency_graph` | Project dependencies |
| `get_project_packages` | NuGet packages referenced by projects |
| `fix_all` | Apply a code fix to all occurrences of a diagnostic |

### Refactoring
| Tool | Description |
|------|-------------|
| `rename_symbol` | Safe rename across solution |
| `get_code_actions` | Available Roslyn refactorings/fixes at a position |
| `apply_code_action` | Apply a Roslyn code action by title or key |
| `add_null_checks` | Generate ArgumentNullException guards |
| `add_using` | Add a using directive (skips if present, auto-sorts) |
| `implement_missing_members` | Generate interface/abstract stubs |

### Formatting
| Tool | Description |
|------|-------------|
| `organize_usings` | Sort/remove unused (document/project/solution scope) |
| `format_document_batch` | Format documents using Roslyn's Formatter (respects .editorconfig) |

### Editing
| Tool | Description |
|------|-------------|
| `edit_file` | Batch find/replace with atomic apply + diagnostics |
| `write_file` | Full file replacement with atomic apply + diagnostics |

### Infrastructure
| Tool | Description |
|------|-------------|
| `health_check` | Server status |
| `load_solution` | Load .sln/.slnx for analysis |
| `unload_solution` | Unload solution and release file locks |
| `reload_solution` | Reload solution to pick up new files/project changes |
| `list_solutions` | List all loaded solutions and which is active |
| `set_active_solution` | Switch active solution context |
| `sync_documents` | Sync in-memory workspace after external file edits |

## AI Agent Configuration Tips

AI agents tend to default to their built-in tools (Grep, Read, LSP) even when DotLens provides a better option. To get the most out of DotLens, nudge your agent toward using it for analysis tasks.

**Claude Code** — add to your project's `CLAUDE.md`:
```
For C# code, prefer DotLens tools for analysis beyond basic navigation:
- Use `analyze_change_impact` before refactoring to understand blast radius
- Use `find_unused_code` instead of manual searching for dead code
- Use `get_complexity_metrics` to identify problem methods
- Use `get_method_source` instead of Read for viewing specific methods (saves tokens)
- Use `search_symbols` / `find_symbols` instead of Grep for finding C# symbols
```

**Other MCP clients** — configure similar tool priority in your agent's system prompt.

## Document Synchronization

DotLens maintains an in-memory representation of your solution for fast queries. When files are modified externally (via Edit/Write tools), the agent is responsible for synchronizing changes.

### When to call `sync_documents`

| Action | Call sync_documents? |
|--------|---------------------|
| Used Edit tool to modify .cs files | **Yes** |
| Used Write tool to create new .cs files | **Yes** |
| Deleted .cs files | **Yes** |
| Used DotLens refactoring tools (rename, extract, etc.) | No (auto-updated) |
| Used DotLens editing tools (edit_file, write_file) | No (auto-updated + returns diagnostics) |
| Modified .csproj files | No (use `reload_solution` instead) |

```
# After editing specific files
sync_documents(filePaths: ["src/MyClass.cs", "src/MyService.cs"])

# After bulk changes — sync all documents
sync_documents()
```

This mirrors how LSP works — the client (editor) notifies the server of changes. This approach eliminates race conditions, avoids file watcher complexity, and gives agents explicit control over workspace state.

**If you don't sync**, queries may return stale data (old method signatures, missing new files, etc.).

## Architecture

DotLens uses the official [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) with a two-process architecture for file lock isolation:

```
MCP Client (AI Agent)
        │ stdin/stdout (MCP protocol)
        ▼
Server Process
  ├─ MCP Server: handles client requests
  ├─ MCP Client: forwards tool calls to worker
  └─ Server-side tools: load/unload/reload/list/set_active/health
        │
        ▼
Worker Process (--worker mode)
  ├─ MCP Server: receives forwarded calls
  ├─ MSBuildWorkspace: holds file locks
  └─ Roslyn semantic analysis tools
        │
        ▼
Microsoft.CodeAnalysis (Roslyn)
  └─ MSBuildWorkspace, SemanticModel, SymbolFinder
```

This architecture allows the worker process to be killed and restarted to release file locks — important when rebuilding projects with source generators.

## Requirements

- [.NET 10.0 or later](https://dotnet.microsoft.com/download) SDK or Runtime
- An MCP-compatible AI agent (e.g., [Claude Code](https://docs.anthropic.com/en/docs/claude-code), [Cursor](https://cursor.sh/), [VS Code + Copilot](https://code.visualstudio.com/))

## Contributing

Contributions are welcome! Please open an issue or pull request on [GitHub](https://github.com/alphaleonis/dotlens-mcp).

Building from source needs the .NET 11 SDK, which `global.json` pins.

```bash
# Development workflow
dotnet build src/DotLens/DotLens.csproj
dotnet test --project tests/DotLens.Tests/DotLens.Tests.csproj
```

### Adding New Tools

Create or update a tool class in `src/DotLens/Tools/`:

```csharp
[McpServerToolType]
public class YourTools
{
    private readonly IWorkspaceManager _workspace;

    public YourTools(IWorkspaceManager workspace) => _workspace = workspace;

    [McpServerTool(Name = "your_tool", ReadOnly = true, OpenWorld = false)]
    [Description("Description shown to AI.")]
    public async Task<string> YourTool(
        [Description("Param description")] string filePath,
        [Description("Optional param")] int? maxResults = 50,
        CancellationToken cancellationToken = default)
    {
        _workspace.EnsureSolutionLoaded();
        // Your logic using _workspace.Solution, etc.
        return ToolResponse.Success(new { /* results */ });
    }
}
```

No separate tool definition needed — the MCP SDK generates schemas from attributes.

### Key Directories

| Directory | Purpose |
|-----------|---------|
| `src/DotLens/Tools/Navigation/` | Symbol navigation (go-to-definition, find references, etc.) |
| `src/DotLens/Tools/Search/` | Symbol search, type members, file outline |
| `src/DotLens/Tools/Intelligence/` | Compound analysis (type overview, method analysis, etc.) |
| `src/DotLens/Tools/Analysis/` | Diagnostics, data/control flow, complexity, project structure |
| `src/DotLens/Tools/Refactoring/` | Rename, code actions, null checks, implement members |
| `src/DotLens/Tools/Formatting/` | Organize usings, format documents |
| `src/DotLens/Tools/Editing/` | File editing with diagnostics |
| `src/DotLens/Tools/Server/` | Solution management (load/unload/reload) |
| `src/DotLens/Infrastructure/` | Shared utilities (WorkspaceManager, MultiWorkerManager, etc.) |
| `tests/DotLens.Tests/` | xUnit integration tests |

## Acknowledgments

DotLens builds on the work of [dotnet-roslyn-mcp](https://github.com/brendankowitz/dotnet-roslyn-mcp) by Brendan Kowitz and [SharpLensMcp](https://github.com/pzalutski-pixel/sharplens-mcp) by Peter Zalutski.

## License

MIT — See [LICENSE](LICENSE) for details.
