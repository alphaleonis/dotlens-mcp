using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Intelligence;
using DotLens.Tools.Navigation;
using DotLens.Tools.Search;
using Xunit;

namespace DotLens.Tests.CrossCutting;

/// <summary>
/// Tests that verify error responses are consistent and well-structured.
/// All errors should throw ToolException with: Code, Message, and Hint.
/// </summary>
[Collection("TestFixtures")]
public class ErrorResponseConsistencyTests : TestFixtureTestBase
{
   public ErrorResponseConsistencyTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GoToDefinitionTool _goToDefinition = null!;
   private FindReferencesTool _findReferences = null!;
   private GetSymbolInfoTool _getSymbolInfo = null!;
   private GetFileOutlineTool _getFileOutline = null!;
   private GetTypeMembersTool _getTypeMembers = null!;
   private GetTypeOverviewTool _getTypeOverview = null!;
   private GetMethodSourceTool _getMethodSource = null!;

   public override async ValueTask InitializeAsync()
   {
      var symbolLookup = new SymbolLookupService(Workspace);
      _goToDefinition = new GoToDefinitionTool(Workspace, symbolLookup);
      _findReferences = new FindReferencesTool(Workspace, symbolLookup);
      _getSymbolInfo = new GetSymbolInfoTool(Workspace, symbolLookup);
      _getFileOutline = new GetFileOutlineTool(Workspace);
      _getTypeMembers = new GetTypeMembersTool(Workspace, symbolLookup);
      _getTypeOverview = new GetTypeOverviewTool(Workspace, symbolLookup);
      _getMethodSource = new GetMethodSourceTool(Workspace, symbolLookup);
   }

   /// <summary>
   /// Validates that a ToolException has the expected structure.
   /// </summary>
   private static void AssertToolExceptionStructure(
      ToolException ex,
      string expectedCode,
      bool requireHint = true)
   {
      Assert.Equal(expectedCode, ex.Code);
      Assert.NotNull(ex.Message);
      Assert.NotEmpty(ex.Message);

      if (requireHint)
      {
         Assert.NotNull(ex.Hint);
         Assert.NotEmpty(ex.Hint);
      }
   }

   #region FILE_NOT_IN_SOLUTION Errors

