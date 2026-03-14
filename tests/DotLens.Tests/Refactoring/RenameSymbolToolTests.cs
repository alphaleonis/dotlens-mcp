using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Refactoring;
using Xunit;

namespace DotLens.Tests.Refactoring;

public class RenameSymbolToolTests : IsolatedProjectTestBase
{

   private RenameSymbolTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      var symbolLookup = new SymbolLookupService(WorkspaceManager);
      _tool = new RenameSymbolTool(WorkspaceManager, symbolLookup);
   }

   #region Success Cases

   [Fact]
   public async Task RenameSymbol_ByPosition_WithPreview_ShowsChanges()
   {
      // _privateField is at line 15 in Classes.cs
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");

      var result = await _tool.RenameSymbolAsync(
         new LookupByPosition { FilePath = filePath, Line = 15, Column = 16 },
         newName: "_renamedField",
         preview: true,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("_privateField", data.GetStringOrNull("symbolName"));
      Assert.Equal("_renamedField", data.GetStringOrNull("newName"));
      Assert.True(data.GetBoolOrNull("preview"));
      Assert.False(data.GetBoolOrNull("applied"));
      Assert.Equal(1, data.GetIntOrNull("totalFiles"));
   }

   [Fact]
   public async Task RenameSymbol_ByName_WithPreview_ShowsChanges()
   {
      var result = await _tool.RenameSymbolAsync(
         new LookupByName { Name = "_privateField" },
         newName: "_renamedField",
         preview: true,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("_privateField", data.GetStringOrNull("symbolName"));
      Assert.Equal("_renamedField", data.GetStringOrNull("newName"));
      Assert.True(data.GetBoolOrNull("preview"));
   }

   [Fact]
   public async Task RenameSymbol_CompactVerbosity_IncludesLocations()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");

      var result = await _tool.RenameSymbolAsync(
         new LookupByPosition { FilePath = filePath, Line = 15, Column = 16 },
         newName: "_renamedField",
         preview: true,
         verbosity: ToolResultVerbosity.Compact,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var changes = data.GetArray("changes");
      Assert.Single(changes); // _privateField is only in one file

      var firstChange = changes[0];
      var locations = firstChange.GetArray("changes");
      Assert.NotEmpty(locations);

      // Compact should have line and column (1-based, positive values)
      var firstLoc = locations[0];
      Assert.True(firstLoc.GetInt("line") >= 1, "Line should be 1-based positive");
      Assert.True(firstLoc.GetInt("column") >= 1, "Column should be 1-based positive");
   }

   [Fact]
   public async Task RenameSymbol_FullVerbosity_IncludesOldAndNewText()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");

      var result = await _tool.RenameSymbolAsync(
         new LookupByPosition { FilePath = filePath, Line = 15, Column = 16 },
         newName: "_renamedField",
         preview: true,
         verbosity: ToolResultVerbosity.Full,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var changes = data.GetArray("changes");
      Assert.Single(changes); // _privateField is only in one file

      var firstChange = changes[0];
      var locations = firstChange.GetArray("changes");
      Assert.NotEmpty(locations);

      // Full verbosity includes old and new text for the change span
      var firstLoc = locations[0];
      // In full verbosity, text may include context - verify the identifier is present
      Assert.Contains("_privateField", firstLoc.GetString("oldText"));
      Assert.Contains("_renamedField", firstLoc.GetString("newText"));
      Assert.True(firstLoc.GetInt("startLine") >= 1, "startLine should be 1-based positive");
      Assert.True(firstLoc.GetInt("endLine") >= 1, "endLine should be 1-based positive");
   }

   [Fact]
   public async Task RenameSymbol_RespectsMaxFiles()
   {
      var result = await _tool.RenameSymbolAsync(
         new LookupByName { Name = "SampleClass" },
         newName: "RenamedSampleClass",
         preview: true,
         maxFiles: 2,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var changes = data.GetArray("changes");
      Assert.NotEmpty(changes);
      Assert.True(changes.Count <= 2, "Should respect maxFiles=2 limit");
   }

   [Fact]
   public async Task RenameSymbol_ReturnsSymbolKind()
   {
      var result = await _tool.RenameSymbolAsync(
         new LookupByName { Name = "_privateField" },
         newName: "_renamedField",
         preview: true,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("field", data.GetStringOrNull("symbolKind"));
   }

   #endregion

   #region Error Cases

   [Fact]
   public async Task RenameSymbol_WithEmptyName_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");

      await AssertThrowsToolExceptionAsync(
         () => _tool.RenameSymbolAsync(
            new LookupByPosition { FilePath = filePath, Line = 15, Column = 16 },
            newName: "",
            preview: true,
            cancellationToken: CT),
         ErrorCodes.InvalidParameter);
   }

   [Fact]
   public async Task RenameSymbol_WithInvalidIdentifier_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");

      await AssertThrowsToolExceptionAsync(
         () => _tool.RenameSymbolAsync(
            new LookupByPosition { FilePath = filePath, Line = 15, Column = 16 },
            newName: "123invalid",
            preview: true,
            cancellationToken: CT),
         ErrorCodes.InvalidParameter);
   }

   [Fact]
   public async Task RenameSymbol_WithUnknownSymbol_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.RenameSymbolAsync(
            new LookupByName { Name = "NonExistentSymbol12345" },
            newName: "NewName",
            preview: true,
            cancellationToken: CT),
         ErrorCodes.SymbolNotFound);
   }

   [Fact]
   public async Task RenameSymbol_WithInvalidPosition_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");

      await AssertThrowsToolExceptionAsync(
         () => _tool.RenameSymbolAsync(
            new LookupByPosition { FilePath = filePath, Line = 99999, Column = 1 },
            newName: "NewName",
            preview: true,
            cancellationToken: CT),
         ErrorCodes.InvalidParameter);
   }

   [Fact]
   public async Task RenameSymbol_MetadataSymbol_ThrowsToolException()
   {
      // Attempting to rename a symbol from the BCL (metadata) should fail
      await AssertThrowsToolExceptionAsync(
         () => _tool.RenameSymbolAsync(
            new LookupByName { Name = "System.String.IsNullOrEmpty" },
            newName: "NewName",
            preview: true,
            cancellationToken: CT),
         ErrorCodes.SymbolInMetadata);
   }

   #endregion
}
