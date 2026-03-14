using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Navigation;
using DotLens.Tools.Search;
using Xunit;

namespace DotLens.Tests.Navigation;

[Collection("TestFixtures")]
public class GoToDefinitionToolTests : TestFixtureTestBase
{
   public GoToDefinitionToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GoToDefinitionTool _tool = null!;
   private FindSymbolsTool _findSymbols = null!;

   public override async ValueTask InitializeAsync()
   {
      var symbolLookup = new SymbolLookupService(Workspace);
      _tool = new GoToDefinitionTool(Workspace, symbolLookup);
      _findSymbols = new FindSymbolsTool(Workspace);
   }

   [Fact]
   public async Task GoToDefinition_ByPosition_ReturnsLocation()
   {
      // Search for a method in CoreTestProject
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "Calculate", kinds: [SymbolKindFilter.Method], maxResults: 10, cancellationToken: CT);
      AssertSuccess(searchResult);

      var results = GetNode(GetData(searchResult), "Items")?.AsArray();
      Assert.True(results?.Count > 0, "Should find Calculate method");

      var symbol = results![0];
      var location = GetNode(symbol, "Location");
      var relativePath = GetStringProperty(location, "FilePath");
      var file = GetAbsolutePath(relativePath!);
      var line = GetProperty<int>(location, "Line") ?? 0;
      var col = GetProperty<int>(location, "Column") ?? 0;

