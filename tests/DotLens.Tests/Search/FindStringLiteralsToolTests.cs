using DotLens.Tools.Search;
using Xunit;

namespace DotLens.Tests.Search;

[Collection("TestFixtures")]
public class FindStringLiteralsToolTests : TestFixtureTestBase
{
   public FindStringLiteralsToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private FindStringLiteralsTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      _tool = new FindStringLiteralsTool(Workspace);
   }

   [Fact]
   public async Task FindStringLiterals_FindsMatchingStrings()
   {
      // Search for "Hello" which appears in StringLiteralExamples
      var result = await _tool.FindStringLiteralsAsync(
         searchText: "Hello",
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var literals = data.AsArray();
      Assert.NotNull(literals);
      Assert.NotEmpty(literals!);

      foreach (var literal in literals!)
      {
         var value = literal!.GetStringOrNull("value");
         Assert.NotNull(value);
         Assert.Contains("Hello", value, StringComparison.OrdinalIgnoreCase);
      }
   }

   [Fact]
   public async Task FindStringLiterals_ExactMatch_FindsOnlyExact()
   {
      // Search for exact string from CoreTestProject
      var result = await _tool.FindStringLiteralsAsync(
         searchText: "TestApplication",
         exactMatch: true,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var literals = data.AsArray();
      Assert.NotNull(literals);
      Assert.NotEmpty(literals!);

      foreach (var literal in literals!)
      {
         var value = literal!.GetStringOrNull("value");
         Assert.NotNull(value);
         Assert.Equal("TestApplication", value);
      }
   }

   [Fact]
   public async Task FindStringLiterals_ByProject_FiltersCorrectly()
   {
      var result = await _tool.FindStringLiteralsAsync(
         projectName: "CoreTestProject",
         maxResults: 10,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var literals = data.AsArray();
      Assert.NotNull(literals);
      Assert.NotEmpty(literals!);

      // All results should be from CoreTestProject (check file paths)
      foreach (var literal in literals!)
      {
         var filePath = literal!.GetStringOrNull("filePath");
         Assert.NotNull(filePath);
         Assert.Contains("CoreTestProject", filePath, StringComparison.OrdinalIgnoreCase);
      }
   }

   [Fact]
   public async Task FindStringLiterals_NoMatch_ReturnsEmptyList()
   {
      var result = await _tool.FindStringLiteralsAsync(
         searchText: "zzz_nonexistent_string_that_is_not_in_test_fixtures_zzz",
         exactMatch: true,
         projectName: "CoreTestProject",
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var literals = data.AsArray();
      Assert.NotNull(literals);
      Assert.Empty(literals!);
   }

   [Fact]
   public async Task FindStringLiterals_FindsErrorMessages()
   {
      // Search for error-related strings in CoreTestProject
      var result = await _tool.FindStringLiteralsAsync(
         searchText: "Error",
         maxResults: 20,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var literals = data.AsArray();
      Assert.NotNull(literals);
      Assert.NotEmpty(literals!);

      // Verify all results contain "Error"
      foreach (var literal in literals!)
      {
         var value = literal!.GetStringOrNull("value");
         Assert.NotNull(value);
         Assert.Contains("Error", value, StringComparison.OrdinalIgnoreCase);
      }
   }

   [Fact]
   public async Task FindStringLiterals_FindsUrlStrings()
   {
      // Search for URL strings
      var result = await _tool.FindStringLiteralsAsync(
         searchText: "https://",
         maxResults: 20,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var literals = data.AsArray();
      Assert.NotNull(literals);
      Assert.NotEmpty(literals!);

      // Verify all results contain "https://"
      foreach (var literal in literals!)
      {
         var value = literal!.GetStringOrNull("value");
         Assert.NotNull(value);
         Assert.Contains("https://", value, StringComparison.OrdinalIgnoreCase);
      }
   }

   [Fact]
   public async Task FindStringLiterals_MaxResults_LimitsOutput()
   {
      // Request only 3 results
      var result = await _tool.FindStringLiteralsAsync(
         maxResults: 3,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var literals = data.AsArray();
      Assert.NotNull(literals);
      Assert.True(literals!.Count > 0 && literals.Count <= 3,
         $"Expected 1-3 results but got {literals.Count}");
   }

   [Fact]
   public async Task FindStringLiterals_IncludesContainingMember()
   {
      // Search for a string we know is in a specific method
      var result = await _tool.FindStringLiteralsAsync(
         searchText: "Bad Request",
         exactMatch: true,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var literals = data.AsArray();
      Assert.NotNull(literals);
      Assert.NotEmpty(literals!);

      // The first match should have a containing member (GetErrorMessage method)
      var firstLiteral = literals![0];
      Assert.NotNull(firstLiteral);
      var containingMember = firstLiteral!.GetStringOrNull("containingMember");
      Assert.NotNull(containingMember);
      Assert.Equal("GetErrorMessage", containingMember);
   }

   [Fact]
   public async Task FindStringLiterals_IncludesLocationInfo()
   {
      var result = await _tool.FindStringLiteralsAsync(
         searchText: "TestApplication",
         exactMatch: true,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var literals = data.AsArray();
      Assert.NotNull(literals);
      Assert.NotEmpty(literals!);

      var firstLiteral = literals![0];
      Assert.NotNull(firstLiteral);

      // Check all location properties are present and valid
      var filePath = firstLiteral!.GetStringOrNull("filePath");
      var line = firstLiteral.GetIntOrNull("line");
      var column = firstLiteral.GetIntOrNull("column");

      Assert.NotNull(filePath);
      Assert.NotNull(line);
      Assert.NotNull(column);
      Assert.True(line > 0, "Line should be 1-based");
      Assert.True(column > 0, "Column should be 1-based");
   }

   [Fact]
   public async Task FindStringLiterals_NonExistentProject_ReturnsEmptyList()
   {
      var result = await _tool.FindStringLiteralsAsync(
         projectName: "NonExistentProject",
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var literals = data.AsArray();
      Assert.NotNull(literals);
      Assert.Empty(literals!);
   }
}
