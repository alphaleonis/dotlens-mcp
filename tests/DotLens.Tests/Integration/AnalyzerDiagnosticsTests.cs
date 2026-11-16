using System.Text.Json;
using System.Text.Json.Nodes;
using DotLens.Infrastructure;
using DotLens.Infrastructure.Results;
using DotLens.Tools.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DotLens.Tests.Integration;

/// <summary>
/// Tests for analyzer diagnostic support in roslyn_get_diagnostics.
/// Uses the TestFixtures/AnalyzerTestProject which contains intentional CA* warnings.
/// </summary>
public class AnalyzerDiagnosticsTests : IAsyncLifetime
{
   private IWorkspace _workspaceManager = null!;
   private GetDiagnosticsTool _getDiagnostics = null!;
   private string _testFixturesSolutionPath = null!;
   private string _analyzerWarningsPath = null!;

   protected CancellationToken CT => TestContext.Current.CancellationToken;

   public async ValueTask InitializeAsync()
   {
      // Find the TestFixtures solution path
      var currentDir = Directory.GetCurrentDirectory();
      var solutionDir = currentDir;

      while (solutionDir != null)
      {
         var testFixturesPath = Path.Combine(solutionDir, "tests", "TestFixtures", "TestFixtures.sln");
         if (File.Exists(testFixturesPath))
         {
            _testFixturesSolutionPath = testFixturesPath;
            _analyzerWarningsPath = Path.Combine(solutionDir, "tests", "TestFixtures", "AnalyzerTestProject", "AnalyzerWarnings.cs");
            break;
         }
         solutionDir = Directory.GetParent(solutionDir)?.FullName;
      }

      if (_testFixturesSolutionPath == null)
         throw new InvalidOperationException("Could not find TestFixtures.sln");

      // Create workspace manager and load the test fixtures solution
      var options = Options.Create(new DotLensOptions());
      var workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance, options);
      _workspaceManager = workspaceManager;

      await TestFixtureRestore.EnsureRestoredAsync(_testFixturesSolutionPath);
      var loadResult = await workspaceManager.LoadSolutionAsync(_testFixturesSolutionPath);
      var json = JsonNode.Parse(JsonSerializer.Serialize(loadResult, loadResult.GetType(), TestJsonOptions.CamelCase))!;
      Assert.NotNull(json["data"]);  // Success indicated by presence of data
      await AssertFrameworkReferencesResolvedAsync(workspaceManager.EnsureSolutionLoaded(), "AnalyzerTestProject");

