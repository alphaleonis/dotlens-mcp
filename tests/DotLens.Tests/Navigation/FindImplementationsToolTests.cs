using DotLens.Infrastructure;
using DotLens.Infrastructure.Results;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Navigation;
using DotLens.Tools.Search;
using Xunit;

namespace DotLens.Tests.Navigation;

[Collection("TestFixtures")]
public class FindImplementationsToolTests : TestFixtureTestBase
{
   public FindImplementationsToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private FindImplementationsTool _tool = null!;
   private FindSymbolsTool _findSymbols = null!;
   private SymbolLookupService _symbolLookup = null!;

   public override async ValueTask InitializeAsync()
   {
      _symbolLookup = new SymbolLookupService(Workspace);
      _tool = new FindImplementationsTool(Workspace, _symbolLookup);
      _findSymbols = new FindSymbolsTool(Workspace);
   }

   private LookupByPosition CreateLookup(string filePath, int line, int column) =>
      new() { FilePath = filePath, Line = line, Column = column };

   private async Task<ToolResult<FindImplementationsResponse>> FindImplementationsAsync(
      string relativePath, int line, int column, int maxResults = 50)
   {
      var filePath = GetAbsolutePath(relativePath);
      return await _tool.FindImplementationsAsync(
         CreateLookup(filePath, line, column),
         maxResults,
         cancellationToken: CT);
   }

   #region Success Cases

   [Fact]
   public async Task FindImplementations_ForInterface_ReturnsImplementers()
   {
      // Search for IBasicService interface
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "IBasicService", kinds: [SymbolKindFilter.Interface], maxResults: 5, cancellationToken: CT);
      var results = GetData(searchResult).GetArrayOrNull("items");
      Assert.NotNull(results);
      Assert.True(results.Count > 0, "Should find IBasicService interface");

      var symbol = results[0];
      var location = symbol?["location"];
      var relativePath = location?.GetStringOrNull("filePath");
      Assert.NotNull(relativePath);
      var line = location?.GetIntOrNull("line") ?? 0;
      var col = location?.GetIntOrNull("column") ?? 0;

