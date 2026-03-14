using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Search;
using Xunit;

namespace DotLens.Tests.Search;

[Collection("TestFixtures")]
public class GetDerivedTypesToolTests : TestFixtureTestBase
{
   public GetDerivedTypesToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetDerivedTypesTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      var symbolLookup = new SymbolLookupService(Workspace);
      _tool = new GetDerivedTypesTool(Workspace, symbolLookup);
   }

   #region Success Cases

   [Fact]
   public async Task GetDerivedTypes_FindsSubclasses()
   {
      // ConcreteClass is a class that exists in CoreTestProject
      var result = await _tool.GetDerivedTypesAsync(
         new LookupByName { Name = "ConcreteClass" },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Contains("ConcreteClass", data.GetStringOrNull("baseType"));
      Assert.Equal("class", data.GetStringOrNull("baseTypeKind"));
   }

   [Fact]
   public async Task GetDerivedTypes_ForBaseClass_FindsDerivedTypes()
   {
      // AbstractBase has ConcreteClass, DerivedClass, SealedOverrideClass as derived types
      var result = await _tool.GetDerivedTypesAsync(
         new LookupByName { Name = "AbstractBase" },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var derivedTypes = data["derivedTypes"]?.AsArray();
      Assert.NotNull(derivedTypes);
      Assert.NotEmpty(derivedTypes!);

      // Verify derived types include expected names
      var names = derivedTypes.Select(t => t.GetStringOrNull("name")).ToList();
      Assert.Contains("ConcreteClass", names); // Known direct subclass of AbstractBase

      // Verify response structure (using SymbolResponse)
      var first = derivedTypes[0];
      Assert.NotNull(first);
      Assert.NotNull(first.GetStringOrNull("kind")); // SymbolResponse has "kind" discriminator
   }

   [Fact]
   public async Task GetDerivedTypes_ForInterface_FindsImplementations()
   {
      // IBasicService is implemented by BasicServiceImpl
      var result = await _tool.GetDerivedTypesAsync(
         new LookupByName { Name = "IBasicService" },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("interface", data.GetStringOrNull("baseTypeKind"));
      var derivedTypes = data["derivedTypes"]?.AsArray();
      Assert.NotNull(derivedTypes);
      // BasicServiceImpl implements IBasicService
      Assert.NotEmpty(derivedTypes!);
   }

   [Fact]
   public async Task GetDerivedTypes_ForTypeWithNoDerived_ReturnsEmptyList()
   {
      // SealedClass has no derived types
      var result = await _tool.GetDerivedTypesAsync(
         new LookupByName { Name = "SealedClass" },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal(0, data.GetIntOrNull("totalDerived"));
      var derivedTypes = data["derivedTypes"]?.AsArray();
      Assert.NotNull(derivedTypes);
      Assert.Empty(derivedTypes!);
   }

   [Fact]
   public async Task GetDerivedTypes_RespectsMaxResults()
   {
      // AbstractBase has multiple derived types
      var result = await _tool.GetDerivedTypesAsync(
         new LookupByName { Name = "AbstractBase" },
         maxResults: 2,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var derivedTypes = data["derivedTypes"]?.AsArray();
      Assert.NotNull(derivedTypes);
      Assert.True(derivedTypes!.Count <= 2);
   }

   [Fact]
   public async Task GetDerivedTypes_IncludesTypeLocation()
   {
      var result = await _tool.GetDerivedTypesAsync(
         new LookupByName { Name = "AbstractBase" },
         verbosity: ToolResultVerbosity.Full,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var derivedTypes = data["derivedTypes"]?.AsArray();
      Assert.NotNull(derivedTypes);
      Assert.NotEmpty(derivedTypes!);

      var firstDerived = derivedTypes[0];
      Assert.NotNull(firstDerived);
      // SymbolResponse includes location at Compact and Full verbosity
      var location = firstDerived["location"];
      Assert.NotNull(location);
      Assert.NotNull(location.GetStringOrNull("filePath"));
      Assert.True(location.GetIntOrNull("line") > 0);
   }

   [Fact]
   public async Task GetDerivedTypes_SummaryVerbosity_ReturnsCountOnly()
   {
      var result = await _tool.GetDerivedTypesAsync(
         new LookupByName { Name = "AbstractBase" },
         verbosity: ToolResultVerbosity.Summary,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.True(data.GetIntOrNull("totalDerived") >= 1);
      // At summary verbosity, derivedTypes should be null
      Assert.Null(data["derivedTypes"]);
   }

   [Fact]
   public async Task GetDerivedTypes_DirectOnly_ExcludesTransitiveDescendants()
   {
      // Use a type that has both direct and transitive descendants
      var transitiveResult = await _tool.GetDerivedTypesAsync(
         new LookupByName { Name = "AbstractBase" },
         includeTransitive: true,
         cancellationToken: CT);

      var directResult = await _tool.GetDerivedTypesAsync(
         new LookupByName { Name = "AbstractBase" },
         includeTransitive: false,
         cancellationToken: CT);

      AssertSuccess(transitiveResult);
      AssertSuccess(directResult);

      var transitiveData = GetData(transitiveResult);
      var directData = GetData(directResult);

      // Transitive count should be > direct count for AbstractBase which has grandchildren
      var transitiveCount = transitiveData.GetIntOrNull("totalDerived") ?? 0;
      var directCount = directData.GetIntOrNull("totalDerived") ?? 0;
      Assert.True(transitiveCount > directCount,
         $"Transitive count ({transitiveCount}) should be > direct count ({directCount}) for AbstractBase which has grandchildren");
   }

   #endregion

   #region Error Cases

   [Fact]
   public async Task GetDerivedTypes_WithUnknownType_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.GetDerivedTypesAsync(
            new LookupByName { Name = "NonExistentClassName12345" },
            cancellationToken: CT),
         ErrorCodes.SymbolNotFound);
   }

   [Fact]
   public async Task GetDerivedTypes_WithEmptyTypeName_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.GetDerivedTypesAsync(
            new LookupByName { Name = "" },
            cancellationToken: CT),
         ErrorCodes.InvalidParameter);
   }

   #endregion
}
