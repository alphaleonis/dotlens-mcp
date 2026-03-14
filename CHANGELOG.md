# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and this project adheres to [Semantic Versioning](https://semver.org/).

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
