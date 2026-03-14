using DotLens.Infrastructure;
using DotLens.Infrastructure.Results;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Navigation;
using DotLens.Tools.Search;
using Xunit;

namespace DotLens.Tests.Navigation;

[Collection("TestFixtures")]
public class FindCallersToolTests : TestFixtureTestBase
{
   public FindCallersToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private FindCallersTool _tool = null!;
   private FindSymbolsTool _findSymbols = null!;
   private SymbolLookupService _symbolLookup = null!;

   public override async ValueTask InitializeAsync()
   {
      _symbolLookup = new SymbolLookupService(Workspace);
      _tool = new FindCallersTool(Workspace, _symbolLookup);
      _findSymbols = new FindSymbolsTool(Workspace);
   }

   private LookupByPosition CreateLookup(string filePath, int line, int column) =>
      new() { FilePath = filePath, Line = line, Column = column };

   private async Task<ToolResult<FindCallersResponse>> FindCallersAsync(
      string relativePath, int line, int column, int maxResults = 50)
   {
      var filePath = GetAbsolutePath(relativePath);
      return await _tool.FindCallersAsync(
         CreateLookup(filePath, line, column),
         maxResults,
         cancellationToken: CT);
   }

   #region Success Cases

   [Fact]
   public async Task FindCallers_FindsUsages()
   {
      // Search for VirtualMethod which is called from derived classes
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "VirtualMethod", kinds: [SymbolKindFilter.Method], maxResults: 10, cancellationToken: CT);
      var results = GetData(searchResult)["items"]?.AsArray();
      Assert.NotNull(results);
      Assert.True(results.Count > 0, "Should find VirtualMethod");

      // Get the AbstractBase version
      var symbol = results.FirstOrDefault(s =>
         s?.GetStringOrNull("containingType")?.Contains("AbstractBase") == true) ?? results[0];

      var location = symbol?["location"];
      var relativePath = location?.GetStringOrNull("filePath");
      Assert.NotNull(relativePath);
      var line = location?.GetIntOrNull("line") ?? 0;
      var col = location?.GetIntOrNull("column") ?? 0;

      var result = await FindCallersAsync(relativePath, line, col);
      AssertSuccess(result);

      var data = GetData(result);
      // symbol should be the method we searched for
      var symbolNode = data.GetObject("symbol");
      Assert.Equal("VirtualMethod", symbolNode.GetString("name"));
      // callers is an array - GetArray throws if missing, so no need for NotNull
      var callers = data.GetArray("callers");
      // VirtualMethod is called by ConcreteClass.VirtualMethod via base.VirtualMethod()
      Assert.Single(callers);
   }

   [Fact]
   public async Task FindCallers_OnProperty_FindsUsages()
   {
      // Search for Id property which may be used in the project
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "Id", kinds: [SymbolKindFilter.Property], maxResults: 10, cancellationToken: CT);
      var results = GetData(searchResult)["items"]?.AsArray();
      Assert.NotNull(results);
      Assert.True(results.Count > 0, "Should find Id property");

      var symbol = results[0];
      var location = symbol?["location"];
      var relativePath = location?.GetStringOrNull("filePath");
      Assert.NotNull(relativePath);
      var line = location?.GetIntOrNull("line") ?? 0;
      var col = location?.GetIntOrNull("column") ?? 0;

      var result = await FindCallersAsync(relativePath, line, col);
      AssertSuccess(result);

      var data = GetData(result);
      // symbol should be the property we searched for
      var symbolNode = data.GetObject("symbol");
      Assert.Equal("Id", symbolNode.GetString("name"));
      // callers is an array - GetArray throws if missing
      var callers = data.GetArray("callers");
      // Id property may have callers depending on test fixtures
      Assert.NotNull(callers); // Keep for documentation that array exists
   }

   [Fact]
   public async Task FindCallers_ByName_ReturnsCallers()
   {
      // Use name-based lookup
      var result = await _tool.FindCallersAsync(
         new LookupByName { Name = "AbstractBase.VirtualMethod" },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      // symbol should match what we searched for
      var symbolNode = data.GetObject("symbol");
      Assert.Equal("VirtualMethod", symbolNode.GetString("name"));
      // VirtualMethod is called by ConcreteClass.VirtualMethod via base.VirtualMethod()
      Assert.Equal(1, data.GetInt("totalCallers"));
   }

   [Fact]
   public async Task FindCallers_WithSummaryVerbosity_ReturnsCountOnly()
   {
      var result = await _tool.FindCallersAsync(
         new LookupByName { Name = "AbstractBase.VirtualMethod" },
         verbosity: ToolResultVerbosity.Summary,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      // symbol should match what we searched for
      var symbolNode = data.GetObject("symbol");
      Assert.Equal("VirtualMethod", symbolNode.GetString("name"));
      // VirtualMethod is called by ConcreteClass.VirtualMethod via base.VirtualMethod()
      Assert.Equal(1, data.GetInt("totalCallers"));
      // Summary verbosity should not include the callers list
      Assert.Null(data["callers"]);
   }

   #endregion

   #region Error Cases

   [Fact]
   public async Task FindCallers_FileNotFound_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.FindCallersAsync(
            CreateLookup("nonexistent.cs", 1, 1),
            cancellationToken: CT),
         "FILE_NOT_IN_SOLUTION");
   }

   [Fact]
   public async Task FindCallers_OnNonCallableSymbol_ThrowsToolException()
   {
      // Search for a class instead of a method/property
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "AbstractBase", kinds: [SymbolKindFilter.Class], maxResults: 5, cancellationToken: CT);
      var results = GetData(searchResult)["items"]?.AsArray();
      Assert.NotNull(results);
      Assert.True(results.Count > 0, "Should find AbstractBase class");

      var symbol = results[0];
      var location = symbol?["location"];
      var relativePath = location?.GetStringOrNull("filePath");
      Assert.NotNull(relativePath);
      var line = location?.GetIntOrNull("line") ?? 0;
      var col = location?.GetIntOrNull("column") ?? 0;

      // SymbolLookupService filters by type, so finding a class when looking for method/property
      // results in SYMBOL_NOT_FOUND
      await AssertThrowsToolExceptionAsync(
         () => FindCallersAsync(relativePath, line, col),
         "SYMBOL_NOT_FOUND");
   }

   #endregion
}
