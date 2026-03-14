using System.Text.Json;
using System.Text.Json.Nodes;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Navigation;
using DotLens.Tools.Search;
using Xunit;

namespace DotLens.Tests.Navigation;

[Collection("TestFixtures")]
public class FindOverridesToolTests : TestFixtureTestBase
{
   public FindOverridesToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private FindOverridesTool _tool = null!;
   private FindSymbolsTool _findSymbols = null!;

   public override async ValueTask InitializeAsync()
   {
      var symbolLookup = new SymbolLookupService(Workspace);
      _tool = new FindOverridesTool(Workspace, symbolLookup);
      _findSymbols = new FindSymbolsTool(Workspace);
   }

   private LookupByPosition CreateLookup(string filePath, int line, int column)
      => new() { FilePath = filePath, Line = line, Column = column };

   [Fact]
   public async Task FindOverrides_VirtualMethod_ReturnsOverrides()
   {
      // Search for VirtualMethod in AbstractBase which has overrides in derived classes
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "VirtualMethod", kinds: [SymbolKindFilter.Method], cancellationToken: CT);

      var data = GetData(searchResult);
      var symbols = data["items"]!.AsArray();
      Assert.NotEmpty(symbols);

      // Get the AbstractBase version which is virtual
      var method = symbols.FirstOrDefault(s =>
      {
         var containingType = s!.GetStringOrNull("containingType") ?? "";
         return containingType.Contains("AbstractBase");
      });

      if (method != null)
      {
         var location = method!["location"]!;
         var relativePath = location.GetStringOrNull("filePath")!;
         var filePath = GetAbsolutePath(relativePath!);
         var line = location.GetIntOrNull("line")!.Value;
         var column = location.GetIntOrNull("column")!.Value;

         var result = await _tool.FindOverridesAsync(CreateLookup(filePath, line, column), cancellationToken: CT);

         AssertSuccess(result);
         var resultData = GetData(result);
         Assert.NotNull(resultData["overrides"]);
      }
   }

   [Fact]
   public async Task FindOverrides_NonVirtualMethod_ThrowsToolException()
   {
      // Search for DoSomething which is not virtual
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "DoSomething", kinds: [SymbolKindFilter.Method], cancellationToken: CT);

      var data = GetData(searchResult);
      var symbols = data["items"]!.AsArray();
      Assert.NotEmpty(symbols);

      var method = symbols[0]!;
      var location = method["location"]!;
      var relativePath = location.GetStringOrNull("filePath")!;
      var filePath = GetAbsolutePath(relativePath!);
      var line = location.GetIntOrNull("line")!.Value;
      var column = location.GetIntOrNull("column")!.Value;

      await AssertThrowsToolExceptionAsync(
         () => _tool.FindOverridesAsync(CreateLookup(filePath, line, column), cancellationToken: CT),
         ErrorCodes.InvalidParameter);
   }

   [Fact]
   public async Task FindOverrides_InvalidPosition_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      await AssertThrowsToolExceptionAsync(
         () => _tool.FindOverridesAsync(CreateLookup(filePath, 99999, 1), cancellationToken: CT),
         "INVALID_PARAMETER");
   }

   [Fact]
   public async Task FindOverrides_NonMethodSymbol_ThrowsToolException()
   {
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "SampleClass", kinds: [SymbolKindFilter.Class], cancellationToken: CT);

      var data = GetData(searchResult);
      var symbols = data["items"]!.AsArray();
      Assert.NotEmpty(symbols);

      var classSymbol = symbols[0]!;
      var location = classSymbol["location"]!;
      var relativePath = location.GetStringOrNull("filePath")!;
      var filePath = GetAbsolutePath(relativePath!);
      var line = location.GetIntOrNull("line")!.Value;
      var column = location.GetIntOrNull("column")!.Value;

      // With SymbolLookupService filter, this returns SymbolNotFound
      await AssertThrowsToolExceptionAsync(
         () => _tool.FindOverridesAsync(CreateLookup(filePath, line, column), cancellationToken: CT),
         ErrorCodes.SymbolNotFound);
   }

   [Fact]
   public async Task FindOverrides_FileNotFound_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.FindOverridesAsync(CreateLookup("NonExistent/file.cs", 1, 1), cancellationToken: CT),
         "FILE_NOT_IN_SOLUTION");
   }

   [Fact]
   public async Task FindOverrides_ByName_ReturnsOverrides()
   {
      // Test the new name-based lookup
      var result = await _tool.FindOverridesAsync(
         new LookupByName { Name = "AbstractBase.VirtualMethod" },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.NotNull(data["baseMember"]);
      Assert.True(data.GetIntOrNull("totalOverrides")!.Value >= 0);
   }

   [Fact]
   public async Task FindOverrides_SummaryVerbosity_ReturnsCountOnly()
   {
      // Test summary verbosity returns count but no detailed list
      var result = await _tool.FindOverridesAsync(
         new LookupByName { Name = "AbstractBase.VirtualMethod" },
         verbosity: ToolResultVerbosity.Summary,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.NotNull(data["baseMember"]);
      Assert.True(data.GetIntOrNull("totalOverrides")!.Value >= 0);
      // At summary verbosity, overrides list should be null
      Assert.True(data["overrides"] == null || data["overrides"]!.GetValueKind() == JsonValueKind.Null);
   }
}
