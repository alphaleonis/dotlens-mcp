using System.Text.Json;
using System.Text.Json.Nodes;
using DotLens.Infrastructure;
using DotLens.Infrastructure.Results;
using DotLens.Tools.Analysis;
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

      _getDiagnostics = new GetDiagnosticsTool(workspaceManager);
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

   [Fact]
   public async Task GetDiagnostics_WithAnalyzers_IncludesCA2007_ConfigureAwait()
   {
      // Act
      var result = await _getDiagnostics.GetDiagnosticsAsync(
         filePath: _analyzerWarningsPath,
         includeAnalyzers: true,
         cancellationToken: CT);

      // Assert
      var data = GetData(result);
      var diagnostics = GetNode(data, "Diagnostics")!.AsArray();

      var ca2007 = diagnostics.FirstOrDefault(d => GetStringProperty(d, "Id") == "CA2007");

      // CA2007 availability depends on SDK version and analyzer configuration
      // Skip if not available (varies by SDK patch version)
      if (ca2007 == null)
      {
         Assert.Skip("CA2007 analyzer diagnostic not generated (SDK version may not include this rule)");
      }

      Assert.Contains("ConfigureAwait", GetStringProperty(ca2007, "Message")!);
   }

   [Fact]
   public async Task GetDiagnostics_WithAnalyzers_IncludesCA2016_CancellationToken()
   {
      // Act
      var result = await _getDiagnostics.GetDiagnosticsAsync(
         filePath: _analyzerWarningsPath,
         includeAnalyzers: true,
         cancellationToken: CT);

      // Assert
      var data = GetData(result);
      var diagnostics = GetNode(data, "Diagnostics")!.AsArray();

      var ca2016 = diagnostics.FirstOrDefault(d => GetStringProperty(d, "Id") == "CA2016");

      // CA2016 availability depends on SDK version and analyzer configuration
      // Skip if not available (varies by SDK patch version)
      if (ca2016 == null)
      {
         Assert.Skip("CA2016 analyzer diagnostic not generated (SDK version may not include this rule)");
      }

      Assert.Contains("cancellationToken", GetStringProperty(ca2016, "Message")!, StringComparison.OrdinalIgnoreCase);
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
