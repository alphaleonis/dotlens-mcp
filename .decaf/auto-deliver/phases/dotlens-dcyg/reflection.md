# dotlens-dcyg — record ToString reads in find_unused_code (2026-11-20)

## Acceptance results

The nib's "done when" needs a decision that only the operator can make. The run did the analysis and appended it to the nib (`## Analysis`, `## Recommendation`), changed no code, and left the nib `in-progress`, which keeps it out of `next-ready` until the decision is made.

## Open questions (filed: dotlens-ln2o)

### Should a record property that only the record's ToString reads count as used?
- **Kind:** decision
- **From:** dotlens-dcyg; `UsageAnalysis.GetReferrersAsync`, `GetGeneratedDeconstructReadersAsync`
- **What happened:** `find_unused_code` reports a property of a non-public record as unused when only the generated `ToString` (through `PrintMembers`) reads it, for example a property that only shows up in logs or `$"{record}"`. Public properties of public records are roots already, so only records not visible outside their assembly are affected. Generated `ToString` is an override and counts as running whenever the record type is used, so reusing the dotlens-oq3f Deconstruct mechanism would make every public property of every used non-public record count as used.
- **Options:**
  - A. Keep the current behavior — no code change; properties that only appear in formatted output keep being reported as unused, which is the error the tool's bias treats as worse.
  - B. Count the PrintMembers reads only when code that runs can format the record: calls ToString/PrintMembers on it, interpolates or concatenates it, converts it to another type (object, interface, base record, dynamic, type parameter), or passes it as a type argument to a generic method — follow-up nib, medium size (one operation scan in UsageAnalysis plus fixtures). Records that only travel as their own type keep useful reports; a container converted covariantly is the main gap, closed by also treating conversions of containers whose type arguments mention the record as formatting.
  - C. Count them always — small change, but unused properties of used non-public records are never reported again.
- **Recommendation:** B, leaning wide, as in the nib's `## Recommendation`. Not checked: how often real solutions convert records to object (logging) — if most do, B behaves close to C at higher cost. On a decision of B or C, file the follow-up nib and close dotlens-dcyg; on A, close it as decided with no change.
