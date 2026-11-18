using Microsoft.CodeAnalysis;

namespace DotLens.Infrastructure;

/// <summary>
/// A problem that the load of a solution found, which can leave the results of tools incomplete.
/// </summary>
/// <param name="Message">The text that tells the user the problem and what to do about it.</param>
/// <param name="ProjectId">
/// The project whose results are incomplete, or null when the problem concerns the whole load.
/// </param>
public sealed record LoadWarning(string Message, ProjectId? ProjectId);
