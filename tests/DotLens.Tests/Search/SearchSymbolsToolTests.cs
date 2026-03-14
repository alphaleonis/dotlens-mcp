using DotLens.Infrastructure;
using DotLens.Tools.Search;
using DotLens.Tools.Shared;
using Xunit;

namespace DotLens.Tests.Search;

[Collection("TestFixtures")]
public class SearchSymbolsToolTests : TestFixtureTestBase
{
   public SearchSymbolsToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private SearchSymbolsTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      _tool = new SearchSymbolsTool(Workspace);
   }

   [Fact]
   public async Task SearchSymbols_FindsTypes()
   {
      // Search for SampleClass from CoreTestProject
      var result = await _tool.SearchSymbolsAsync("SampleClass", kind: null, maxResults: 10, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.True(items!.Count > 0, "Should find SampleClass");

      var hasMatch = items.Any(r =>
          r.GetStringOrNull("name")?.Contains("SampleClass") == true ||
          r.GetStringOrNull("fullyQualifiedName")?.Contains("SampleClass") == true);
      Assert.True(hasMatch, "Search should find symbols containing 'SampleClass'");
   }

   [Fact]
   public async Task SearchSymbols_WithKindFilter_FiltersResults()
   {
      // Search for Get* methods in test fixtures
      var result = await _tool.SearchSymbolsAsync("Get*", kind: SymbolKindFilter.Method, maxResults: 50, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);

      foreach (var symbol in items!)
      {
         Assert.Equal("method", symbol.GetStringOrNull("kind"));
      }
   }

   [Fact]
   public async Task SearchSymbols_WithWildcard_FindsMultiple()
   {
      // Search for *Async methods - CoreTestProject has several async methods
      var result = await _tool.SearchSymbolsAsync("*Async", kind: SymbolKindFilter.Method, maxResults: 100, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      // Test fixtures contain multiple *Async methods
      Assert.True(items!.Count > 0, "Should find async methods in test fixtures");
   }

   [Fact]
   public async Task SearchSymbols_FindsInterface()
   {
      // Search for IBasicService interface from CoreTestProject
      var result = await _tool.SearchSymbolsAsync("IBasicService", kind: SymbolKindFilter.Interface, maxResults: 10, verbosity: ToolResultVerbosity.Full, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      // There's exactly one IBasicService interface
      Assert.Single(items!);

      var iface = items[0]!;
      Assert.Equal("IBasicService", iface.GetStringOrNull("name"));
      Assert.Contains("CoreTestProject.Types", iface.GetStringOrNull("fullyQualifiedName"));
   }

   [Fact]
   public async Task SearchSymbols_FindsEnum()
   {
      // Search for Status enum from CoreTestProject (without kind filter to find it)
      var result = await _tool.SearchSymbolsAsync("Status", kind: null, maxResults: 10, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);

      // Find an enum named Status - verify it's in the results
      var hasStatusEnum = items.Any(r =>
         r.GetStringOrNull("name") == "Status");
      Assert.True(hasStatusEnum, "Should find Status enum");
   }

   [Fact]
   public async Task SearchSymbols_FindsRecord()
   {
      // Search for Person record from CoreTestProject
      var result = await _tool.SearchSymbolsAsync("Person", kind: null, maxResults: 10, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);

      var hasPerson = items.Any(r =>
         r.GetStringOrNull("name") == "Person");
      Assert.True(hasPerson, "Should find Person record");
   }

   [Fact]
   public async Task SearchSymbols_FindsGenericType()
   {
      // Search for Container<T> generic class from CoreTestProject
      var result = await _tool.SearchSymbolsAsync("Container", kind: SymbolKindFilter.Class, maxResults: 10, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);

      var hasContainer = items.Any(r =>
         r.GetStringOrNull("name")?.Contains("Container") == true);
      Assert.True(hasContainer, "Should find Container generic class");
   }

   [Fact]
   public async Task SearchSymbols_ReturnsPolymorphicResponse()
   {
      // Verify that the response includes kind discriminator
      var result = await _tool.SearchSymbolsAsync("SampleClass", kind: SymbolKindFilter.Class, maxResults: 5, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      // There's exactly one SampleClass in CoreTestProject.Types
      Assert.Single(items!);

      // Check that type responses include kind discriminator
      var typeSymbol = items[0]!;
      Assert.Equal("class", typeSymbol.GetStringOrNull("kind"));
   }

   [Fact]
   public async Task SearchSymbols_ModifierFilter_Static_FiltersResults()
   {
      var result = await _tool.SearchSymbolsAsync(
         "Create",
         kind: SymbolKindFilter.Method,
         modifiers: [ModifierFilter.Static],
         maxResults: 50,
         verbosity: ToolResultVerbosity.Full,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArray("items");
      Assert.NotEmpty(items);

      // All returned methods should be static
      foreach (var item in items)
      {
         var declaration = item.GetString("declaration");
         Assert.Contains("static", declaration);
      }
   }

   [Fact]
   public async Task SearchSymbols_ModifierFilter_Record_FindsRecordTypes()
   {
      var result = await _tool.SearchSymbolsAsync(
         "Person",
         modifiers: [ModifierFilter.Record],
         maxResults: 10,
         verbosity: ToolResultVerbosity.Full,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArray("items");
      Assert.NotEmpty(items);

      // Person is a record - should be found
      var names = items.Select(m => m.GetString("name")).ToList();
      Assert.Contains("Person", names);
   }

   [Fact]
   public async Task SearchSymbols_MethodResponseIncludesSignature()
   {
      // Search for the DoWorkAsync method which has a unique signature in AsyncService
      var result = await _tool.SearchSymbolsAsync(
         "DoWorkAsync",
         kind: SymbolKindFilter.Method,
         maxResults: 1,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArray("items");
      Assert.Single(items);

      var methodSymbol = items[0];
      Assert.Equal("method", methodSymbol.GetString("kind"));
      Assert.Equal("Task AsyncService.DoWorkAsync()", methodSymbol.GetString("signature"));
      Assert.Equal("System.Threading.Tasks.Task", methodSymbol.GetString("returnType"));
   }
}