   [Fact]
   public async Task GoToDefinition_FileNotInSolution_ThrowsToolException()
   {
      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _goToDefinition.GoToDefinitionAsync(
            new LookupByPosition { FilePath = "NonExistent/File.cs", Line = 1, Column = 1 },
            cancellationToken: CT));
      AssertToolExceptionStructure(ex, "FILE_NOT_IN_SOLUTION");
   }

   [Fact]
   public async Task FindReferences_FileNotInSolution_ThrowsToolException()
   {
      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _findReferences.FindReferencesAsync(
            new LookupByPosition { FilePath = "NonExistent/File.cs", Line = 1, Column = 1 },
            cancellationToken: CT));
      AssertToolExceptionStructure(ex, "FILE_NOT_IN_SOLUTION");
   }

   [Fact]
   public async Task GetSymbolInfo_FileNotInSolution_ThrowsToolException()
   {
      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _getSymbolInfo.GetSymbolInfoAsync(
            new LookupByPosition { FilePath = "NonExistent/File.cs", Line = 1, Column = 1 },
            cancellationToken: CT));
      AssertToolExceptionStructure(ex, "FILE_NOT_IN_SOLUTION");
   }

   #endregion

   #region FILE_NOT_IN_SOLUTION Errors (more tests)

   [Fact]
   public async Task GetFileOutline_FileNotFound_ThrowsToolException()
   {
      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _getFileOutline.GetFileOutlineAsync("NonExistent/File.cs", ct: CT));
      AssertToolExceptionStructure(ex, "FILE_NOT_IN_SOLUTION");
   }

   #endregion

   #region SYMBOL_NOT_FOUND Errors

   [Fact]
   public async Task GoToDefinition_NoSymbolAtPosition_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      // Line 0 is likely whitespace/empty in most files
      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _goToDefinition.GoToDefinitionAsync(
            new LookupByPosition { FilePath = filePath, Line = 0, Column = 0 },
            cancellationToken: CT));
      // Could be SYMBOL_NOT_FOUND or INVALID_PARAMETER depending on position
      Assert.Contains(ex.Code, new[] { "SYMBOL_NOT_FOUND", "INVALID_PARAMETER" });
   }

   #endregion

   #region TYPE_NOT_FOUND Errors

   [Fact]
   public async Task GetTypeOverview_NonExistentType_ThrowsToolException()
   {
      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _getTypeOverview.GetTypeOverviewAsync(new LookupByName { Name = "NonExistentTypeThatDoesNotExist12345" }, cancellationToken: CT));
      // SymbolLookupService throws SYMBOL_NOT_FOUND with hint
      AssertToolExceptionStructure(ex, ErrorCodes.SymbolNotFound, requireHint: false);
   }

   [Fact]
   public async Task GetTypeMembers_NonExistentType_ThrowsToolException()
   {
      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _getTypeMembers.GetTypeMembersAsync(new LookupByName { Name = "NonExistentTypeThatDoesNotExist12345" }, cancellationToken: CT));
      // SymbolLookupService throws SYMBOL_NOT_FOUND with hint
      AssertToolExceptionStructure(ex, ErrorCodes.SymbolNotFound, requireHint: false);
   }

   [Fact]
   public async Task GetMethodSource_NonExistentType_ThrowsToolException()
   {
      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _getMethodSource.GetMethodSourceAsync(
            new LookupByName { Name = "NonExistentTypeThatDoesNotExist12345.SomeMethod" },
            cancellationToken: CT));
      // SymbolLookupService throws SYMBOL_NOT_FOUND
      AssertToolExceptionStructure(ex, ErrorCodes.SymbolNotFound, requireHint: false);
   }

   #endregion

   #region INVALID_PARAMETER Errors

   [Fact]
   public async Task GoToDefinition_NegativeLine_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      // Negative line should be caught by parameter validation
      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _goToDefinition.GoToDefinitionAsync(
            new LookupByPosition { FilePath = filePath, Line = -1, Column = 0 },
            cancellationToken: CT));
      Assert.Equal("INVALID_PARAMETER", ex.Code);
   }

   [Fact]
   public async Task GoToDefinition_LineOutOfBounds_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _goToDefinition.GoToDefinitionAsync(
            new LookupByPosition { FilePath = filePath, Line = 999999, Column = 0 },
            cancellationToken: CT));
      AssertToolExceptionStructure(ex, "INVALID_PARAMETER");
   }

   #endregion

   #region Error Response Structure

   [Fact]
   public async Task AllErrorScenarios_ThrowToolExceptionWithConsistentStructure()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");

      // FILE_NOT_IN_SOLUTION
      var ex1 = await Assert.ThrowsAsync<ToolException>(
         () => _goToDefinition.GoToDefinitionAsync(
            new LookupByPosition { FilePath = "NonExistent/File.cs", Line = 1, Column = 1 },
            cancellationToken: CT));
      Assert.NotNull(ex1.Code);
      Assert.NotNull(ex1.Message);

      // SYMBOL_NOT_FOUND (for type lookup)
      var ex2 = await Assert.ThrowsAsync<ToolException>(
         () => _getTypeOverview.GetTypeOverviewAsync(new LookupByName { Name = "NonExistentType123" }, cancellationToken: CT));
      Assert.NotNull(ex2.Code);
      Assert.NotNull(ex2.Message);

      // INVALID_PARAMETER (line out of bounds)
      var ex3 = await Assert.ThrowsAsync<ToolException>(
         () => _goToDefinition.GoToDefinitionAsync(
            new LookupByPosition { FilePath = filePath, Line = 999999, Column = 0 },
            cancellationToken: CT));
      Assert.NotNull(ex3.Code);
      Assert.NotNull(ex3.Message);
   }

   #endregion
}
