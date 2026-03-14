namespace CoreTestProject.Refactoring;

// NOTE: This file intentionally has compilation errors (CS0535) for testing purposes.
// It is used by ImplementMissingMembersTool tests.

/// <summary>
/// Interface for testing implement_missing_members tool.
/// </summary>
public interface ITestService
{
   string Name { get; }
   void Execute();
   Task ExecuteAsync();
}

/// <summary>
/// Class with missing interface members for testing implement_missing_members tool.
/// This class intentionally doesn't suppress CS0535.
/// </summary>
public class ImplementableClass : ITestService
{
   public string Name => "Test";

   // Missing: Execute() and ExecuteAsync()
}
