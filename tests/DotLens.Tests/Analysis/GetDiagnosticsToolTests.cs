using DotLens.Tests;
using DotLens.Tools.Analysis;
using Xunit;

namespace DotLens.Analysis.Tests;

[Collection("TestFixtures")]
public class GetDiagnosticsToolTests : TestFixtureTestBase
{
   public GetDiagnosticsToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetDiagnosticsTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      _tool = new GetDiagnosticsTool(Workspace);
   }

   [Fact]
   public async Task GetDiagnostics_ReturnsWarnings()
   {
      var result = await _tool.GetDiagnosticsAsync(cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.NotNull(GetNode(data, "Diagnostics"));
   }

   [Fact]
   public async Task GetDiagnostics_ForSpecificFile_FiltersResults()
   {
      // Use CompilerWarnings.cs which has known diagnostics (CS0168, CS0219, CS0612, CS0414)
      var filePath = GetAnalyzerTestProjectPath("CompilerWarnings.cs");
      var result = await _tool.GetDiagnosticsAsync(
         filePath: filePath,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var diagnostics = GetNode(data, "Diagnostics")?.AsArray();
      Assert.NotNull(diagnostics);

      // CompilerWarnings.cs has known diagnostics - verify we get some results
      Assert.NotEmpty(diagnostics);

      // Verify expected diagnostic IDs are present
      var diagnosticIds = diagnostics
         .Select(d => GetStringProperty(d, "Id"))
         .ToHashSet();

      // These diagnostics are known to exist in CompilerWarnings.cs
      Assert.Contains("CS0168", diagnosticIds); // unused variable
      Assert.Contains("CS0219", diagnosticIds); // assigned but unused variable

      // All returned diagnostics should be from the specified file
      foreach (var diag in diagnostics)
      {
         var location = GetNode(diag, "Location");
         Assert.NotNull(location);
         Assert.Contains("CompilerWarnings", GetStringProperty(location, "FilePath"));
      }
   }

   [Fact]
   public async Task GetDiagnostics_FilterBySeverity_ReturnsOnlyMatchingSeverity()
   {
      // Filter by Warning since test fixtures have warnings but no errors (clean build)
      var result = await _tool.GetDiagnosticsAsync(
         severity: DiagnosticSeverityDto.Warning,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var diagnostics = GetNode(data, "Diagnostics")?.AsArray();
      Assert.NotNull(diagnostics);
      Assert.NotEmpty(diagnostics);

      foreach (var diag in diagnostics)
      {
         // Severity is now an enum serialized as lowercase string
         Assert.Equal("warning", GetStringProperty(diag, "Severity"));
      }
   }

   [Fact]
   public async Task GetDiagnostics_ForProject_FiltersResults()
   {
      var projectPath = Path.Combine(TestFixturesDirectory, "CoreTestProject", "CoreTestProject.csproj");
      var result = await _tool.GetDiagnosticsAsync(
         projectPath: projectPath,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.NotNull(GetNode(data, "Diagnostics"));
   }

   [Fact]
   public async Task GetDiagnostics_ProjectNotFound_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.GetDiagnosticsAsync(
            projectPath: "NonExistentProject.csproj",
            cancellationToken: CT),
         "PROJECT_NOT_FOUND");
   }

   [Fact]
   public async Task GetDiagnostics_IdPatternWithWildcard_FiltersResults()
   {
      // CS* should match compiler diagnostics like CS0168, CS8600, etc.
      var result = await _tool.GetDiagnosticsAsync(
         idPattern: "CS*",
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var diagnostics = GetNode(data, "Diagnostics")?.AsArray();
      Assert.NotNull(diagnostics);

      // Verify we got some results (test fixtures should have CS warnings)
      Assert.NotEmpty(diagnostics);

      // All returned diagnostics should start with CS
      foreach (var diag in diagnostics)
      {
         var id = GetStringProperty(diag, "Id");
         Assert.NotNull(id);
         Assert.StartsWith("CS", id);
      }
   }

   [Fact]
   public async Task GetDiagnostics_IdPatternNoMatch_ReturnsEmpty()
   {
      // Pattern that won't match any diagnostics
      var result = await _tool.GetDiagnosticsAsync(
         idPattern: "NONEXISTENT*",
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var diagnostics = GetNode(data, "Diagnostics")?.AsArray();

      Assert.NotNull(diagnostics);
      Assert.Empty(diagnostics);
      Assert.Equal(0, GetProperty<int>(data, "ErrorCount"));
      Assert.Equal(0, GetProperty<int>(data, "WarningCount"));
   }

   [Fact]
   public async Task GetDiagnostics_IdPatternWithQuestionMark_MatchesSingleChar()
   {
      // CS???? should match 4-digit CS codes like CS0168
      var result = await _tool.GetDiagnosticsAsync(
         idPattern: "CS????",
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var diagnostics = GetNode(data, "Diagnostics")?.AsArray();
      Assert.NotNull(diagnostics);
      Assert.NotEmpty(diagnostics);

      foreach (var diag in diagnostics)
      {
         var id = GetStringProperty(diag, "Id");
         Assert.NotNull(id);
         Assert.StartsWith("CS", id);
         Assert.Equal(6, id.Length); // CS + 4 digits
      }
   }

   [Fact]
   public async Task GetDiagnostics_NullIdPattern_ReturnsAll()
   {
      // null pattern should return all diagnostics (same as not specifying)
      var resultWithNull = await _tool.GetDiagnosticsAsync(
         idPattern: null,
         cancellationToken: CT);

      var resultWithoutPattern = await _tool.GetDiagnosticsAsync(
         cancellationToken: CT);

      AssertSuccess(resultWithNull);
      AssertSuccess(resultWithoutPattern);

      var countWithNull = GetNode(GetData(resultWithNull), "Diagnostics")?.AsArray()?.Count ?? 0;
      var countWithout = GetNode(GetData(resultWithoutPattern), "Diagnostics")?.AsArray()?.Count ?? 0;

      Assert.Equal(countWithout, countWithNull);
   }

   [Fact]
   public async Task GetDiagnostics_IdPatternCombinedWithSeverity_AppliesBothFilters()
   {
      // First, get all diagnostics to ensure there are some
      var allResult = await _tool.GetDiagnosticsAsync(cancellationToken: CT);
      AssertSuccess(allResult);
      var allDiagnostics = GetNode(GetData(allResult), "Diagnostics")?.AsArray();
      Assert.NotNull(allDiagnostics);

      // Verify there are diagnostics to filter
      Assert.NotEmpty(allDiagnostics);

      // Now apply combined filter
      var result = await _tool.GetDiagnosticsAsync(
         idPattern: "CS*",
         severity: DiagnosticSeverityDto.Warning,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var diagnostics = GetNode(data, "Diagnostics")?.AsArray();
      Assert.NotNull(diagnostics);
      Assert.NotEmpty(diagnostics);

      // All returned items must match both filters
      foreach (var diag in diagnostics)
      {
         var id = GetStringProperty(diag, "Id");
         var severity = GetStringProperty(diag, "Severity");
         Assert.NotNull(id);
         Assert.StartsWith("CS", id);
         Assert.Equal("warning", severity);
      }
   }
}
