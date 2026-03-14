using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Intelligence;
using Xunit;

namespace DotLens.Tests.Intelligence;

[Collection("TestFixtures")]
public class GetInstantiationOptionsToolTests : TestFixtureTestBase
{
   public GetInstantiationOptionsToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetInstantiationOptionsTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      var symbolLookup = new SymbolLookupService(Workspace);
      _tool = new GetInstantiationOptionsTool(symbolLookup);
   }

   private static LookupByName ByName(string name) => new() { Name = name };

   [Fact]
   public async Task GetInstantiationOptions_ReturnsConstructorInfo()
   {
      var result = await _tool.GetInstantiationOptionsAsync(ByName("SampleClass"), cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Contains("SampleClass", data.GetStringOrNull("typeName"));
      Assert.NotNull(data["constructors"]);
      var constructors = data["constructors"]?.AsArray();
      Assert.NotNull(constructors);
      // SampleClass has 3 constructors: (), (int id), (int id, string name)
      Assert.Equal(3, constructors!.Count);
   }

   [Fact]
   public async Task GetInstantiationOptions_WithSingleConstructor_ReturnsOne()
   {
      var result = await _tool.GetInstantiationOptionsAsync(ByName("SealedClass"), cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var constructors = data["constructors"]?.AsArray();
      Assert.NotNull(constructors);
      // SealedClass has exactly one constructor: (string data)
      Assert.Single(constructors!);
   }

   [Fact]
   public async Task GetInstantiationOptions_WithAbstractClass_ReturnsInfo()
   {
      var result = await _tool.GetInstantiationOptionsAsync(ByName("AbstractBase"), cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Contains("AbstractBase", data.GetStringOrNull("typeName"));
      Assert.True(data.GetBoolOrNull("isAbstract") == true);
   }

   [Fact]
   public async Task GetInstantiationOptions_SummaryVerbosity_ReturnsCountsOnly()
   {
      var result = await _tool.GetInstantiationOptionsAsync(
         ByName("SampleClass"),
         verbosity: ToolResultVerbosity.Summary,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Contains("SampleClass", data.GetStringOrNull("typeName"));
      // Should have counts with exact values
      // SampleClass has 3 constructors and 1 factory method (Create)
      Assert.Equal(3, data.GetIntOrNull("constructorCount"));
      Assert.Equal(1, data.GetIntOrNull("factoryMethodCount"));
      // Lists should be null at summary verbosity
      Assert.Null(data["constructors"]);
      Assert.Null(data["factoryMethods"]);
      Assert.Null(data["hints"]);
   }

   [Fact]
   public async Task GetInstantiationOptions_TypeNotFound_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.GetInstantiationOptionsAsync(ByName("NonExistentType"), cancellationToken: CT),
         "SYMBOL_NOT_FOUND");
   }

   [Fact]
   public async Task GetInstantiationOptions_ByPosition_Works()
   {
      // Get location of SampleClass using the file path
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");

      // SampleClass is defined on line 12 in Classes.cs
      var lookup = new LookupByPosition { FilePath = filePath, Line = 12, Column = 14 };
      var result = await _tool.GetInstantiationOptionsAsync(lookup, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Contains("SampleClass", data.GetStringOrNull("typeName"));
   }
}
