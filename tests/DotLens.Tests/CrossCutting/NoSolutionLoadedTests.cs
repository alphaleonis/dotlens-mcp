using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Analysis;
using DotLens.Tools.Intelligence;
using DotLens.Tools.Navigation;
using DotLens.Tools.Refactoring;
using DotLens.Tools.Search;
using Xunit;

namespace DotLens.Tests.CrossCutting;

/// <summary>
/// Tests tool behavior when no solution is loaded.
/// All tools should throw InvalidOperationException with appropriate message.
/// Note: The MCP server layer catches these exceptions and converts them to error responses.
/// </summary>
public class NoSolutionLoadedTests : MockableRoslynServiceTestBase
{
   // Navigation tools
   private GetSymbolInfoTool _getSymbolInfo = null!;
   private GoToDefinitionTool _goToDefinition = null!;
   private FindReferencesTool _findReferences = null!;
   private FindImplementationsTool _findImplementations = null!;
   private GetTypeHierarchyTool _getTypeHierarchy = null!;
   private FindCallersTool _findCallers = null!;

   // Search tools
   private FindSymbolsTool _findSymbols = null!;
   private GetFileOutlineTool _getFileOutline = null!;
   private GetTypeMembersTool _getTypeMembers = null!;

   // Analysis tools
   private GetDiagnosticsTool _getDiagnostics = null!;
   private GetComplexityMetricsTool _getComplexityMetrics = null!;
   private AnalyzeDataFlowTool _analyzeDataFlow = null!;
   private GetProjectStructureTool _getProjectStructure = null!;

   // Refactoring tools
   private RenameSymbolTool _renameSymbol = null!;
   private GetCodeActionsTool _getCodeActions = null!;

   // Intelligence tools
   private GetFileOverviewTool _getFileOverview = null!;
   private GetTypeOverviewTool _getTypeOverview = null!;
   private GetMethodSourceTool _getMethodSource = null!;

   // Services
   private SymbolLookupService _symbolLookup = null!;

   public NoSolutionLoadedTests()
   {
      SetupNoSolutionLoaded();

      // Initialize services
      _symbolLookup = new SymbolLookupService(FakeWorkspaceManager);

      // Initialize navigation tools
      _getSymbolInfo = new GetSymbolInfoTool(FakeWorkspaceManager, _symbolLookup);
      _goToDefinition = new GoToDefinitionTool(FakeWorkspaceManager, _symbolLookup);
      _findReferences = new FindReferencesTool(FakeWorkspaceManager, _symbolLookup);
      _findImplementations = new FindImplementationsTool(FakeWorkspaceManager, _symbolLookup);
      _getTypeHierarchy = new GetTypeHierarchyTool(FakeWorkspaceManager, _symbolLookup);
      _findCallers = new FindCallersTool(FakeWorkspaceManager, _symbolLookup);

      _findSymbols = new FindSymbolsTool(FakeWorkspaceManager);
      _getFileOutline = new GetFileOutlineTool(FakeWorkspaceManager);
      _getTypeMembers = new GetTypeMembersTool(FakeWorkspaceManager, _symbolLookup);

      _getDiagnostics = new GetDiagnosticsTool(FakeWorkspaceManager);
      _getComplexityMetrics = new GetComplexityMetricsTool(FakeWorkspaceManager, _symbolLookup);
      _analyzeDataFlow = new AnalyzeDataFlowTool(FakeWorkspaceManager, _symbolLookup);
      _getProjectStructure = new GetProjectStructureTool(FakeWorkspaceManager);

      _renameSymbol = new RenameSymbolTool(FakeWorkspaceManager, _symbolLookup);
      _getCodeActions = new GetCodeActionsTool(FakeWorkspaceManager);

      _getFileOverview = new GetFileOverviewTool(FakeWorkspaceManager);
      _getTypeOverview = new GetTypeOverviewTool(FakeWorkspaceManager, _symbolLookup);
      _getMethodSource = new GetMethodSourceTool(FakeWorkspaceManager, _symbolLookup);
   }

   private async Task AssertThrowsNoSolutionLoadedAsync(Func<Task> action)
   {
      await Assert.ThrowsAsync<InvalidOperationException>(action);
   }

   #region NavigationTools

   [Fact]
   public async Task GetSymbolInfo_NoSolutionLoaded_ThrowsException()
   {
      await AssertThrowsNoSolutionLoadedAsync(() =>
         _getSymbolInfo.GetSymbolInfoAsync(
            new LookupByPosition { FilePath = "test.cs", Line = 1, Column = 1 },
            cancellationToken: CT));
   }

   [Fact]
   public async Task GoToDefinition_NoSolutionLoaded_ThrowsException()
   {
      await AssertThrowsNoSolutionLoadedAsync(() =>
         _goToDefinition.GoToDefinitionAsync(
            new LookupByPosition { FilePath = "test.cs", Line = 1, Column = 1 },
            cancellationToken: CT));
   }

   [Fact]
   public async Task FindReferences_NoSolutionLoaded_ThrowsException()
   {
      await AssertThrowsNoSolutionLoadedAsync(() =>
         _findReferences.FindReferencesAsync(
            new LookupByPosition { FilePath = "test.cs", Line = 1, Column = 1 },
            cancellationToken: CT));
   }