      _getDiagnostics = new GetDiagnosticsTool(workspaceManager);
   }

   /// <summary>
   /// Fails setup when the project's compilation cannot resolve <see cref="object"/>, so a load without framework
   /// references fails with that cause instead of as missing CA2007/CA2016 diagnostics.
   /// </summary>
   private async Task AssertFrameworkReferencesResolvedAsync(Solution solution, string projectName)
   {
      var project = Assert.Single(solution.Projects, p => p.Name == projectName);
      var compilation = await project.GetCompilationAsync(CT);
      Assert.NotNull(compilation);
      var objectType = compilation.GetSpecialType(SpecialType.System_Object);
      Assert.True(
         objectType.TypeKind != TypeKind.Error,
         $"{projectName} loaded without framework references ({project.MetadataReferences.Count} metadata references); restore the fixture before loading it.");
   }

   public ValueTask DisposeAsync()
   {
      (_workspaceManager as IDisposable)?.Dispose();
      return ValueTask.CompletedTask;
   }

   private JsonNode GetData(object response)
   {
      // Handle new typed ToolResult directly - if we got a result, it succeeded
      // Errors now throw ToolException instead of returning failure results
      if (response is ToolResult)
      {
         // Success - errors are thrown, not returned
      }

      var json = JsonNode.Parse(JsonSerializer.Serialize(response, response.GetType(), TestJsonOptions.CamelCase))!;

      return json["data"] ?? json["Data"]!;
   }

   private static string? GetStringProperty(JsonNode? node, string pascalCaseName)
   {
      if (node == null)
         return null;
      var camelCaseName = char.ToLowerInvariant(pascalCaseName[0]) + pascalCaseName[1..];
      var value = node[pascalCaseName] ?? node[camelCaseName];
      return value?.GetValue<string>();
   }

   private static JsonNode? GetNode(JsonNode? node, string pascalCaseName)
   {
      if (node == null)
         return null;
      var camelCaseName = char.ToLowerInvariant(pascalCaseName[0]) + pascalCaseName[1..];
      return node[pascalCaseName] ?? node[camelCaseName];
   }

   [Fact]
   public async Task GetDiagnostics_WithoutAnalyzers_ReturnsOnlyCompilerDiagnostics()
   {
      // Act - Get diagnostics without analyzers (default behavior)
      var result = await _getDiagnostics.GetDiagnosticsAsync(
         includeAnalyzers: false,
         cancellationToken: CT);

      // Assert
      var data = GetData(result);
      var diagnostics = GetNode(data, "Diagnostics")!.AsArray();

      // Should only have CS* codes (compiler diagnostics), no CA* codes
      var diagnosticIds = diagnostics.Select(d => GetStringProperty(d, "Id")).ToList();

      // Verify we have some CS* diagnostics
      Assert.Contains(diagnosticIds, id => id?.StartsWith("CS") == true);

      // Verify we don't have CA* diagnostics
      Assert.DoesNotContain(diagnosticIds, id => id?.StartsWith("CA") == true);
   }

   [Fact]
   public async Task GetDiagnostics_WithAnalyzers_ReturnsAnalyzerDiagnostics()
   {
      // Act - Get diagnostics with analyzers
      var result = await _getDiagnostics.GetDiagnosticsAsync(
         includeAnalyzers: true,
         cancellationToken: CT);

      // Assert
      var data = GetData(result);
      var diagnostics = GetNode(data, "Diagnostics")!.AsArray();

      var diagnosticIds = diagnostics.Select(d => GetStringProperty(d, "Id")).ToList();

      // Should have CA* codes (analyzer diagnostics)
      Assert.Contains(diagnosticIds, id => id?.StartsWith("CA") == true);
   }

   private static List<(string Severity, string Message, int StartLine, int StartColumn)> DiagnosticsWithId(
      JsonNode data, string id) =>
      data.GetArray("diagnostics")
         .Where(d => d.GetString("id") == id)
         .Select(d =>
         {
            var location = d.GetObject("location");
            return (d.GetString("severity"), d.GetString("message"), location.GetInt("startLine"), location.GetInt("startColumn"));
         })
         .OrderBy(d => d.Item3)
         .ThenBy(d => d.Item4)
         .ToList();

   [Fact]
   public async Task GetDiagnostics_WithAnalyzers_IncludesCA2007_ConfigureAwait()
   {
      var result = await _getDiagnostics.GetDiagnosticsAsync(
         filePath: _analyzerWarningsPath,
         includeAnalyzers: true,
         cancellationToken: CT);

      // Both awaits in AnalyzerWarnings.cs, on the GetStringAsync call that each one awaits.
      Assert.Equal(
         [
            ("warning", "Consider calling ConfigureAwait on the awaited task", 15, 28),
            ("warning", "Consider calling ConfigureAwait on the awaited task", 25, 28),
         ],
         DiagnosticsWithId(GetData(result), "CA2007"));
   }

   [Fact]
   public async Task GetDiagnostics_WithAnalyzers_IncludesCA2016_CancellationToken()
   {
      var result = await _getDiagnostics.GetDiagnosticsAsync(
         filePath: _analyzerWarningsPath,
         includeAnalyzers: true,
         cancellationToken: CT);

      Assert.Equal(
         [
            ("warning",
               "Forward the 'cancellationToken' parameter to the 'GetStringAsync' method or pass in 'CancellationToken.None' explicitly to indicate intentionally not propagating the token",
               25, 28),
         ],
         DiagnosticsWithId(GetData(result), "CA2016"));
   }

   [Fact]
   public async Task GetDiagnostics_WithAnalyzers_IncludesCA1822_CouldBeStatic()
   {
      // Act
      var result = await _getDiagnostics.GetDiagnosticsAsync(
         filePath: _analyzerWarningsPath,
         includeAnalyzers: true,
         cancellationToken: CT);

      // Assert
      var data = GetData(result);
      var diagnostics = GetNode(data, "Diagnostics")!.AsArray();

      var ca1822 = diagnostics.FirstOrDefault(d => GetStringProperty(d, "Id") == "CA1822");
      Assert.NotNull(ca1822);
      Assert.Contains("static", GetStringProperty(ca1822, "Message")!, StringComparison.OrdinalIgnoreCase);
   }

   [Fact]
   public async Task GetDiagnostics_SeverityFilter_WorksWithAnalyzers()
   {
      // Act - Filter by Warning severity
      var result = await _getDiagnostics.GetDiagnosticsAsync(
         severity: DiagnosticSeverityDto.Warning,
         includeAnalyzers: true,
         cancellationToken: CT);

      // Assert
      var data = GetData(result);
      var diagnostics = GetNode(data, "Diagnostics")!.AsArray();

      // All returned diagnostics should be warnings (enum serializes as lowercase)
      foreach (var diag in diagnostics)
      {
         Assert.Equal("warning", GetStringProperty(diag, "Severity"));
      }

      // Should include both CS* and CA* warnings
      var diagnosticIds = diagnostics.Select(d => GetStringProperty(d, "Id")).ToList();
      Assert.Contains(diagnosticIds, id => id?.StartsWith("CA") == true);
   }

   [Fact]
   public async Task GetDiagnostics_ForProject_ReturnsProjectAnalyzerDiagnostics()
   {
      // Arrange - Get the project path
      var projectPath = Path.Combine(
         Path.GetDirectoryName(_testFixturesSolutionPath)!,
         "AnalyzerTestProject",
         "AnalyzerTestProject.csproj");

      // Act
      var result = await _getDiagnostics.GetDiagnosticsAsync(
         projectPath: projectPath,
         includeAnalyzers: true,
         cancellationToken: CT);

      // Assert
      var data = GetData(result);
      var diagnostics = GetNode(data, "Diagnostics")!.AsArray();

      var diagnosticIds = diagnostics.Select(d => GetStringProperty(d, "Id")).ToList();

      // Should have CA* analyzer diagnostics
      Assert.Contains(diagnosticIds, id => id?.StartsWith("CA") == true);
   }

   [Fact]
   public async Task GetDiagnostics_DefaultBehavior_UnchangedWithNoParameter()
   {
      // Act - Call without includeAnalyzers parameter (defaults to false)
      var result = await _getDiagnostics.GetDiagnosticsAsync(cancellationToken: CT);

      // Assert
      var data = GetData(result);
      var diagnostics = GetNode(data, "Diagnostics")!.AsArray();

      var diagnosticIds = diagnostics.Select(d => GetStringProperty(d, "Id")).ToList();

      // Should have CS* but not CA* (backward compatibility)
      Assert.Contains(diagnosticIds, id => id?.StartsWith("CS") == true);
      Assert.DoesNotContain(diagnosticIds, id => id?.StartsWith("CA") == true);
   }
}
