// ===========================================
// This file intentionally defines types with the same
// fully-qualified names as types in CoreTestProject.
// Used to test ambiguity detection in type lookup.
// ===========================================

namespace CoreTestProject.Types;

/// <summary>
/// Intentionally conflicts with CoreTestProject.Types.AmbiguousTestClass.
/// Used ONLY for testing ambiguity detection - has a unique name to avoid
/// breaking other tests that use SampleClass.
/// </summary>
public class AmbiguousTestClass
{
    // Different implementation than the "real" AmbiguousTestClass
    public string ConflictingProjectMarker => "From ConflictingTypesProject";

    public int ConflictingId { get; set; }

    public void ConflictingMethod() { }
}
