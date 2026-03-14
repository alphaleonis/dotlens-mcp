using System.Text.Json;
using System.Text.Json.Nodes;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Analysis;
using DotLens.Tools.Intelligence;
using DotLens.Tools.Navigation;
using DotLens.Tools.Search;
using Xunit;

namespace DotLens.Tests.CrossCutting;

/// <summary>
/// Tests for security boundaries and input validation.
/// Ensures proper error handling for malicious or malformed inputs.
/// </summary>
[Collection("TestFixtures")]
public class SecurityBoundaryTests : TestFixtureTestBase
{
   public SecurityBoundaryTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetSymbolInfoTool _getSymbolInfo = null!;
   private GoToDefinitionTool _goToDefinition = null!;
   private FindReferencesTool _findReferences = null!;
   private FindSymbolsTool _findSymbols = null!;
   private GetTypeMembersTool _getTypeMembers = null!;
   private GetMethodSourceTool _getMethodSource = null!;
   private AnalyzeDataFlowTool _analyzeDataFlow = null!;

   public override async ValueTask InitializeAsync()
   {
      var symbolLookup = new SymbolLookupService(Workspace);
      _getSymbolInfo = new GetSymbolInfoTool(Workspace, symbolLookup);
      _goToDefinition = new GoToDefinitionTool(Workspace, symbolLookup);
      _findReferences = new FindReferencesTool(Workspace, symbolLookup);
      _findSymbols = new FindSymbolsTool(Workspace);
      _getTypeMembers = new GetTypeMembersTool(Workspace, symbolLookup);
      _getMethodSource = new GetMethodSourceTool(Workspace, symbolLookup);
      _analyzeDataFlow = new AnalyzeDataFlowTool(Workspace, symbolLookup);
   }

   #region Path Traversal Prevention

   [Fact]
   public async Task GetSymbolInfo_PathTraversal_ThrowsToolException()
   {
      // Attempt to access file outside solution using ../
      await AssertThrowsToolExceptionAsync(
         () => _getSymbolInfo.GetSymbolInfoAsync(
            new LookupByPosition { FilePath = "../../etc/passwd", Line = 1, Column = 1 },
            cancellationToken: CT));
   }

   [Fact]
   public async Task GetSymbolInfo_AbsolutePathOutsideSolution_ThrowsToolException()
   {
      // Attempt to access file with absolute path outside solution
      await AssertThrowsToolExceptionAsync(
         () => _getSymbolInfo.GetSymbolInfoAsync(
            new LookupByPosition { FilePath = "/etc/passwd", Line = 1, Column = 1 },
            cancellationToken: CT));
   }

