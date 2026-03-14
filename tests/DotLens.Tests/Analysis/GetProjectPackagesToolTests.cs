using DotLens.Tools.Analysis;
using Xunit;

namespace DotLens.Tests.Analysis;

[Collection("TestFixtures")]
public class GetProjectPackagesToolTests : TestFixtureTestBase
{
   public GetProjectPackagesToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetProjectPackagesTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      _tool = new GetProjectPackagesTool(Workspace);
   }

   [Fact]
   public async Task GetProjectPackages_AllProjects_ReturnsKnownPackages()
   {
      var result = await _tool.GetProjectPackagesAsync(CT);

      AssertSuccess(result);
      var data = GetData(result);

      // Verify we found at least the known package
      var totalPackages = data.GetIntOrNull("totalPackages");
      Assert.NotNull(totalPackages);
      Assert.True(totalPackages >= 1, "Should find at least Newtonsoft.Json");

      // Verify packages array exists and has items
      var packages = data["packages"]?.AsArray();
      Assert.NotNull(packages);
      Assert.NotEmpty(packages);

      // Find Newtonsoft.Json specifically
      var newtonsoftPackage = packages.FirstOrDefault(p =>
         p.GetStringOrNull("name") == "Newtonsoft.Json");
      Assert.NotNull(newtonsoftPackage);

      // Verify version info
      var versions = newtonsoftPackage["versions"]?.AsArray();
      Assert.NotNull(versions);
      Assert.NotEmpty(versions);

      var versionEntry = versions[0];
      Assert.Equal("13.0.3", versionEntry.GetStringOrNull("version"));
      Assert.Equal("CoreTestProject", versionEntry.GetStringOrNull("project"));
   }

   [Fact]
   public async Task GetProjectPackages_SpecificProject_FiltersToThatProject()
   {
      var result = await _tool.GetProjectPackagesAsync(CT, projectName: "CoreTestProject");

      AssertSuccess(result);
      var data = GetData(result);

      // CoreTestProject has Newtonsoft.Json
      var totalPackages = data.GetIntOrNull("totalPackages");
      Assert.Equal(1, totalPackages);

      var packages = data["packages"]?.AsArray();
      Assert.NotNull(packages);
      Assert.Single(packages);

      var package = packages[0];
      Assert.Equal("Newtonsoft.Json", package.GetStringOrNull("name"));
   }

   [Fact]
   public async Task GetProjectPackages_ProjectWithNoPackages_ReturnsEmptyList()
   {
      // InheritanceTestProject has no package references
      var result = await _tool.GetProjectPackagesAsync(CT, projectName: "InheritanceTestProject");

      AssertSuccess(result);
      var data = GetData(result);

      Assert.Equal(0, data.GetIntOrNull("totalPackages"));

      var packages = data["packages"]?.AsArray();
      Assert.NotNull(packages);
      Assert.Empty(packages);
   }

   [Fact]
   public async Task GetProjectPackages_NonexistentProject_ReturnsEmptyList()
   {
      var result = await _tool.GetProjectPackagesAsync(CT, projectName: "NonexistentProject");

      AssertSuccess(result);
      var data = GetData(result);

      Assert.Equal(0, data.GetIntOrNull("totalPackages"));

      var packages = data["packages"]?.AsArray();
      Assert.NotNull(packages);
      Assert.Empty(packages);
   }

   [Fact]
   public async Task GetProjectPackages_NoParseErrors_ParseErrorsIsNull()
   {
      var result = await _tool.GetProjectPackagesAsync(CT);

      AssertSuccess(result);
      var data = GetData(result);

      // parseErrors should be null when all projects parse successfully
      var parseErrors = data["parseErrors"];
      Assert.Null(parseErrors);
   }
}
