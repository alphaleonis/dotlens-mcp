using DotLens.Tools.Analysis;
using Xunit;

namespace DotLens.Tests.Analysis;

[Collection("TestFixtures")]
public class GetDependencyGraphToolTests : TestFixtureTestBase
{
   public GetDependencyGraphToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetDependencyGraphTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      _tool = new GetDependencyGraphTool(Workspace);
   }

   [Fact]
   public async Task DependencyGraph_JsonFormat_ReturnsStructuredData()
   {
      var result = await _tool.GetDependencyGraphAsync(format: DependencyGraphFormat.Json, cancellationToken: CT);

      AssertSuccess(result);
      var jsonResult = Assert.IsType<DependencyGraphJsonResponse>(result.Data);

      // Verify structure
      Assert.NotNull(jsonResult.Dependencies);
      Assert.NotEmpty(jsonResult.Dependencies);

      // Verify expected projects are present
      Assert.Contains("CoreTestProject", jsonResult.Dependencies.Keys);
      Assert.Contains("InheritanceTestProject", jsonResult.Dependencies.Keys);
   }

   [Fact]
   public async Task DependencyGraph_MermaidFormat_ReturnsValidDiagram()
   {
      var result = await _tool.GetDependencyGraphAsync(format: DependencyGraphFormat.Mermaid, cancellationToken: CT);

      AssertSuccess(result);
      var mermaidResult = Assert.IsType<DependencyGraphMermaidResponse>(result.Data);

      // Verify mermaid graph structure
      Assert.NotNull(mermaidResult.Graph);
      Assert.StartsWith("graph", mermaidResult.Graph);

      // Verify expected projects appear in the diagram
      Assert.Contains("CoreTestProject", mermaidResult.Graph);
   }

   [Fact]
   public async Task DependencyGraph_DefaultFormat_ReturnsJson()
   {
      // Default should be JSON
      var result = await _tool.GetDependencyGraphAsync(cancellationToken: CT);

      AssertSuccess(result);
      Assert.IsType<DependencyGraphJsonResponse>(result.Data);
   }

   [Fact]
   public async Task DependencyGraph_NoCycles_ReportsNoCycles()
   {
      // Test fixtures have no circular dependencies
      var result = await _tool.GetDependencyGraphAsync(format: DependencyGraphFormat.Json, cancellationToken: CT);

      AssertSuccess(result);
      var jsonResult = Assert.IsType<DependencyGraphJsonResponse>(result.Data);

      Assert.False(jsonResult.HasCycles);
      Assert.Empty(jsonResult.Cycles);
   }

   [Fact]
   public async Task DependencyGraph_IndependentProjects_HaveEmptyDependencies()
   {
      // Test fixture projects have no project references between them
      var result = await _tool.GetDependencyGraphAsync(format: DependencyGraphFormat.Json, cancellationToken: CT);

      AssertSuccess(result);
      var jsonResult = Assert.IsType<DependencyGraphJsonResponse>(result.Data);

      // Each project should have empty dependency list (no inter-project references)
      Assert.NotEmpty(jsonResult.Dependencies);
      foreach (var (projectName, dependencies) in jsonResult.Dependencies)
      {
         Assert.Empty(dependencies);
      }
   }

   [Fact]
   public async Task DependencyGraph_ContainsAllTestProjects()
   {
      var result = await _tool.GetDependencyGraphAsync(format: DependencyGraphFormat.Json, cancellationToken: CT);

      AssertSuccess(result);
      var jsonResult = Assert.IsType<DependencyGraphJsonResponse>(result.Data);

      // Verify all expected projects are in the graph
      var expectedProjects = new[]
      {
         "CoreTestProject",
         "InheritanceTestProject",
         "AnalyzerTestProject",
         "ConflictingTypesProject"
      };

      foreach (var project in expectedProjects)
      {
         Assert.Contains(project, jsonResult.Dependencies.Keys);
      }

      // Verify count matches expected
      Assert.Equal(expectedProjects.Length, jsonResult.Dependencies.Count);
   }

   [Fact]
   public async Task DependencyGraph_SuggestsNextTools()
   {
      var result = await _tool.GetDependencyGraphAsync(format: DependencyGraphFormat.Json, cancellationToken: CT);

      AssertSuccess(result);
      Assert.NotNull(result.Metadata?.SuggestedNextTools);
      Assert.NotEmpty(result.Metadata.SuggestedNextTools);
   }
}
