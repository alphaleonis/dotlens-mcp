# dotlens-r1yu — Report C# 15 unions as unions (2026-11-19)

Single-feature plan; the feature was the phase, so BREAKDOWN was skipped.

## Acceptance results

- [run] `--filter-method "*.CSharp15FixtureTests.SymbolInfo_UnionDeclaration"`: 3/3, exit 0.
- [run] `--filter-method "*.CSharp15FixtureTests.SymbolInfo_CustomUnionKeepsDeclaredKeyword"`: 2/2, exit 0.
- [run] `--filter-method "*.CSharp15FixtureTests.TypeOverview_Union*"`: 12/12, exit 0.
- [run] `--filter-method "*.CSharp15FixtureTests.SearchSymbols_UnionModifierFilter"`: 1/1, exit 0.
- [run] `--filter-method "*.RoslynHelpersTests.Declaration_MetadataUnionKeepsStructKeyword"`: 2/2, exit 0.
- [run] full suite (`--max-threads 8`): 1906/1906, exit 0.

Review: bugs (solo seat), APPROVED with no findings; no fix round. Commit 13f5957.

## How it went

The implementer probed 21 declaration shapes through Roslyn 5.12 before coding (partial, nested, readonly, base list, attributes, custom [Union] struct/class/record, metadata). That settled the open edges up front: a partial type cannot mix union and struct parts (CS0261), so "any part is a UnionDeclarationSyntax" decides the keyword; `readonly union` is the only extra modifier allowed and Roslyn reports it as readonly; the case list prints through the existing primary-constructor path.

## Deviations

None from the spec. Beyond it: a `FindSymbols_UnionModifierFilter` test and a `Declaration_SourceUnionPrintsUnionKeyword` test covering partial, nested, base-list, readonly and attributed unions.

## Follow-ups

None filed. Awareness (pre-existing, not changed): every struct and enum reports `sealed`, unions included; a `[Union]` type that does not implement `IUnion` is still a union to Roslyn, so DotLens reports it as one.

## Open questions

None.
