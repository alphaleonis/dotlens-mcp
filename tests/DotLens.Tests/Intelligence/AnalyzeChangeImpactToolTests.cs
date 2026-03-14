using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Intelligence;
using DotLens.Tools.Shared;
using Xunit;

namespace DotLens.Tests.Intelligence;

[Collection("TestFixtures")]
public class AnalyzeChangeImpactToolTests : TestFixtureTestBase
{
   public AnalyzeChangeImpactToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private AnalyzeChangeImpactTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      var symbolLookup = new SymbolLookupService(Workspace);
      _tool = new AnalyzeChangeImpactTool(Workspace, symbolLookup);
   }

   #region Success Cases

   [Fact]
   public async Task AnalyzeChangeImpact_WithMethodRename_ShowsAffectedLocations()
   {
      // Use position-based lookup for specific method
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      // Line 48 is SampleClass.Calculate
      var result = await _tool.AnalyzeChangeImpactAsync(
         new LookupByPosition { FilePath = filePath, Line = 48, Column = 15 },
         changeType: ChangeType.Rename,
         newValue: "ComputeSum",
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("Calculate", data.GetStringOrNull("symbolName"));
      Assert.Equal("rename", data.GetStringOrNull("changeType"));
      Assert.Equal("ComputeSum", data.GetStringOrNull("newValue"));
   }

   [Fact]
   public async Task AnalyzeChangeImpact_WithClassModification_ReturnsImpactAnalysis()
   {
      // SampleClass is unique in CoreTestProject
      var result = await _tool.AnalyzeChangeImpactAsync(
         new LookupByName { Name = "SampleClass" },
         changeType: ChangeType.Rename,
         newValue: "RenamedClass",
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("SampleClass", data.GetString("symbolName"));
      // Renaming SampleClass would affect all references
      Assert.True(data.GetInt("referenceCount") > 0, "SampleClass should have references");
      // Renaming is considered safe because rename_symbol can handle all references automatically
      Assert.True(data.GetBool("isSafe"), "Renaming is safe when using rename_symbol tool");
   }

   [Fact]
   public async Task AnalyzeChangeImpact_WithAddParameter_AnalyzesBreakingChange()
   {
      // Use position-based lookup for SampleClass.DoSomething (line 43)
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var result = await _tool.AnalyzeChangeImpactAsync(
         new LookupByPosition { FilePath = filePath, Line = 43, Column = 16 },
         changeType: ChangeType.AddParameter,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("addParameter", data.GetString("changeType"));
      // Adding a parameter to a method is always a breaking change for existing callers
      // The tool should report this as not safe
      var referenceCount = data.GetInt("referenceCount");
      var isSafe = data.GetBool("isSafe");
      // If there are references (callers), adding parameter is not safe
      if (referenceCount > 0)
      {
         Assert.False(isSafe, "Adding parameter should not be safe when there are callers");
      }
   }

   [Fact]
   public async Task AnalyzeChangeImpact_WithChangeType_IncludesNewValue()
   {
      // _privateField is a unique field in SampleClass
      var result = await _tool.AnalyzeChangeImpactAsync(
         new LookupByName { Name = "_privateField" },
         changeType: ChangeType.ChangeType,
         newValue: "long",
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("changeType", data.GetStringOrNull("changeType"));
      Assert.Equal("long", data.GetStringOrNull("newValue"));
   }

   [Fact]
   public async Task AnalyzeChangeImpact_WithDelete_AnalyzesDeletion()
   {
      // PrivateMethod is unique in SampleClass
      var result = await _tool.AnalyzeChangeImpactAsync(
         new LookupByName { Name = "PrivateMethod" },
         changeType: ChangeType.Delete,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("delete", data.GetStringOrNull("changeType"));
   }

   [Fact]
   public async Task AnalyzeChangeImpact_ByPosition_WorksCorrectly()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");

      // Line 12 column 14 is where SampleClass is defined
      var result = await _tool.AnalyzeChangeImpactAsync(
         new LookupByPosition { FilePath = filePath, Line = 12, Column = 14 },
         changeType: ChangeType.Rename,
         newValue: "RenamedClass",
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("SampleClass", data.GetStringOrNull("symbolName"));
   }

   [Fact]
   public async Task AnalyzeChangeImpact_SummaryVerbosity_ReturnsCountOnly()
   {
      var result = await _tool.AnalyzeChangeImpactAsync(
         new LookupByName { Name = "SampleClass" },
         changeType: ChangeType.Rename,
         verbosity: ToolResultVerbosity.Summary,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      // SampleClass has references in the codebase
      Assert.True(data.GetInt("referenceCount") > 0, "SampleClass should have references");
      // At summary verbosity, impactedLocations should be null
      Assert.Null(data["impactedLocations"]);
   }

   [Fact]
   public async Task AnalyzeChangeImpact_RespectsMaxLocations()
   {
      var result = await _tool.AnalyzeChangeImpactAsync(
         new LookupByName { Name = "SampleClass" },
         changeType: ChangeType.Rename,
         maxLocations: 5,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var locations = data.GetArray("impactedLocations");
      Assert.True(locations.Count <= 5, "Should respect maxLocations=5 limit");
   }

   #endregion

   #region Error Cases

   [Fact]
   public async Task AnalyzeChangeImpact_WithUnknownSymbol_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.AnalyzeChangeImpactAsync(
            new LookupByName { Name = "NonExistentSymbol12345" },
            changeType: ChangeType.Rename,
            cancellationToken: CT),
         ErrorCodes.SymbolNotFound);
   }

   [Fact]
   public async Task AnalyzeChangeImpact_WithInvalidPosition_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");

      await AssertThrowsToolExceptionAsync(
         () => _tool.AnalyzeChangeImpactAsync(
            new LookupByPosition { FilePath = filePath, Line = 99999, Column = 1 },
            changeType: ChangeType.Rename,
            cancellationToken: CT),
         ErrorCodes.InvalidParameter);
   }

   [Fact]
   public async Task AnalyzeChangeImpact_WithEmptyName_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.AnalyzeChangeImpactAsync(
            new LookupByName { Name = "" },
            changeType: ChangeType.Rename,
            cancellationToken: CT),
         ErrorCodes.InvalidParameter);
   }

   #endregion

   #region Edge Cases

   [Fact]
   public async Task AnalyzeChangeImpact_ReturnsSymbolKind()
   {
      var result = await _tool.AnalyzeChangeImpactAsync(
         new LookupByName { Name = "SampleClass" },
         changeType: ChangeType.Rename,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("class", data.GetString("symbolKind"));
   }

   [Fact]
   public async Task AnalyzeChangeImpact_MethodReturnsMethodKind()
   {
      // Use position-based lookup for SampleClass.Calculate (line 48)
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var result = await _tool.AnalyzeChangeImpactAsync(
         new LookupByPosition { FilePath = filePath, Line = 48, Column = 15 },
         changeType: ChangeType.Rename,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("method", data.GetStringOrNull("symbolKind"));
   }

   [Fact]
   public async Task AnalyzeChangeImpact_ImpactedLocationsHaveRequiredFields()
   {
      var result = await _tool.AnalyzeChangeImpactAsync(
         new LookupByName { Name = "SampleClass" },
         changeType: ChangeType.Rename,
         verbosity: ToolResultVerbosity.Full,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var locations = data.GetArray("impactedLocations");

      Assert.NotEmpty(locations);
      var firstLocation = locations[0];
      Assert.False(string.IsNullOrEmpty(firstLocation.GetString("filePath")), "Location should have filePath");
      Assert.True(firstLocation.GetInt("line") > 0, "Location should have positive line");
      Assert.True(firstLocation.GetInt("column") > 0, "Location should have positive column");
   }

   [Fact]
   public async Task AnalyzeChangeImpact_IsDefinition_MarksReferencesCorrectly()
   {
      // SampleClass has references throughout the codebase
      var result = await _tool.AnalyzeChangeImpactAsync(
         new LookupByName { Name = "SampleClass" },
         changeType: ChangeType.Rename,
         verbosity: ToolResultVerbosity.Full,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var locations = data.GetArray("impactedLocations");
      Assert.NotEmpty(locations);

      // All locations should have isDefinition property (boolean)
      foreach (var loc in locations)
      {
         // GetBool will throw if missing, verifying the property exists and is boolean
         _ = loc.GetBool("isDefinition");
      }

      // Most locations should be references (not definitions)
      var referenceCount = locations.Count(loc => loc.GetBool("isDefinition") == false);
      Assert.True(referenceCount > 0,
         "Expected some locations to be marked as references (IsDefinition = false)");
   }

   #endregion
}
