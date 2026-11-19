namespace DotLens.Infrastructure;

/// <summary>
/// Server instruction constants for AI clients.
/// </summary>
public static class ServerInstructions
{
   /// <summary>
   /// Brief instructions (~150 tokens) with core tool selection guidance.
   /// </summary>
   public const string Brief = """
      DotLens provides Roslyn-based semantic analysis for C# codebases.

      PREFER DOTLENS FOR C# (over Grep/Read):
      - Symbol search: search_symbols (handles overloads, partials, generics)
      - Find usages: find_references, find_callers (semantic, not text)
      - Read methods: get_method_source (targeted, efficient)
      - Type info: get_type_members, get_type_overview
      - Compiler errors: get_diagnostics

      CRITICAL: After Edit/Write on .cs files, call sync_documents to update the workspace.

      AUTO-LOAD: If one .sln/.slnx exists in the working directory, it loads automatically.
      WORKFLOW: analysis tools -> sync_documents after edits (load_solution only if auto-load didn't apply)
      """;

   /// <summary>
   /// Detailed instructions (~400 tokens) with comprehensive guidance.
   /// </summary>
   public const string Detailed = """
      DotLens provides Roslyn-based tools for semantic C# code analysis, navigation, and refactoring.

      ## When to Prefer DotLens

      For C# code, DotLens tools are more accurate than text-based alternatives:

      | Task | Use DotLens | Instead of |
      |------|-------------|------------|
      | Find symbols | search_symbols | Grep (misses overloads, partials) |
      | Find usages | find_references, find_callers | Grep (false positives from strings/comments) |
      | Read methods | get_method_source | Read entire file |
      | Type structure | get_type_members, get_type_overview | Manual file reading |
      | Compilation check | get_diagnostics | Running build |
      | Rename | rename_symbol | Find/replace (misses scoped references) |

      ## Critical Workflow

      1. **Auto-load**: If one .sln/.slnx exists in the working directory, it loads automatically on startup. Call load_solution only if no solution was auto-loaded or to switch solutions.
      2. **Sync after edits**: After Edit/Write on .cs files, call sync_documents to update the workspace
      3. **Verify**: Use get_diagnostics after modifications to catch compiler errors

      ## Efficient Patterns

      - **Explore type**: get_type_overview -> get_type_members -> get_method_source for specifics
      - **Understand method**: analyze_method (signature + callers + outgoing in one call)
      - **Batch operations**: get_type_members_batch for multiple items
      - **Before refactoring**: analyze_change_impact to preview breaking changes

      ## Avoid

      - Grep for C# symbols (misses overloads, generics, partial classes)
      - Forgetting sync_documents after edits (causes stale results)
      - Reading entire files when get_method_source suffices
      - Text find/replace instead of rename_symbol
      """;

   /// <summary>
   /// Gets the instruction text for the specified level.
   /// </summary>
   public static string? GetInstructions(InstructionLevel level) => level switch
   {
      InstructionLevel.Brief => Brief,
      InstructionLevel.Detailed => Detailed,
      _ => null
   };
}
