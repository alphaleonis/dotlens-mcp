# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## Unreleased

### Changed

- The default `DOTLENS_TimeoutSeconds` is 60 seconds, up from 30, so `find_unused_code` on a large project finishes on a cold workspace.

## v1.1.0

### Added

- C# 15 support. DotLens analyzes code with unions, labeled `break` and `continue`, and extension indexers. A union keeps its struct or class kind and gets a `union` modifier that `search_symbols` and `find_symbols` can filter on. Declarations print the `union` keyword, `get_file_outline` lists unions with their case types, and `get_type_overview` reports `IsUnion` and `UnionCaseTypes`.
- `search_symbols` (`kind`) and `find_symbols` (`kinds`) can filter to C# 14 extension blocks with the new `Extension` value.
- The `memberKind` filter of `get_type_members` and `get_type_members_batch` can select constructors, destructors, operators, conversions, accessors and nested types, so every kind the listing reports can be filtered.
- `find_unused_code` takes an `entryPointAttributes` parameter that names more attributes whose members count as used.
- `find_references` lists references in source-generated files, which it used to count without listing. References, caller locations and impact locations carry `IsGenerated`.
- `load_solution`, `reload_solution` and `health_check` warn when a project resolves no framework references, usually because it was never restored, and `get_diagnostics` reports the warning for that project.
- `organize_usings` and `add_using` follow `dotnet_sort_system_directives_first` and `dotnet_separate_import_directive_groups` from `.editorconfig`.

### Changed

