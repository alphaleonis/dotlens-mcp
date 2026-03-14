namespace CoreTestProject.Types;

/// <summary>
/// Test fixture class for file pattern filtering tests.
/// Used by FormatDocumentBatchToolTests to verify "*Service*.cs" pattern matching.
/// </summary>
public class TestService
{
   public string Name { get; set; } = string.Empty;

   public void Execute()
   {
      // Simple service method for testing
   }
}
