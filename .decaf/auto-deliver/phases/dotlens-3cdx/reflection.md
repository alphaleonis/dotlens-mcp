# dotlens-3cdx — Position lookup on an indexer's '[' (2026-11-16)

## Acceptance results

- `--filter-method "*LookupByPosition_Indexer*"`: 14/14 (implementer), 25/25 `*LookupByPosition_*` (reviewer).
- Full suite: 1042/1042 with `--max-threads 8`, load 2.3 at start.

## Deviations

- Scope widened within the same root cause: call parentheses and commas, constructor `(`, primary-constructor base `(` and attribute argument lists also resolve to their owner (the base-type `(` used to return the derived class).
- The ancestor-walk guard changes older behavior beyond indexers (see the open question).

## Open questions (filed: dotlens-qw76)

### A symbol-less position inside an expression now finds nothing
- **Kind:** confirm
- **From:** dotlens-3cdx; `src/DotLens/Infrastructure/SymbolLookup/SymbolLookupService.cs`
- **What happened:** Before, a position lookup on any token without its own symbol walked up to the nearest declaration and returned it: a literal in a field initializer returned the field, a literal in `var x = Foo(1)` returned `x`. The fix stops the walk at the first value expression, so those positions now report "symbol not found". That ends the silent wrong answers this bug was about, but a client that clicked a literal in an initializer and expected the field now gets an error. Statement keywords directly in a method body still return the method; `get_outgoing_calls` uses a separate enclosing-symbol lookup and is unaffected.
- **Options:**
  - A. Keep it — an expression position resolves to its own symbol or to nothing.
  - B. Keep "enclosing declaration" for initializers and expression bodies only — friendlier for a click on a field's initializer, but it brings back an answer that is not the symbol at the position.
- **Recommendation:** A. "The symbol at this position" should not be a different symbol; callers that want the enclosing member already have a lookup for that. Not checked: how MCP clients use position lookups on literals.