   [Fact]
   public async Task FindImplementations_NoSolutionLoaded_ThrowsException()
   {
      await AssertThrowsNoSolutionLoadedAsync(() =>
         _findImplementations.FindImplementationsAsync(
            new LookupByPosition { FilePath = "test.cs", Line = 1, Column = 1 },
            cancellationToken: CT));
   }

   [Fact]
   public async Task GetTypeHierarchy_NoSolutionLoaded_ThrowsException()
   {
      await AssertThrowsNoSolutionLoadedAsync(() =>
         _getTypeHierarchy.GetTypeHierarchyAsync(
            new LookupByPosition { FilePath = "test.cs", Line = 1, Column = 1 },
            cancellationToken: CT));
   }

   [Fact]
   public async Task FindCallers_NoSolutionLoaded_ThrowsException()
   {
      await AssertThrowsNoSolutionLoadedAsync(() =>
         _findCallers.FindCallersAsync(
            new LookupByPosition { FilePath = "test.cs", Line = 1, Column = 1 },
            cancellationToken: CT));
   }

   #endregion

   #region SearchTools

   [Fact]
   public async Task SearchSymbols_NoSolutionLoaded_ThrowsException()
   {
      await AssertThrowsNoSolutionLoadedAsync(() =>
         _findSymbols.FindSymbolsAsync(name: "TestClass", cancellationToken: CT));
   }

   [Fact]
   public async Task GetFileOutline_NoSolutionLoaded_ThrowsException()
   {
      await AssertThrowsNoSolutionLoadedAsync(() =>
         _getFileOutline.GetFileOutlineAsync("test.cs", ct: CT));
   }

   [Fact]
   public async Task GetTypeMembers_NoSolutionLoaded_ThrowsException()
   {
      await AssertThrowsNoSolutionLoadedAsync(() =>
         _getTypeMembers.GetTypeMembersAsync(new LookupByName { Name = "TestClass" }, cancellationToken: CT));
   }

   #endregion

   #region AnalysisTools

   [Fact]
   public async Task GetDiagnostics_NoSolutionLoaded_ThrowsException()
   {
      await AssertThrowsNoSolutionLoadedAsync(() =>
         _getDiagnostics.GetDiagnosticsAsync(cancellationToken: CT));
   }

   [Fact]
   public async Task GetComplexityMetrics_NoSolutionLoaded_ThrowsException()
   {
      await AssertThrowsNoSolutionLoadedAsync(() =>
         _getComplexityMetrics.GetComplexityMetricsAsync(
            new ComplexityTargetFile { FilePath = "test.cs" },
            cancellationToken: CT));
   }

   [Fact]
   public async Task AnalyzeDataFlow_NoSolutionLoaded_ThrowsException()
   {
      var target = new DataFlowTargetByLines { FilePath = "test.cs", StartLine = 1, EndLine = 5 };
      await AssertThrowsNoSolutionLoadedAsync(() =>
         _analyzeDataFlow.AnalyzeDataFlowAsync(target, CT));
   }

   [Fact]
   public async Task GetProjectStructure_NoSolutionLoaded_ThrowsException()
   {
      await AssertThrowsNoSolutionLoadedAsync(() =>
         _getProjectStructure.GetProjectStructureAsync());
   }

   #endregion

   #region RefactoringTools

   [Fact]
   public async Task RenameSymbol_NoSolutionLoaded_ThrowsException()
   {
      await AssertThrowsNoSolutionLoadedAsync(() =>
         _renameSymbol.RenameSymbolAsync(
            new LookupByPosition { FilePath = "test.cs", Line = 1, Column = 1 },
            "NewName",
            cancellationToken: CT));
   }

   [Fact]
   public async Task GetCodeActions_NoSolutionLoaded_ThrowsException()
   {
      var target = new PositionTarget { FilePath = "test.cs", Line = 1, Column = 1 };
      await AssertThrowsNoSolutionLoadedAsync(() =>
         _getCodeActions.GetCodeActionsAsync(target, cancellationToken: CT));
   }

   #endregion

   #region IntelligenceTools

   [Fact]
   public async Task GetFileOverview_NoSolutionLoaded_ThrowsException()
   {
      await AssertThrowsNoSolutionLoadedAsync(() =>
         _getFileOverview.GetFileOverviewAsync("test.cs", ct: CT));
   }

   [Fact]
   public async Task GetTypeOverview_NoSolutionLoaded_ThrowsException()
   {
      await AssertThrowsNoSolutionLoadedAsync(() =>
         _getTypeOverview.GetTypeOverviewAsync(new LookupByName { Name = "TestClass" }, cancellationToken: CT));
   }

   [Fact]
   public async Task GetMethodSource_NoSolutionLoaded_ThrowsException()
   {
      await AssertThrowsNoSolutionLoadedAsync(() =>
         _getMethodSource.GetMethodSourceAsync(new LookupByName { Name = "TestClass.TestMethod" }, cancellationToken: CT));
   }

   #endregion
}
