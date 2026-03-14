namespace DotLens.Infrastructure;

/// <summary>
/// Error codes for consistent error handling across all tools.
/// </summary>
public static class ErrorCodes
{

   public const string FileNotFound = "FILE_NOT_FOUND";
   public const string FileNotInSolution = "FILE_NOT_IN_SOLUTION";
   public const string FileNotInAnyProject = "FILE_NOT_IN_ANY_PROJECT";
   public const string ProjectNotFound = "PROJECT_NOT_FOUND";
   public const string SymbolNotFound = "SYMBOL_NOT_FOUND";
   public const string SymbolInMetadata = "SYMBOL_IN_METADATA";
   public const string TypeNotFound = "TYPE_NOT_FOUND";
   public const string AmbiguousType = "AMBIGUOUS_TYPE";
   public const string AmbiguousSymbol = "AMBIGUOUS_SYMBOL";
   public const string NotAType = "NOT_A_TYPE";
   public const string NotAMethod = "NOT_A_METHOD";
   public const string AnalysisFailed = "ANALYSIS_FAILED";
   public const string InternalError = "INTERNAL_ERROR";
   public const string NotSupported = "NOT_SUPPORTED";
   public const string InvalidParameter = "INVALID_PARAMETER";
   public const string Timeout = "TIMEOUT";
   public const string SecurityError = "SECURITY_ERROR";

   public const string WorkerNotRunning = "WORKER_NOT_RUNNING";
   public const string WorkerCommunicationFailed = "WORKER_COMMUNICATION_FAILED";
   public const string NoSolutionLoaded = "NO_SOLUTION_LOADED";
   public const string SolutionLoading = "SOLUTION_LOADING";
   public const string InvalidDocument = "INVALID_DOCUMENT";
   public const string ApplyChangesFailed = "APPLY_CHANGES_FAILED";
}
