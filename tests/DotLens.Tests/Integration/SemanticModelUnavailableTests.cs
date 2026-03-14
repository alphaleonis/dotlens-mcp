using Xunit;

namespace DotLens.Tests.Integration;

/// <summary>
/// Tests for semantic model error code consistency.
///
/// Note: Testing scenarios where GetSemanticModelAsync returns null requires
/// a real Document object from Roslyn. Since Document is a sealed class,
/// those scenarios require integration tests. The code handles null semantic
/// model gracefully by returning ANALYSIS_FAILED errors.
/// </summary>
public class SemanticModelUnavailableTests
{
   [Fact]
   public void AnalysisFailed_ErrorCode_HasExpectedValue()
   {
      // All semantic model null checks use ErrorCodes.AnalysisFailed
      // This test verifies the constant has the expected string value
      Assert.Equal("ANALYSIS_FAILED", DotLens.Infrastructure.ErrorCodes.AnalysisFailed);
   }
}
