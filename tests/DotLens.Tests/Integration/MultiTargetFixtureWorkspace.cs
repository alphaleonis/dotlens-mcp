namespace DotLens.Tests.Integration;

/// <summary>
/// A workspace loaded with a fixture project that targets two frameworks, so the workspace holds one project for each.
/// </summary>
public sealed class MultiTargetFixtureWorkspace() : StandaloneFixtureWorkspace("MultiTargetTestProject");
