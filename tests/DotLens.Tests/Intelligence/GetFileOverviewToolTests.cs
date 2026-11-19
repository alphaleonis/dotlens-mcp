using DotLens.Infrastructure;
using DotLens.Tools.Intelligence;
using Xunit;

namespace DotLens.Tests.Intelligence;

[Collection("TestFixtures")]
public class GetFileOverviewToolTests : TestFixtureTestBase
{
   public GetFileOverviewToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetFileOverviewTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      _tool = new GetFileOverviewTool(Workspace);
   }

   [Fact]
   public async Task GetFileOverview_ReturnsComprehensiveInfo()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var result = await _tool.GetFileOverviewAsync(filePath, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Contains("Classes.cs", data.GetString("filePath"));
      // Classes.cs has 147 lines
      Assert.Equal(147, data.GetInt("lineCount"));

      // Verify types array contains the expected types from Classes.cs
      var types = data.GetArray("types");
      var typeNames = types.Select(t => t.GetString("name")).ToList();
      // Classes.cs defines SampleClass, SealedClass, and PartialClass
      Assert.Contains("SampleClass", typeNames);
      Assert.Contains("SealedClass", typeNames);

      // Verify first type has correct structure
      var firstType = types[0];
      Assert.False(string.IsNullOrEmpty(firstType.GetString("name")));
      Assert.False(string.IsNullOrEmpty(firstType.GetString("kind")));
      Assert.False(string.IsNullOrEmpty(firstType.GetString("accessibility")));

      // Verify location has valid data
      var location = firstType.GetObject("location");
      Assert.True(location.GetInt("line") > 0, "Type should have positive line number");
   }

   [Fact]
   public async Task GetFileOverview_WithAsyncFile_ReturnsAsyncInfo()
   {
      var filePath = GetCoreTestProjectPath("Patterns/AsyncPatterns.cs");
      var result = await _tool.GetFileOverviewAsync(filePath, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      // AsyncPatterns.cs defines AsyncService class
      var types = data.GetArray("types");
      var typeNames = types.Select(t => t.GetString("name")).ToList();
      Assert.Contains("AsyncService", typeNames);
   }

   [Fact]
   public async Task GetFileOverview_SummaryVerbosity_ReturnsTypeCountOnly()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var result = await _tool.GetFileOverviewAsync(filePath, ToolResultVerbosity.Summary, CT);

      AssertSuccess(result);
      var data = GetData(result);

      // Summary verbosity should have typeCount but not types array
      // Classes.cs has 8 top-level types (including nested types)
      Assert.Equal(8, data.GetInt("typeCount"));
      Assert.Null(data["types"]);
   }

   [Fact]
   public async Task GetFileOverview_IncludesDiagnosticsSummary()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var result = await _tool.GetFileOverviewAsync(filePath, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);

      var diagnostics = data.GetObject("diagnosticsSummary");
      // Classes.cs should compile without errors or warnings
      Assert.Equal(0, diagnostics.GetInt("errorCount"));
      Assert.Equal(0, diagnostics.GetInt("warningCount"));
   }

   [Fact]
   public async Task GetFileOverview_FileNotFound_ThrowsToolException()
   {
      var ex = await Assert.ThrowsAsync<ToolException>(() =>
         _tool.GetFileOverviewAsync("NonExistent/file.cs", cancellationToken: CT));

      Assert.Equal(ErrorCodes.FileNotInSolution, ex.Code);
   }
}
