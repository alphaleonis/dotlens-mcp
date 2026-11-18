# Type members group — dotlens-iqdg, dotlens-p0yq, dotlens-ynf5, dotlens-mh8u (2026-11-19)

## Acceptance results

- [run] `*TypeMembers_IncludeInherited_Operators*`, `*TypeMembers_IncludeInherited_Accessibility*`, `*TypeMembers_NestedTypeKinds*`, `*FullDeclaration_RecordEquatable*`: 16/16, exit 0.
- [run] full suite (`--max-threads 8`): 1625/1625, exit 0.

Commit 5a6d1e5. Reviews: bugs (APPROVED, 1M), bugs re-review (APPROVED, 2M+4L, regressions from the folded kind mapping), bugs roster=3 (APPROVED, 3M from the syntax fallback + 1 Minor); final round removed the fallback; fix check ADDRESSED with one accepted Low (retargeted records may show a compiler-added IEquatable<Self>).

## Deviations

- iqdg resolved by decision, not code (see open questions).
- Folded in member-kind mapping and batch name resolution.

## Follow-ups

None.

## Open questions

### Base operators stay in inherited member lists
- **Kind:** confirm
- **From:** dotlens-iqdg; `src/DotLens/Infrastructure/RoslynHelpers.cs`
- **What happened:** The nib asked to leave base operators and conversion operators out of includeInherited lists "per the C# spec". The spec (§15.10.1, and §15.3.4, which excludes only constructors, finalizers and static constructors from inheritance) says operators are inherited, so they stay listed; a test pins the list. The conductor's brief had said the opposite; the implementer and reviewer both checked the spec.
- **Options:**
  - A. Keep listing them.
  - B. Leave them out anyway (a one-line filter and a test change).
- **Recommendation:** A. It follows the language spec.

### Member kinds are reported more precisely
- **Kind:** confirm
- **From:** dotlens-iqdg group; `RoslynHelpers.GetMemberKindString`
- **What happened:** get_type_members and the batch tool now report Accessor, Operator, Conversion, Destructor and Constructor instead of Method for those members, and the memberKind filter and get_type_overview's method count follow the same mapping. Clients that filtered on "Method" to find accessors or operators see fewer results.
- **Options:**
  - A. Keep the precise kinds.
  - B. Report the old kinds for compatibility.
- **Recommendation:** A. Counts and filters now agree across tools.

### Retargeted partial records may show a compiler-added IEquatable<Self>
- **Kind:** decision
- **From:** dotlens-mh8u; `RoslynHelpers.WritesOwnEquatable`
- **What happened:** When a record is reached through a project built against another core library, its own compilation is not available to bind the written base types, so the declaration shows IEquatable<Self> whenever the symbol has it. Three rounds of syntax heuristics each introduced new errors and were removed.
- **Options:**
  - A. Accept the occasional extra interface.
  - B. File a follow-up to map the symbol to its source definition (SymbolFinder.FindSourceDefinitionAsync with the solution) in the tools that build declarations, then bind there.
- **Recommendation:** B if exact declarations for multi-targeted records matter; it touches about 20 call sites.