   [Fact]
   public async Task GetMethodSource_NonExistentType_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _getMethodSource.GetMethodSourceAsync(
            new LookupByName { Name = "NonExistentType.DoesNotExist" },
            cancellationToken: CT),
         "SYMBOL_NOT_FOUND");
   }

   #endregion

   #region Input Validation - Null/Empty Parameters

   [Fact]
   public async Task FindSymbols_EmptyQuery_ThrowsToolException()
   {
      // Empty query with no filters should throw - requires at least one filter
      await AssertThrowsToolExceptionAsync(
         () => _findSymbols.FindSymbolsAsync(
            name: "",
            cancellationToken: CT),
         "INVALID_PARAMETER");
   }

   [Fact]
   public async Task GetTypeMembers_EmptyTypeName_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _getTypeMembers.GetTypeMembersAsync(
            new LookupByName { Name = "" },
            cancellationToken: CT),
         "INVALID_PARAMETER");
   }

   [Fact]
   public async Task GoToDefinition_EmptyFilePath_ThrowsToolException()
   {
      // Empty file path should throw ToolException with INVALID_PARAMETER
      await AssertThrowsToolExceptionAsync(
         () => _goToDefinition.GoToDefinitionAsync(
            new LookupByPosition { FilePath = "", Line = 1, Column = 1 },
            cancellationToken: CT),
         "INVALID_PARAMETER");
   }

   #endregion

   #region Input Validation - Invalid Line/Column Values

   [Fact]
   public async Task GetSymbolInfo_NegativeLine_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      await AssertThrowsToolExceptionAsync(
         () => _getSymbolInfo.GetSymbolInfoAsync(
            new LookupByPosition { FilePath = filePath, Line = -1, Column = 1 },
            cancellationToken: CT),
         "INVALID_PARAMETER");
   }

   [Fact]
   public async Task GetSymbolInfo_NegativeColumn_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      // Line 1 in Classes.cs is typically a using statement with content, so column -1 is truly invalid
      await AssertThrowsToolExceptionAsync(
         () => _getSymbolInfo.GetSymbolInfoAsync(
            new LookupByPosition { FilePath = filePath, Line = 10, Column = -1 },
            cancellationToken: CT),
         "INVALID_PARAMETER");
   }

   [Fact]
   public async Task GetSymbolInfo_LineExceedsFile_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      await AssertThrowsToolExceptionAsync(
         () => _getSymbolInfo.GetSymbolInfoAsync(
            new LookupByPosition { FilePath = filePath, Line = 999999, Column = 1 },
            cancellationToken: CT),
         "INVALID_PARAMETER");
   }

   [Fact]
   public async Task GetSymbolInfo_ColumnExceedsLine_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      await AssertThrowsToolExceptionAsync(
         () => _getSymbolInfo.GetSymbolInfoAsync(
            new LookupByPosition { FilePath = filePath, Line = 1, Column = 999999 },
            cancellationToken: CT),
         "INVALID_PARAMETER");
   }

   #endregion

   #region Input Validation - Malformed Parameters

   [Fact]
   public async Task SearchSymbols_InvalidGlobPattern_HandledGracefully()
   {
      // Malformed glob patterns should not crash
      var result = await _findSymbols.FindSymbolsAsync(
         name: "[invalid",
         cancellationToken: CT);

      // Should either return error or empty results, not crash
      var json = JsonNode.Parse(JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase))!;
      Assert.NotNull(json);
   }

   // SemanticQuery_InvalidModifier test removed: accessibility is now an enum type,
   // so invalid values are rejected at compile-time rather than runtime.

   #endregion

   #region Edge Cases

   [Fact]
   public async Task AnalyzeDataFlow_SwappedLines_NormalizesRange()
   {
      var filePath = GetCoreTestProjectPath("Patterns/ControlFlow.cs");
      // End line before start line - tool should normalize (swap) them
      // Lines 77-82 (1-based) is ControlFlowExamples.SumRange method body
      var target = new DataFlowTargetByLines
      {
         FilePath = filePath,
         StartLine = 82,
         EndLine = 77  // Before start - should be normalized
      };

      var result = await _analyzeDataFlow.AnalyzeDataFlowAsync(target, CT);

      // Should succeed with normalized region
      var data = GetData(result);
      var region = data["region"];
      Assert.NotNull(region);
      var actualStart = region.GetIntOrNull("startLine");
      var actualEnd = region.GetIntOrNull("endLine");
      Assert.True(actualStart <= actualEnd, $"Region should be normalized: {actualStart} <= {actualEnd}");
   }

   [Fact]
   public async Task GetSymbolInfo_OnComment_ReturnsEnclosingSymbol()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      // Line 5 is a comment (// =========...) - Roslyn returns enclosing namespace
      var result = await _getSymbolInfo.GetSymbolInfoAsync(
         new LookupByPosition { FilePath = filePath, Line = 5, Column = 5 },
         cancellationToken: CT);

      // Roslyn finds the enclosing namespace even when positioned on a comment
      Assert.NotNull(result);
   }

   [Fact]
   public async Task FindReferences_OnComment_ReturnsEnclosingSymbolReferences()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      // Line 5 is a comment (// =========...) - Roslyn returns enclosing namespace
      var result = await _findReferences.FindReferencesAsync(
         new LookupByPosition { FilePath = filePath, Line = 5, Column = 5 },
         cancellationToken: CT);

      // Roslyn finds the enclosing namespace and returns its references
      Assert.NotNull(result);
   }

   #endregion
}
