namespace DotLens.Tests.Integration;

/// <summary>
/// A workspace loaded with an executable that uses test, web and serializer frameworks, a library whose internals are
/// visible to an assembly outside the solution, and a library whose internals are visible to the executable.
/// </summary>
public sealed class UnusedCodeFixtureWorkspace() : StandaloneFixtureWorkspace("UnusedCodeTestProject");
