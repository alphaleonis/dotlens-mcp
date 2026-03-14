using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Analysis;
using DotLens.Tools.Intelligence;
using DotLens.Tools.Navigation;
using DotLens.Tools.Refactoring;
using DotLens.Tools.Search;
using Xunit;

namespace DotLens.Tests.CrossCutting;

/// <summary>
/// Tests tool behavior when a file path is provided but the document is not in the solution.
/// All tools throw ToolException with appropriate error codes.
/// </summary>
public class DocumentNotFoundTests : MockableRoslynServiceTestBase
{
   // Use a relative path within solution boundary to test "file not in solution" (not security error)
   private const string NonExistentFile = "NonExistent/File.cs";

   // Navigation tools
   private GetSymbolInfoTool _getSymbolInfo = null!;
   private GoToDefinitionTool _goToDefinition = null!;
   private FindReferencesTool _findReferences = null!;
   private FindImplementationsTool _findImplementations = null!;
   private GetTypeHierarchyTool _getTypeHierarchy = null!;
   private FindCallersTool _findCallers = null!;

   // Search tools
   private GetFileOutlineTool _getFileOutline = null!;

   // Analysis tools
   private GetComplexityMetricsTool _getComplexityMetrics = null!;
   private AnalyzeDataFlowTool _analyzeDataFlow = null!;
   private AnalyzeControlFlowTool _analyzeControlFlow = null!;

   // Refactoring tools
   private RenameSymbolTool _renameSymbol = null!;
   private GetCodeActionsTool _getCodeActions = null!;

   // Intelligence tools
   private GetFileOverviewTool _getFileOverview = null!;
   private GetOutgoingCallsTool _getOutgoingCalls = null!;

   // Services
   private SymbolLookupService _symbolLookup = null!;

   public DocumentNotFoundTests()
   {
      SetupSolutionLoaded();
      SetupDocumentNotFound(NonExistentFile);

      // Initialize services
      _symbolLookup = new SymbolLookupService(FakeWorkspaceManager);

      // Initialize navigation tools
      _getSymbolInfo = new GetSymbolInfoTool(FakeWorkspaceManager, _symbolLookup);
      _goToDefinition = new GoToDefinitionTool(FakeWorkspaceManager, _symbolLookup);
      _findReferences = new FindReferencesTool(FakeWorkspaceManager, _symbolLookup);
      _findImplementations = new FindImplementationsTool(FakeWorkspaceManager, _symbolLookup);
      _getTypeHierarchy = new GetTypeHierarchyTool(FakeWorkspaceManager, _symbolLookup);
      _findCallers = new FindCallersTool(FakeWorkspaceManager, _symbolLookup);

      _getFileOutline = new GetFileOutlineTool(FakeWorkspaceManager);
      _getComplexityMetrics = new GetComplexityMetricsTool(FakeWorkspaceManager, _symbolLookup);
      _analyzeDataFlow = new AnalyzeDataFlowTool(FakeWorkspaceManager, _symbolLookup);
      _analyzeControlFlow = new AnalyzeControlFlowTool(FakeWorkspaceManager, _symbolLookup);
      _renameSymbol = new RenameSymbolTool(FakeWorkspaceManager, _symbolLookup);
      _getCodeActions = new GetCodeActionsTool(FakeWorkspaceManager);
      _getFileOverview = new GetFileOverviewTool(FakeWorkspaceManager);
      _getOutgoingCalls = new GetOutgoingCallsTool(FakeWorkspaceManager, _symbolLookup);
   }

   private async Task AssertThrowsToolExceptionAsync(Func<Task> action, string expectedCode)
   {
      var ex = await Assert.ThrowsAsync<ToolException>(action);
      Assert.Contains(expectedCode, ex.Code, StringComparison.OrdinalIgnoreCase);
   }

   #region NavigationTools - Throw ToolException