      var result = await FindImplementationsAsync(relativePath, line, col);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.NotNull(data["baseType"]);
      Assert.NotNull(data["implementations"]);
      // IBasicService should have at least BasicServiceImpl as implementation
      Assert.True(data.GetIntOrNull("totalImplementations") >= 1);
   }

   [Fact]
   public async Task FindImplementations_ForAbstractClass_ReturnsImplementers()
   {
      // Search for AbstractBase class
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "AbstractBase", kinds: [SymbolKindFilter.Class], maxResults: 5, cancellationToken: CT);
      var results = GetData(searchResult).GetArrayOrNull("items");
      Assert.NotNull(results);
      Assert.True(results.Count > 0, "Should find AbstractBase class");

      var symbol = results[0];
      var location = symbol?["location"];
      var relativePath = location?.GetStringOrNull("filePath");
      Assert.NotNull(relativePath);
      var line = location?.GetIntOrNull("line") ?? 0;
      var col = location?.GetIntOrNull("column") ?? 0;

      var result = await FindImplementationsAsync(relativePath, line, col);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.NotNull(data["implementations"]);
      // AbstractBase has ConcreteClass, DerivedClass, SealedOverrideClass as implementations
      var implementations = data.GetArrayOrNull("implementations");
      Assert.NotNull(implementations);
   }

   [Fact]
   public async Task FindImplementations_ReturnsImplementationDetails()
   {
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "IBasicService", kinds: [SymbolKindFilter.Interface], maxResults: 5, cancellationToken: CT);
      var results = GetData(searchResult).GetArrayOrNull("items");
      Assert.NotNull(results);
      Assert.True(results.Count > 0, "Should find IBasicService interface");

      var symbol = results[0];
      var location = symbol?["location"];
      var relativePath = location?.GetStringOrNull("filePath");
      Assert.NotNull(relativePath);
      var line = location?.GetIntOrNull("line") ?? 0;
      var col = location?.GetIntOrNull("column") ?? 0;

      var result = await FindImplementationsAsync(relativePath, line, col);

      AssertSuccess(result);
      var data = GetData(result);
      var implementations = data.GetArrayOrNull("implementations");
      Assert.NotNull(implementations);

      Assert.True(implementations.Count > 0, "IBasicService should have at least one implementation");
      var impl = implementations[0];
      Assert.NotNull(impl?["name"]);
      Assert.NotNull(impl?["kind"]); // JSON discriminator
      Assert.NotNull(impl?["location"]);
   }

   [Fact]
   public async Task FindImplementations_RespectsMaxResults()
   {
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "AbstractBase", kinds: [SymbolKindFilter.Class], maxResults: 5, cancellationToken: CT);
      var results = GetData(searchResult).GetArrayOrNull("items");
      Assert.NotNull(results);
      Assert.True(results.Count > 0, "Should find AbstractBase class");

      var symbol = results[0];
      var location = symbol?["location"];
      var relativePath = location?.GetStringOrNull("filePath");
      Assert.NotNull(relativePath);
      var line = location?.GetIntOrNull("line") ?? 0;
      var col = location?.GetIntOrNull("column") ?? 0;

      var result = await FindImplementationsAsync(relativePath, line, col, maxResults: 2);

      AssertSuccess(result);
      var data = GetData(result);
      var implementations = data.GetArrayOrNull("implementations");
      Assert.NotNull(implementations);
      Assert.True(implementations.Count <= 2);
   }

   [Fact]
   public async Task FindImplementations_WithTypeHavingNoImplementations_ReturnsEmptyList()
   {
      // Search for SealedClass which has no derived implementations
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "SealedClass", kinds: [SymbolKindFilter.Class], maxResults: 5, cancellationToken: CT);
      var results = GetData(searchResult).GetArrayOrNull("items");
      Assert.NotNull(results);
      Assert.True(results.Count > 0, "Should find SealedClass");

      var symbol = results[0];
      var location = symbol?["location"];
      var relativePath = location?.GetStringOrNull("filePath");
      Assert.NotNull(relativePath);
      var line = location?.GetIntOrNull("line") ?? 0;
      var col = location?.GetIntOrNull("column") ?? 0;

      var result = await FindImplementationsAsync(relativePath, line, col);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.NotNull(data["implementations"]);
      // SealedClass is sealed and has no implementations
      Assert.Equal(0, data.GetIntOrNull("totalImplementations"));
   }

   [Fact]
   public async Task FindImplementations_ByName_ReturnsImplementers()
   {
      // Use name-based lookup
      var result = await _tool.FindImplementationsAsync(
         new LookupByName { Name = "IBasicService" },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.NotNull(data["baseType"]);
      Assert.True(data.GetIntOrNull("totalImplementations") >= 1);
   }

   [Fact]
   public async Task FindImplementations_WithSummaryVerbosity_ReturnsCountOnly()
   {
      var result = await _tool.FindImplementationsAsync(
         new LookupByName { Name = "IBasicService" },
         verbosity: ToolResultVerbosity.Summary,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.NotNull(data["baseType"]);
      Assert.True(data.GetIntOrNull("totalImplementations") >= 1);
      // Summary verbosity should not include the implementations list
      Assert.Null(data["implementations"]);
   }

   #endregion

   #region Error Cases

   [Fact]
   public async Task FindImplementations_WithInvalidFilePath_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.FindImplementationsAsync(
            CreateLookup("NonExistent/path/file.cs", 0, 0),
            cancellationToken: CT),
         "FILE_NOT_IN_SOLUTION");
   }

   [Fact]
   public async Task FindImplementations_WithInvalidLineNumber_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      await AssertThrowsToolExceptionAsync(
         () => _tool.FindImplementationsAsync(
            CreateLookup(filePath, 99999, 0),
            cancellationToken: CT),
         "INVALID_PARAMETER");
   }

   [Fact]
   public async Task FindImplementations_WithInvalidColumnNumber_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      await AssertThrowsToolExceptionAsync(
         () => _tool.FindImplementationsAsync(
            CreateLookup(filePath, 1, 99999),
            cancellationToken: CT),
         "INVALID_PARAMETER");
   }

   [Fact]
   public async Task FindImplementations_WithNonTypeSymbol_ThrowsToolException()
   {
      // Search for a method instead of a type
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "Calculate", kinds: [SymbolKindFilter.Method], maxResults: 5, cancellationToken: CT);
      var results = GetData(searchResult).GetArrayOrNull("items");
      Assert.NotNull(results);
      Assert.True(results.Count > 0, "Should find Calculate method");

      var symbol = results[0];
      var location = symbol?["location"];
      var relativePath = location?.GetStringOrNull("filePath");
      Assert.NotNull(relativePath);
      var line = location?.GetIntOrNull("line") ?? 0;
      var col = location?.GetIntOrNull("column") ?? 0;

      // SymbolLookupService filters by type, so finding a method when looking for INamedTypeSymbol
      // results in SYMBOL_NOT_FOUND rather than TYPE_MISMATCH
      await AssertThrowsToolExceptionAsync(
         () => FindImplementationsAsync(relativePath, line, col),
         "SYMBOL_NOT_FOUND");
   }

   #endregion
}
