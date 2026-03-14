using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Intelligence;
using Xunit;

namespace DotLens.Tests.Intelligence;

[Collection("TestFixtures")]
public class GetTypeOverviewToolTests : TestFixtureTestBase
{
   public GetTypeOverviewToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetTypeOverviewTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      var symbolLookup = new SymbolLookupService(Workspace);
      _tool = new GetTypeOverviewTool(Workspace, symbolLookup);
   }

   private static LookupByName ByName(string name) => new() { Name = name };

   private static LookupByPosition ByPosition(string filePath, int line, int column = 1)
      => new() { FilePath = filePath, Line = line, Column = column };

   [Fact]
   public async Task GetTypeOverview_ReturnsComprehensiveInfo()
   {
      var result = await _tool.GetTypeOverviewAsync(ByName("SampleClass"), cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("SampleClass", data.GetStringOrNull("name"));
      Assert.Equal("Class", data.GetStringOrNull("typeKind"));

      // memberCounts is a required field
      var memberCounts = data.GetObject("memberCounts");
      // SampleClass has exactly: 8 methods, 4 properties, 5 fields
      Assert.Equal(8, memberCounts.GetInt("methods"));
      Assert.Equal(4, memberCounts.GetInt("properties"));
      Assert.Equal(5, memberCounts.GetInt("fields"));
   }

   [Fact]
   public async Task GetTypeOverview_WithInterface_ReturnsInterfaceKind()
   {
      var result = await _tool.GetTypeOverviewAsync(ByName("IBasicService"), cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("IBasicService", data.GetStringOrNull("name"));
      Assert.Equal("Interface", data.GetStringOrNull("typeKind"));
   }

   [Fact]
   public async Task GetTypeOverview_WithAbstractClass_ReturnsAbstractInfo()
   {
      var result = await _tool.GetTypeOverviewAsync(ByName("AbstractBase"), cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("AbstractBase", data.GetStringOrNull("name"));
      Assert.Equal("Class", data.GetStringOrNull("typeKind"));
   }

   [Fact]
   public async Task GetTypeOverview_WithEnum_ReturnsEnumKind()
   {
      var result = await _tool.GetTypeOverviewAsync(ByName("Status"), cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("Status", data.GetStringOrNull("name"));
      Assert.Equal("Enum", data.GetStringOrNull("typeKind"));
   }

   [Fact]
   public async Task GetTypeOverview_ByPosition_FindsType()
   {
      // SampleClass is at line 7 in Classes.cs
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");

      var result = await _tool.GetTypeOverviewAsync(ByPosition(filePath, 7, 14), cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("SampleClass", data.GetStringOrNull("name"));
   }

   [Fact]
   public async Task GetTypeOverview_SummaryVerbosity_ExcludesBaseTypesAndInterfaces()
   {
      var result = await _tool.GetTypeOverviewAsync(
         ByName("SampleClass"),
         verbosity: ToolResultVerbosity.Summary,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("SampleClass", data.GetStringOrNull("name"));
      // Summary still includes member counts - SampleClass has 8 methods
      var memberCounts = data.GetObject("memberCounts");
      Assert.Equal(8, memberCounts.GetInt("methods"));
      // But excludes base types and interfaces at Summary verbosity
      Assert.Null(data["baseTypes"]);
      Assert.Null(data["interfaces"]);
   }

   [Fact]
   public async Task GetTypeOverview_CompactVerbosity_IncludesBaseTypesAndInterfaces()
   {
      var result = await _tool.GetTypeOverviewAsync(
         ByName("SampleClass"),
         verbosity: ToolResultVerbosity.Compact,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      // Compact includes base types and interfaces (required arrays)
      var baseTypes = data.GetArray("baseTypes");
      // SampleClass has no explicit base type (System.Object is excluded from output)
      Assert.Empty(baseTypes);

      var interfaces = data.GetArray("interfaces");
      // SampleClass doesn't implement any interfaces
      Assert.Empty(interfaces);

      // But Compact excludes documentation
      Assert.Null(data["documentation"]);
   }

   [Fact]
   public async Task GetTypeOverview_TypeNotFound_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.GetTypeOverviewAsync(ByName("NonExistentType"), cancellationToken: CT),
         ErrorCodes.SymbolNotFound);
   }
}