      var result = await _tool.GoToDefinitionAsync(
         new LookupByPosition { FilePath = file, Line = line, Column = col },
         cancellationToken: CT);
      AssertSuccess(result);
      var data = GetData(result);
      var definitions = GetNode(data, "Definitions")?.AsArray();
      Assert.NotNull(definitions);
      var definition = Assert.Single(definitions);
      var filePath = GetStringProperty(definition, "FilePath");
      Assert.NotNull(filePath);
      Assert.NotEmpty(filePath);
   }

   [Fact]
   public async Task GoToDefinition_ByName_ReturnsLocation()
   {
      // Look up SampleClass by name directly
      var result = await _tool.GoToDefinitionAsync(
         new LookupByName { Name = "SampleClass" },
         cancellationToken: CT);
      AssertSuccess(result);
      var data = GetData(result);
      var definitions = GetNode(data, "Definitions")?.AsArray();
      Assert.NotNull(definitions);
      var definition = Assert.Single(definitions);
      var filePath = GetStringProperty(definition, "FilePath");
      Assert.NotNull(filePath);
      Assert.NotEmpty(filePath);
      Assert.EndsWith("Classes.cs", filePath); // SampleClass is defined in Classes.cs
   }

   [Fact]
   public async Task GoToDefinition_WithFullVerbosity_IncludesAllDetails()
   {
      var result = await _tool.GoToDefinitionAsync(
         new LookupByName { Name = "SampleClass" },
         verbosity: ToolResultVerbosity.Full,
         cancellationToken: CT);
      AssertSuccess(result);
      var data = GetData(result);

      // Full verbosity should include symbol details
      var symbol = GetNode(data, "Symbol");
      Assert.NotNull(symbol);
      Assert.NotNull(GetStringProperty(symbol, "Name"));
   }

   [Fact]
   public async Task GoToDefinition_WithIncludeSource_ReturnsMethodSource()
   {
      var result = await _tool.GoToDefinitionAsync(
         new LookupByName { Name = "SampleClass.DoSomething" },
         includeSource: true,
         cancellationToken: CT);
      AssertSuccess(result);
      var data = GetData(result);

      var sourceCodes = GetNode(data, "SourceCodes")?.AsArray();
      Assert.NotNull(sourceCodes);
      var sourceCode = Assert.Single(sourceCodes)?.GetValue<string>();
      // Normalize newlines for platform-independent comparison (source files may have LF or CRLF)
      Assert.Equal(
         """

         // Methods
         public void DoSomething()
         {
            _privateField++;
         }

         """.ReplaceLineEndings("\n"), sourceCode?.ReplaceLineEndings("\n"));
   }

   [Fact]
   public async Task GoToDefinition_WithIncludeSource_ReturnsPropertySource()
   {
      var result = await _tool.GoToDefinitionAsync(
         new LookupByName { Name = "SampleClass.Id" },
         includeSource: true,
         cancellationToken: CT);
      AssertSuccess(result);
      var data = GetData(result);

      var sourceCodes = GetNode(data, "SourceCodes")?.AsArray();
      Assert.NotNull(sourceCodes);
      var sourceCode = Assert.Single(sourceCodes)?.GetValue<string>();
      // Normalize newlines for platform-independent comparison (source files may have LF or CRLF)
      Assert.Equal(
         """

         // Properties
         public int Id { get; set; }

         """.ReplaceLineEndings("\n"), sourceCode?.ReplaceLineEndings("\n"));
   }

   [Fact]
   public async Task GoToDefinition_WithoutIncludeSource_NoSourceCode()
   {
      var result = await _tool.GoToDefinitionAsync(
         new LookupByName { Name = "SampleClass.DoSomething" },
         includeSource: false,
         cancellationToken: CT);
      AssertSuccess(result);
      var data = GetData(result);

      var sourceCodes = GetNode(data, "SourceCodes");
      Assert.Null(sourceCodes);
   }

   [Fact]
   public async Task GoToDefinition_IncludeSourceOnType_NoSourceCode()
   {
      // Types don't support includeSource - only methods/properties/events
      var result = await _tool.GoToDefinitionAsync(
         new LookupByName { Name = "SampleClass" },
         includeSource: true,
         cancellationToken: CT);
      AssertSuccess(result);
      var data = GetData(result);

      var sourceCodes = GetNode(data, "SourceCodes");
      Assert.Null(sourceCodes);
   }

   [Fact]
   public async Task GoToDefinition_FileNotFound_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.GoToDefinitionAsync(
            new LookupByPosition { FilePath = "nonexistent.cs", Line = 1, Column = 1 },
            cancellationToken: CT));
   }

   [Fact]
   public async Task GoToDefinition_SymbolNotFound_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.GoToDefinitionAsync(
            new LookupByName { Name = "NonExistentSymbol12345" },
            cancellationToken: CT));
   }

   [Fact]
   public async Task GoToDefinition_PartialClass_AllLocations_ReturnsMultipleDefinitions()
   {
      // PartialService has 3 partial declarations: 2 in PartialTypes.cs, 1 in PartialTypes.Part2.cs
      var result = await _tool.GoToDefinitionAsync(
         new LookupByName { Name = "PartialService" },
         allLocations: true,
         cancellationToken: CT);
      AssertSuccess(result);
      var data = GetData(result);

      var definitions = GetNode(data, "Definitions")?.AsArray();
      Assert.NotNull(definitions);
      Assert.Equal(3, definitions.Count);

      // Verify exact file names (2 from PartialTypes.cs, 1 from PartialTypes.Part2.cs)
      var fileNames = definitions.Select(d => Path.GetFileName(GetStringProperty(d, "FilePath"))).ToList();
      Assert.Equivalent(new[] { "PartialTypes.cs", "PartialTypes.cs", "PartialTypes.Part2.cs" }, fileNames);

      // Verify 3 distinct (file, line) pairs - each partial declaration is unique
      var locations = definitions.Select(d => (Path.GetFileName(GetStringProperty(d, "FilePath")), GetProperty<int>(d, "StartLine"))).ToList();
      Assert.Equal(3, locations.Distinct().Count());
   }

   [Fact]
   public async Task GoToDefinition_PartialClass_DefaultBehavior_ReturnsSingleDefinition()
   {
      // Default behavior (allLocations=false) should return only one location
      var result = await _tool.GoToDefinitionAsync(
         new LookupByName { Name = "PartialService" },
         cancellationToken: CT);
      AssertSuccess(result);
      var data = GetData(result);

      // When allLocations=false (default), Definitions should contain single item
      var definitions = GetNode(data, "Definitions")?.AsArray();
      Assert.NotNull(definitions);
      var definition = Assert.Single(definitions);
      var filePath = GetStringProperty(definition, "FilePath");
      Assert.True(filePath?.EndsWith("PartialTypes.cs") == true || filePath?.EndsWith("PartialTypes.Part2.cs") == true,
         $"Expected definition in PartialTypes files but got: {filePath}");
   }

   [Fact]
   public async Task GoToDefinition_NonPartialClass_AllLocations_ReturnsSingleItemArray()
   {
      // NonPartialService has only one declaration
      var result = await _tool.GoToDefinitionAsync(
         new LookupByName { Name = "NonPartialService" },
         allLocations: true,
         cancellationToken: CT);
      AssertSuccess(result);
      var data = GetData(result);

      // When allLocations=true but there's only one location, Definitions should have 1 item
      var definitions = GetNode(data, "Definitions")?.AsArray();
      Assert.NotNull(definitions);
      var singleDefinition = Assert.Single(definitions);
      Assert.EndsWith("PartialTypes.cs", GetStringProperty(singleDefinition, "FilePath"));
   }
}
