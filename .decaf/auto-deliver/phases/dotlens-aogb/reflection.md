# Deconstruct reference search — dotlens-aogb, dotlens-hmk5 (2026-11-20)

## Acceptance results

Both nibs state prose criteria; each was settled by execution:
- aogb: `ImplicitCallTests` asserts exact callers and locations for 15 shapes (positional, var, nested patterns; switch arms; declaration, typed, existing-variable and nested deconstruction assignments; foreach, typed and await foreach) × 4 Deconstruct kinds (hand-written, record, classic extension, extension block). `DeconstructionReferenceTests` asserts `find_callers` → `Run` at 98:32 for the nib's repro.
- hmk5: the same classes assert kind `call` for every Deconstruct reference shape.
- [run] full suite (`--max-threads 8`): 2033/2033, exit 0.

Commit a7a26bd. BREAKDOWN skipped: leaf bugs with acceptance. Reviews: bugs (APPROVED, no findings, 4 minor notes); scoped bugs re-review of the folded kind rule (APPROVED, 1 Medium + 1 Low, both fixed); fix-verifier: both ADDRESSED, one Low inferred breakage (implicit reference anchored on a string literal), which the shape table shows does not occur.

## Deviations

- Scope folded in from hmk5's "check them while fixing this": kind `call` for implicit GetEnumerator/MoveNext, GetAwaiter, Dispose, collection-initializer Add, CollectionBuilder Create, target-typed new(), and for in-code references to operators, conversions and constructors (`: base()`, `: this()`, primary-constructor bases, attributes) and `obj?.M()`.
- find_references now files a positional pattern under whichever Deconstruct in the cascade it binds to, so a search also finds patterns that call an override or implementation, as Roslyn does for assignments.

## Follow-ups

- dotlens-l4ju — generic extension block implementation method misses deconstruction assignments (pre-existing).
- dotlens-56s0 — compiler-inserted calls that Roslyn does not report at all (awaiter GetResult, await foreach members, query syntax, implicit conversions, operator true/false, delegate invocation).
Both filed at the project root, outside the v1.1.1 milestone.

## Open questions

None.
