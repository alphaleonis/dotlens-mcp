namespace CoreTestProject.Refactoring;

// NOTE: This file is used for apply-mode tests that modify the file.
// It will be restored after each test run via test cleanup.

public interface IApplyModeService
{
   void DoWork();
}

/// <summary>
/// Class used for testing apply mode. Will be modified during tests.
/// </summary>
public class ApplyModeClass : IApplyModeService
{
   // Missing: DoWork()
}
