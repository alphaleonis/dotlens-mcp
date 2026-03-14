using DotLens.Infrastructure;
using DotLens.Tools.Search;
using Xunit;

namespace DotLens.Tests.Search;

[Collection("TestFixtures")]
public class GetAttributesToolTests : TestFixtureTestBase
{
   public GetAttributesToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetAttributesTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      _tool = new GetAttributesTool(Workspace);
   }

   #region Success Cases

   [Fact]
   public async Task GetAttributes_FindsAttributesOnClasses()
   {
      // ServiceAttribute is applied to UserService, DataService in CoreTestProject
      var result = await _tool.GetAttributesAsync("Service", cancellationToken: CT);

      AssertSuccess(result);
      var symbols = GetData(result).AsArray();
      Assert.NotNull(symbols);
      Assert.NotEmpty(symbols);

      // Verify we found expected types (using SymbolResponse property names)
      var symbolNames = symbols.Select(s => s?.GetStringOrNull("name")).ToList();
      Assert.Contains("UserService", symbolNames);
   }

   [Fact]
   public async Task GetAttributes_FindsAttributesOnMethods()
   {
      // LogAttribute is applied to methods in UserService
      var result = await _tool.GetAttributesAsync("Log", cancellationToken: CT);

      AssertSuccess(result);
      var symbols = GetData(result).AsArray();
      Assert.NotNull(symbols);
      Assert.NotEmpty(symbols);

      // Verify all results are methods (kind is the polymorphic discriminator)
      foreach (var symbol in symbols)
      {
         Assert.NotNull(symbol?["kind"]);
         Assert.Equal("method", symbol?.GetStringOrNull("kind"));
      }
   }

   [Fact]
   public async Task GetAttributes_ForUnknownAttribute_ReturnsEmptyList()
   {
      var result = await _tool.GetAttributesAsync("NonExistentAttribute12345", cancellationToken: CT);

      AssertSuccess(result);
      var symbols = GetData(result).AsArray();
      Assert.NotNull(symbols);
      Assert.Empty(symbols);
   }

   [Fact]
   public async Task GetAttributes_RespectsMaxResults()
   {
      // Validate attribute is applied to 3 properties in CoreTestProject/Patterns/Attributes.cs
      // With maxResults: 2, we should get exactly 2 results
      var result = await _tool.GetAttributesAsync("Validate", maxResults: 2, cancellationToken: CT);

      AssertSuccess(result);
      var symbols = GetData(result).AsArray();
      Assert.NotNull(symbols);
      Assert.Equal(2, symbols.Count);
   }

   [Fact]
   public async Task GetAttributes_IncludesSymbolLocation()
   {
      var result = await _tool.GetAttributesAsync("Service", maxResults: 5, cancellationToken: CT);

      AssertSuccess(result);
      var symbols = GetData(result).AsArray();
      Assert.NotNull(symbols);
      Assert.NotEmpty(symbols);

      var symbol = symbols[0];
      Assert.NotNull(symbol?["name"]);
      Assert.NotNull(symbol?["kind"]);
      Assert.NotNull(symbol?["location"]);

      var location = symbol?["location"];
      Assert.NotNull(location?["filePath"]);
      Assert.NotNull(location?["line"]);
      Assert.NotNull(location?["column"]);
   }

   [Fact]
   public async Task GetAttributes_WithProjectScope_RestrictsSearch()
   {
      var result = await _tool.GetAttributesAsync(
         "Service",
         scope: new ProjectScope { Name = "CoreTestProject" },
         cancellationToken: CT);

      AssertSuccess(result);
      var symbols = GetData(result).AsArray();
      Assert.NotNull(symbols);
      Assert.NotEmpty(symbols);

      // Verify all results are from CoreTestProject
      foreach (var symbol in symbols)
      {
         var location = symbol?["location"];
         Assert.NotNull(location);
         var filePath = location?.GetStringOrNull("filePath");
         Assert.NotNull(filePath);
         Assert.Contains("CoreTestProject", filePath);
      }
   }

   [Fact]
   public async Task GetAttributes_WithFileScope_RestrictsSearch()
   {
      var result = await _tool.GetAttributesAsync(
         "Service",
         scope: new FileScope { Path = "Attributes.cs" },
         cancellationToken: CT);

      AssertSuccess(result);
      var symbols = GetData(result).AsArray();
      Assert.NotNull(symbols);
      Assert.NotEmpty(symbols);

      // Verify all results are from the scoped file
      foreach (var symbol in symbols)
      {
         var location = symbol?["location"];
         Assert.NotNull(location);
         var filePath = location?.GetStringOrNull("filePath");
         Assert.NotNull(filePath);
         Assert.EndsWith("Attributes.cs", filePath);
      }
   }

   [Fact]
   public async Task GetAttributes_WithFullVerbosity_ReturnsFullyQualifiedName()
   {
      var result = await _tool.GetAttributesAsync(
         "Service",
         maxResults: 5,
         verbosity: ToolResultVerbosity.Full,
         cancellationToken: CT);

      AssertSuccess(result);
      var symbols = GetData(result).AsArray();
      Assert.NotNull(symbols);
      Assert.NotEmpty(symbols);

      var symbol = symbols[0];
      Assert.NotNull(symbol?["fullyQualifiedName"]);
      var fqn = symbol?.GetStringOrNull("fullyQualifiedName");
      Assert.NotNull(fqn);
      Assert.Contains(".", fqn); // FQN should include namespace
   }

   [Fact]
   public async Task GetAttributes_ReturnsContainingTypeForMethods()
   {
      // LogAttribute is on methods, which have a containing type
      var result = await _tool.GetAttributesAsync("Log", cancellationToken: CT);

      AssertSuccess(result);
      var symbols = GetData(result).AsArray();
      Assert.NotNull(symbols);
      Assert.NotEmpty(symbols);

      // Methods should have a containing type
      var methodSymbol = symbols.First(s => s?.GetStringOrNull("kind") == "method");
      Assert.NotNull(methodSymbol);
      Assert.NotNull(methodSymbol["containingType"]);
   }

   #endregion

   #region Error Cases

   [Fact]
   public async Task GetAttributes_WithEmptyAttributeName_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.GetAttributesAsync("", cancellationToken: CT));
   }

   [Fact]
   public async Task GetAttributes_WithWhitespaceAttributeName_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.GetAttributesAsync("   ", cancellationToken: CT));
   }

   [Fact]
   public async Task GetAttributes_WithNonExistentProject_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.GetAttributesAsync(
            "Service",
            scope: new ProjectScope { Name = "NonExistentProject" },
            cancellationToken: CT));
   }

   #endregion
}
