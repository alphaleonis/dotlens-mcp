namespace DotLens.Tests.Integration;

/// <summary>
/// A workspace loaded with a fixture project whose types derive from types of two other fixture projects, one of which
/// gives it access to its internals.
/// </summary>
public sealed class MemberListsFixtureWorkspace() : StandaloneFixtureWorkspace("MemberListsTestProject");