- DotLens also starts on a machine that has .NET 11 but not .NET 10, and analyzes code with Roslyn 5.12.
- `organize_usings` writes what Roslyn's Organize Imports writes: the same order (global usings first, then namespace, static and alias usings, with System first and an order that does not depend on the current culture), the same group blank lines and the same line breaks, including the `end_of_line` option where no using ends its line. A using list that a preprocessor directive splits keeps its order, as Roslyn keeps it. It also sorts extern aliases and the using lists inside namespaces, and leaves code outside the using lists untouched instead of reformatting the file. Where a using shares a line with the next using or with code, it gets a line break, without the whitespace before it, only when what follows it changes.
- `add_using` treats a namespace as present only when a using in scope imports it, including global and implicit usings. It refuses type names and malformed names, adds to the namespace's using list when the file keeps its usings there, joins the usings inside an `#if` that encloses all the code, and writes a `global::` or extern-alias-qualified name when a plain name would bind to something else.
- `get_complexity_metrics` computes cognitive complexity as the SonarSource Cognitive Complexity white paper (v1.7) defines it. That covers `goto` and labeled jumps, `do`-`while`, `else if` chains, sequences of logical operators, nesting inside lambdas, local functions, ternaries and switch expressions, and recursion, including methods that call each other. Scores can differ from earlier versions and from SonarQube's C# analyzer; the tool description says where SonarQube departs from the white paper.
- `find_unused_code` counts only references from code that can run. Private and internal members that only reference each other are now reported. Members that test, web, serializer, MEF, Blazor and benchmark frameworks call by reflection count as used, and so does a static class whose members, extension members included, are used outside it. Extension blocks are no longer reported as unnamed unused classes; their unused members are.
- `get_type_members` and `get_type_members_batch` list every overload, apply the C# hiding rules to inherited members, leave out inherited members the type cannot access, and report one kind per member, with nested types by their type kind. `get_type_members_batch` resolves names as `get_type_members` does, so nested names such as `Outer.Inner` work, and an ambiguous name fails only its own entry.
- Declarations from `get_symbol_info`, `search_symbols`, `get_file_overview`, `analyze_method` and `get_method_info` keep ref returns, nullable annotations, constraints, variance, explicit interface qualifiers, the `this` of extension methods, default values in full and base lists as written.
- Every tool names an extension block by its header, such as `extension(Grid)`, and reports it with an `extension` kind instead of as an unnamed class. `rename_symbol` refuses a block.
- Name lookup selects `Task` apart from `Task<T>` when the name gives no type parameters, accepts a metadata arity suffix such as ``Task`1``, finds extension block members by their block's header and accessors by their `get_`/`set_`/`add_`/`remove_` names. An ambiguity error lists every candidate in a form that name lookup accepts.
- `search_symbols` lists only symbols that source code declares, so a wildcard search no longer returns the global namespace, compiler-generated members or the implementation methods behind extension members.
- `find_references`, `find_callers`, `analyze_method`, `analyze_change_impact` and `find_unused_code` list each use once in a multi-targeted project or a linked file, instead of once per target framework.
- `find_callers` reports a use in a property or event initializer as a use by that property or event, and a use in top-level statements with no calling member. `get_containing_member` reports no member inside top-level statements.
- The tools that add or remove files refuse, before anything is written, a change that MSBuild would get wrong: deleting a file another project still compiles, removing a file an SDK glob includes, or listing a file a glob already adds. When a write fails partway, DotLens restores the files it changed and reports which. Writes honor cancellation.
- A failed `load_solution` or `reload_solution` keeps the previous solution loaded and reports why the load failed.

### Fixed

- `find_references` classified writes through indexers, deconstruction targets, null-conditional assignments, writable refs and attribute named arguments as reads. A `ref` passed to an `in` or `ref readonly` parameter, and a nested member initializer such as `Items = { 1 }`, now count as reads.
- `find_references`, `find_callers`, `analyze_change_impact`, `analyze_method` and `find_unused_code` missed indexers used in null-conditional accesses such as `x?[i] = v`.
- A position lookup on an indexer's `[` or another argument-list token resolved to the enclosing declaration instead of the indexer or call. A position lookup on a positional record parameter now finds the property that `search_symbols` reports there.
- `get_file_overview` left out enums and delegates.
- `get_file_outline` left out extension block headers, unions and primary-constructor parameters, broke on signatures written across lines, showed private fields with `includePrivate` false, cut expression-bodied indexers and the first `///` of doc comments, and dropped explicit interface qualifiers, `checked` operators, `record class` and fixed buffer sizes.
- `sync_documents` left the other copies of a multi-targeted or linked file stale, and later edits re-applied its changes, which could re-save project files or overwrite a synced file with old text.
- `organize_usings` and `add_using` wrote CRLF line breaks into LF files, dropped the file header and `#nullable` and `#pragma` lines above the usings, could drop or unbalance `#if` and `#region` blocks among them, and added blank lines after U+2028, U+2029 and U+0085 line breaks.
- `get_type_members` suggested the nonexistent tools `get_method_signature` and `get_base_types`.
- `load_solution` failed when the server ran as `dotnet DotLens.dll`. A worker's error code, message and hint now reach the client as the worker reported them.
- `--help` listed `Trace` as a log level, which made the server fail at startup; it now lists `Verbose` through `Fatal`. "No solution loaded" messages named a `DOTNET_SOLUTION_PATH` variable and a `roslyn:load_solution` tool, which do not exist.
- `THIRD-PARTY-NOTICES.txt` lists every third-party component the package redistributes, including transitive dependencies and the assemblies bundled inside Roslyn's BuildHost, and includes the full MIT and Apache-2.0 license texts. The notices in 1.0.0 and 1.0.1 listed only direct dependencies, some at outdated versions.

## v1.0.1

Initial public release of DotLens — an MCP server providing 54 AI-optimized tools for .NET/C# semantic code analysis, built on Roslyn.

### Highlights

- **Two-process architecture**: Server manages worker processes (one per loaded solution), isolating MSBuildWorkspace file locks for stability and multi-solution support
- **Automatic solution discovery**: Auto-detects and loads `.sln`/`.slnx` files from the working directory on startup
- **54 public-facing tools** across 8 categories, designed to complement your agent's built-in LSP support with deeper semantic analysis

### Tool Categories

- **Navigation** (8 tools): `find_references`, `find_callers`, `find_implementations`, `find_overrides`, `find_base_member`, `go_to_definition`, `get_symbol_info`, `get_type_hierarchy`
- **Search** (11 tools): `search_symbols`, `find_symbols`, `find_string_literals`, `get_type_members`, `get_derived_types`, `get_file_outline`, `get_method_info`, `get_containing_member`, `get_attributes`
- **Intelligence** (10 tools): `analyze_method`, `analyze_change_impact`, `get_type_overview`, `get_file_overview`, `get_method_source`, `get_outgoing_calls`, `get_missing_members`, `check_type_compatibility`, `get_instantiation_options`, `get_type_members_batch`
- **Analysis** (9 tools): `get_diagnostics`, `get_complexity_metrics`, `analyze_control_flow`, `analyze_data_flow`, `dependency_graph`, `find_unused_code`, `get_project_structure`, `get_project_packages`, `fix_all`
- **Refactoring** (6 tools): `rename_symbol`, `add_null_checks`, `add_using`, `implement_missing_members`, `get_code_actions`, `apply_code_action`
- **Editing** (2 tools): `edit_file`, `write_file`
- **Formatting** (2 tools): `format_document_batch`, `organize_usings`
- **Server Management** (6 tools): `load_solution`, `unload_solution`, `reload_solution`, `list_solutions`, `set_active_solution`, `health_check`

### Configuration

All options configurable via `DOTLENS_` environment variables or command-line arguments, including solution path, timeouts, diagnostics limits, caching, and log level.
