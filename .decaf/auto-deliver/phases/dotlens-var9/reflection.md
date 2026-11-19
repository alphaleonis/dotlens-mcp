# Type members group — dotlens-var9, dotlens-qx4f (2026-11-19)

## Acceptance results

- [run] `*ToolNameMentions_NameRegisteredTools*`: 1/1, exit 0 (implementer and reviewer saw it fail on exactly get_method_signature and get_base_types before the fix).
- [run] `rg -n "get_method_signature|get_base_types" src`: no output.
- [run] `*TypeMembers_MemberKinds*`: 3/3; `*TypeMembers_NestedTypeKinds*`: 2/2, exit 0.
- [run] full suite (`--max-threads 8`): 1657/1657, exit 0.

Commit 0c45e9e. Review: bugs (APPROVED, 1 Low: test summary overstated its scope; reworded by the conductor).

## Deviations

- The new enum values carry no [JsonStringEnumMemberName]: the existing values have none and the global camelCase converter names them (constructor, …, nestedType).

## Follow-ups

None filed. The implementer noticed find_references on an enum reports "modifiers":"sealed" and suggests find_callers; raised with the operator.
