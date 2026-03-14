using DotLens.Tools.Analysis;
using Xunit;

namespace DotLens.Tests.Analysis;

[Collection("TestFixtures")]
public class GetProjectStructureToolTests : TestFixtureTestBase
{
   public GetProjectStructureToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetProjectStructureTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      _tool = new GetProjectStructureTool(Workspace);
   }

   [Fact]
   public async Task GetProjectStructure_ReturnsSolutionInfo()
   {
      var result = await _tool.GetProjectStructureAsync(
          includeReferences: true,
          includeDocuments: true);

      AssertSuccess(result);
      var data = GetData(result);
      var projects = data.GetArrayOrNull("projects");
      Assert.NotNull(projects);
      Assert.NotEmpty(projects);

      // Verify CoreTestProject is in the solution with expected properties
      var coreProject = projects.FirstOrDefault(p => p?.GetStringOrNull("name") == "CoreTestProject");
      Assert.NotNull(coreProject);
      Assert.Equal("C#", coreProject.GetStringOrNull("language"));
      var documentCount = coreProject.GetIntOrNull("documentCount");
      Assert.True(documentCount > 0, $"Expected documentCount > 0 but got {documentCount}");
   }

   [Fact]
   public async Task GetProjectStructure_WithSummaryOnly_ReturnsMinimalInfo()
   {
      var result = await _tool.GetProjectStructureAsync(
          includeReferences: false,
          includeDocuments: false,
          summaryOnly: true);

      AssertSuccess(result);
      var data = GetData(result);
      var projects = data.GetArrayOrNull("projects");
      Assert.NotNull(projects);
      Assert.NotEmpty(projects);

      // Summary mode should have documentCount and projectReferenceCount but NOT full details
      var firstProject = projects[0];
      Assert.NotNull(firstProject);
      Assert.NotNull(firstProject.GetStringOrNull("name"));
      Assert.NotNull(firstProject["documentCount"]);
      Assert.NotNull(firstProject["projectReferenceCount"]);
      Assert.NotNull(firstProject["language"]);

      // Summary mode should NOT have references or documents arrays
      Assert.Null(firstProject["references"]);
      Assert.Null(firstProject["documents"]);
   }

   [Fact]
   public async Task GetProjectStructure_IncludesLanguage()
   {
      var result = await _tool.GetProjectStructureAsync(
          includeReferences: true,
          includeDocuments: true);

      AssertSuccess(result);
      var data = GetData(result);
      var projects = data.GetArrayOrNull("projects");
      Assert.NotNull(projects);
      Assert.NotEmpty(projects);

      foreach (var project in projects)
      {
         Assert.Equal("C#", project?.GetStringOrNull("language"));
      }
   }

   [Fact]
   public async Task GetProjectStructure_WithProjectNamePattern_FiltersProjects()
   {
      var result = await _tool.GetProjectStructureAsync(
          includeReferences: false,
          projectNamePattern: "Core*");

      AssertSuccess(result);
      var data = GetData(result);
      var projects = data.GetArrayOrNull("projects");
      Assert.NotNull(projects);
      Assert.NotEmpty(projects);

      // All returned projects should match the pattern
      foreach (var project in projects)
      {
         var name = project?.GetStringOrNull("name");
         Assert.NotNull(name);
         Assert.StartsWith("Core", name);
      }
   }

   [Fact]
   public async Task GetProjectStructure_WithNonMatchingPattern_ReturnsEmpty()
   {
      var result = await _tool.GetProjectStructureAsync(
          includeReferences: false,
          projectNamePattern: "NonExistent*");

      AssertSuccess(result);
      var data = GetData(result);
      var projects = data.GetArrayOrNull("projects");
      Assert.NotNull(projects);
      Assert.Empty(projects);
   }

   [Fact]
   public async Task GetProjectStructure_WithMaxProjects_LimitsResults()
   {
      // Precondition: verify fixture has multiple projects so the test is meaningful
      var allResult = await _tool.GetProjectStructureAsync(includeReferences: false);
      AssertSuccess(allResult);
      var allProjects = GetData(allResult).GetArrayOrNull("projects");
      Assert.NotNull(allProjects);
      Assert.True(allProjects.Count > 1, "Test requires multiple projects to verify maxProjects limit");

      // Now test the limit
      var result = await _tool.GetProjectStructureAsync(
          includeReferences: false,
          maxProjects: 1);

      AssertSuccess(result);
      var data = GetData(result);
      var projects = data.GetArrayOrNull("projects");
      Assert.NotNull(projects);
      Assert.Single(projects);
   }

   [Fact]
   public async Task GetProjectStructure_WithDocuments_IncludesDocumentList()
   {
      var result = await _tool.GetProjectStructureAsync(
          includeReferences: false,
          includeDocuments: true,
          projectNamePattern: "CoreTestProject");

      AssertSuccess(result);
      var data = GetData(result);
      var projects = data.GetArrayOrNull("projects");
      Assert.NotNull(projects);
      Assert.NotEmpty(projects);

      var project = projects[0];
      var documents = project?.GetArrayOrNull("documents");
      Assert.NotNull(documents);
      Assert.NotEmpty(documents);

      // Verify document structure
      var firstDoc = documents[0];
      Assert.NotNull(firstDoc?.GetStringOrNull("name"));
      Assert.NotNull(firstDoc?["folders"]);
   }

   [Fact]
   public async Task GetProjectStructure_WithoutDocuments_OmitsDocumentList()
   {
      var result = await _tool.GetProjectStructureAsync(
          includeReferences: false,
          includeDocuments: false,
          projectNamePattern: "CoreTestProject");

      AssertSuccess(result);
      var data = GetData(result);
      var projects = data.GetArrayOrNull("projects");
      Assert.NotNull(projects);
      Assert.NotEmpty(projects);

      var project = projects[0];
      Assert.Null(project?["documents"]);
   }

   [Fact]
   public async Task GetProjectStructure_IncludesSolutionPath()
   {
      var result = await _tool.GetProjectStructureAsync(
          includeReferences: false);

      AssertSuccess(result);
      var data = GetData(result);
      var solutionPath = data.GetStringOrNull("solutionPath");
      Assert.NotNull(solutionPath);
      Assert.Contains("TestFixtures.sln", solutionPath);
   }
}
