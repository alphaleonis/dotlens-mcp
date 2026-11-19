namespace DotLens.Tests.Integration;

/// <summary>
/// A workspace loaded with an executable whose entry point is top-level statements, beside a user method named Main
/// and a user part of the generated Program class.
/// </summary>
public sealed class TopLevelStatementsFixtureWorkspace() : StandaloneFixtureWorkspace("TopLevelStatementsTestProject");
