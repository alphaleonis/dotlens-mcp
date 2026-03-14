using DotLens.Infrastructure;
using Xunit;

namespace DotLens.Tests.Infrastructure;

public class ErrorCodesTests
{
   [Fact]
   public void ErrorCodes_HasExpectedConstants()
   {
      // Verify commonly used error codes exist and have expected values
      Assert.Equal("SYMBOL_NOT_FOUND", ErrorCodes.SymbolNotFound);
      Assert.Equal("TYPE_NOT_FOUND", ErrorCodes.TypeNotFound);
      Assert.Equal("ANALYSIS_FAILED", ErrorCodes.AnalysisFailed);
      Assert.Equal("NO_SOLUTION_LOADED", ErrorCodes.NoSolutionLoaded);
      Assert.Equal("INVALID_PARAMETER", ErrorCodes.InvalidParameter);
      Assert.Equal("FILE_NOT_FOUND", ErrorCodes.FileNotFound);
      Assert.Equal("TIMEOUT", ErrorCodes.Timeout);
      Assert.Equal("WORKER_NOT_RUNNING", ErrorCodes.WorkerNotRunning);
      Assert.Equal("WORKER_COMMUNICATION_FAILED", ErrorCodes.WorkerCommunicationFailed);
   }
}
