namespace DotLens.Tests.Integration;

/// <summary>
/// A workspace loaded with a two-target fixture project whose uses cover linked copies, source-generated code, writable
/// references and attribute named arguments.
/// </summary>
public sealed class ReferenceSearchFixtureWorkspace() : StandaloneFixtureWorkspace("ReferenceSearchTestProject");
