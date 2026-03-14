namespace ConflictingTypesProject;

/// <summary>
/// A unique type only in this project (no conflict with other projects).
/// Used to test that non-ambiguous type lookups succeed.
/// </summary>
public class UniqueConflictingType
{
    public string Value { get; set; } = "";
}