   [Fact]
   public async Task GetSymbolInfo_DocumentNotFound_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _getSymbolInfo.GetSymbolInfoAsync(
            new LookupByPosition { FilePath = NonExistentFile, Line = 1, Column = 1 },
            cancellationToken: CT),
         "FILE_NOT_IN_SOLUTION");
   }

   [Fact]
   public async Task GoToDefinition_DocumentNotFound_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _goToDefinition.GoToDefinitionAsync(
            new LookupByPosition { FilePath = NonExistentFile, Line = 1, Column = 1 },
            cancellationToken: CT),
         "FILE_NOT_IN_SOLUTION");
   }

   [Fact]
   public async Task FindReferences_DocumentNotFound_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _findReferences.FindReferencesAsync(
            new LookupByPosition { FilePath = NonExistentFile, Line = 1, Column = 1 },
            cancellationToken: CT),
         "FILE_NOT_IN_SOLUTION");
   }

   [Fact]
   public async Task FindImplementations_DocumentNotFound_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _findImplementations.FindImplementationsAsync(
            new LookupByPosition { FilePath = NonExistentFile, Line = 1, Column = 1 },
            cancellationToken: CT),
         "FILE_NOT_IN_SOLUTION");
   }

   [Fact]
   public async Task GetTypeHierarchy_DocumentNotFound_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _getTypeHierarchy.GetTypeHierarchyAsync(
            new LookupByPosition { FilePath = NonExistentFile, Line = 1, Column = 1 },
            cancellationToken: CT),
         "FILE_NOT_IN_SOLUTION");
   }

   [Fact]
   public async Task FindCallers_DocumentNotFound_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _findCallers.FindCallersAsync(
            new LookupByPosition { FilePath = NonExistentFile, Line = 1, Column = 1 },
            cancellationToken: CT),
         "FILE_NOT_IN_SOLUTION");
   }

   #endregion

   #region SearchTools - Throw ToolException

   [Fact]
   public async Task GetFileOutline_DocumentNotFound_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _getFileOutline.GetFileOutlineAsync(NonExistentFile, ct: CT),
         ErrorCodes.FileNotInSolution);
   }

   #endregion

   #region AnalysisTools - Throw Exceptions

   [Fact]
   public async Task GetComplexityMetrics_DocumentNotFound_ThrowsException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _getComplexityMetrics.GetComplexityMetricsAsync(
            new ComplexityTargetFile { FilePath = NonExistentFile },
            cancellationToken: CT),
         "FILE_NOT_IN_SOLUTION");
   }

   [Fact]
   public async Task AnalyzeDataFlow_DocumentNotFound_ThrowsToolException()
   {
      var target = new DataFlowTargetByLines { FilePath = NonExistentFile, StartLine = 1, EndLine = 5 };
      await AssertThrowsToolExceptionAsync(() =>
         _analyzeDataFlow.AnalyzeDataFlowAsync(target, CT),
         ErrorCodes.FileNotInSolution);
   }

   [Fact]
   public async Task AnalyzeControlFlow_DocumentNotFound_ThrowsToolException()
   {
      var target = new ControlFlowTargetByLines { FilePath = NonExistentFile, StartLine = 1, EndLine = 5 };
      await AssertThrowsToolExceptionAsync(() =>
         _analyzeControlFlow.AnalyzeControlFlowAsync(target, CT),
         ErrorCodes.FileNotInSolution);
   }

   #endregion

   #region RefactoringTools - Throw Exceptions

   [Fact]
   public async Task RenameSymbol_DocumentNotFound_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(() =>
         _renameSymbol.RenameSymbolAsync(
            new LookupByPosition { FilePath = NonExistentFile, Line = 1, Column = 1 },
            newName: "NewName",
            cancellationToken: CT),
         "FILE_NOT_IN_SOLUTION");
   }

   [Fact]
   public async Task GetCodeActions_DocumentNotFound_ThrowsToolException()
   {
      var target = new PositionTarget { FilePath = NonExistentFile, Line = 1, Column = 1 };
      await AssertThrowsToolExceptionAsync(() =>
         _getCodeActions.GetCodeActionsAsync(target, cancellationToken: CT),
         ErrorCodes.FileNotInSolution);
   }

   #endregion

   #region IntelligenceTools - Throw Exceptions

   [Fact]
   public async Task GetFileOverview_DocumentNotFound_ThrowsException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _getFileOverview.GetFileOverviewAsync(NonExistentFile, ct: CT),
         ErrorCodes.FileNotInSolution);
   }

   [Fact]
   public async Task GetOutgoingCalls_DocumentNotFound_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _getOutgoingCalls.GetOutgoingCallsAsync(
            new LookupByPosition { FilePath = NonExistentFile, Line = 1, Column = 1 },
            cancellationToken: CT),
         "FILE_NOT_IN_SOLUTION");
   }

   #endregion
}
