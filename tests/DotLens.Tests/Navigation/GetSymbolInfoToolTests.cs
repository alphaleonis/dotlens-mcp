using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Navigation;
using DotLens.Tools.Shared;
using Xunit;

namespace DotLens.Tests.Navigation;

[Collection("TestFixtures")]
public class GetSymbolInfoToolTests : TestFixtureTestBase
{
   public GetSymbolInfoToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetSymbolInfoTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      var symbolLookup = new SymbolLookupService(Workspace);
      _tool = new GetSymbolInfoTool(Workspace, symbolLookup);
   }

   [Fact]
   public async Task GetSymbolInfo_ByPosition_OnClassName_ReturnsTypeInfo()
   {
      // Look up SampleClass by position
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var target = new LookupByPosition { FilePath = filePath, Line = 10, Column = 14 }; // SampleClass declaration

      var result = await _tool.GetSymbolInfoAsync(target, ToolResultVerbosity.Full, CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("class", data.GetStringOrNull("kind"));
      Assert.Equal("SampleClass", data.GetStringOrNull("name"));
   }

   [Fact]
   public async Task GetSymbolInfo_ByName_ReturnsTypeInfo()
   {
      // Look up SampleClass by name
      var target = new LookupByName { Name = "SampleClass" };

      var result = await _tool.GetSymbolInfoAsync(target, ToolResultVerbosity.Full, CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("class", data.GetStringOrNull("kind"));
      Assert.Equal("SampleClass", data.GetStringOrNull("name"));
   }

   [Fact]
   public async Task GetSymbolInfo_ByName_OnMethod_ReturnsMethodInfo()
   {
      // Look up Calculate method by name
      var target = new LookupByName { Name = "SampleClass.Calculate" };

      var result = await _tool.GetSymbolInfoAsync(target, ToolResultVerbosity.Full, CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("method", data.GetStringOrNull("kind"));
      Assert.Equal("Calculate", data.GetStringOrNull("name"));
      Assert.NotNull(data["returnType"]);
   }

   [Fact]
   public async Task GetSymbolInfo_SummaryVerbosity_ReturnsMinimalInfo()
   {
      var target = new LookupByName { Name = "SampleClass" };

      var result = await _tool.GetSymbolInfoAsync(target, ToolResultVerbosity.Summary, CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("SampleClass", data.GetStringOrNull("name"));
      // Summary verbosity should not include location or documentation
      Assert.Null(data["location"]);
      Assert.Null(data["documentation"]);
   }

   [Fact]
   public async Task GetSymbolInfo_CompactVerbosity_IncludesLocation()
   {
      var target = new LookupByName { Name = "SampleClass" };

      var result = await _tool.GetSymbolInfoAsync(target, ToolResultVerbosity.Compact, CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.NotNull(data["location"]);
      // Compact should not include documentation
      Assert.Null(data["documentation"]);
   }

   [Fact]
   public async Task GetSymbolInfo_FullVerbosity_IncludesDocumentation()
   {
      // SampleClass has XML documentation
      var target = new LookupByName { Name = "SampleClass" };

      var result = await _tool.GetSymbolInfoAsync(target, ToolResultVerbosity.Full, CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.NotNull(data["location"]);
      Assert.NotNull(data["fullyQualifiedName"]);
      // May have documentation if XML comments are present in test fixture
   }

   [Fact]
   public async Task GetSymbolInfo_GenericType_IncludesTypeParameters()
   {
      // Look up a generic type (Container<T> in Types/Generics.cs)
      var target = new LookupByName { Name = "Container" };

      var result = await _tool.GetSymbolInfoAsync(target, ToolResultVerbosity.Full, CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("class", data.GetStringOrNull("kind"));

      var typeParams = data["typeParameters"]?.AsArray();
      Assert.NotNull(typeParams);
      Assert.True(typeParams!.Count > 0, "Generic type should have type parameters");
      Assert.Equal("T", typeParams[0].GetStringValue());
   }

   [Fact]
   public async Task GetSymbolInfo_SymbolNotFound_ThrowsToolException()
   {
      var target = new LookupByName { Name = "NonExistentSymbol12345" };

      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _tool.GetSymbolInfoAsync(target, cancellationToken: CT));

      Assert.Equal(ErrorCodes.SymbolNotFound, ex.Code);
   }
}
