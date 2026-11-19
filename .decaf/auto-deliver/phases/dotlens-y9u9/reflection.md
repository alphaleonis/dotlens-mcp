# dotlens-y9u9 — Extension blocks with empty names in other tools (2026-11-16)

## Acceptance results

`--filter-method "*ExtensionBlockName*"`: 22/22. Full suite: 1140/1140 with `--max-threads 8`.

## Deviations

- Scope widened within the same root cause: get_type_members' dedup (which also collapsed overloads) replaced by C# hiding rules shared with get_type_members_batch. Three review rounds (cap reached; final fix check addressed all six findings).

## Follow-ups

dotlens-iqdg, dotlens-p0yq, dotlens-ynf5 (bugs, in this run's scope), dotlens-ljme (feature, out of scope). Note on dotlens-nvro (block declaration string).

## Open questions (filed: dotlens-0vlu)
### Inherited member lists now include static members in both member tools
- **Kind:** confirm
- **From:** dotlens-y9u9; `RoslynHelpers.GetTypeMembers`, `get_type_members`, `get_type_members_batch`
- **What happened:** The two tools disagreed: `get_type_members` with `includeInherited` listed base static members, the batch tool did not (it had tests asserting that). Both now share one helper, and the run chose to include statics — C# inherits them, and it restores `get_type_members`' earlier behavior. The batch tool therefore now lists base constants, static methods and static nested types it used to omit; its test was changed from "excludes" to "includes". Private members, constructors and destructors stay out, and a private member hides nothing in an inherited list.
- **Options:**
  - A. Keep statics in both tools.
  - B. Exclude statics in both — the batch tool's old rule; `get_type_members` loses base statics it used to show.
- **Recommendation:** A. It matches the language and the single-type tool's history. Not checked: whether any client relies on the batch tool omitting statics.
