using System.Text.RegularExpressions;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Search;
using Xunit;

namespace DotLens.Tests.Search;

[Collection("TestFixtures")]
public class GetMethodInfoToolTests : TestFixtureTestBase
{
   public GetMethodInfoToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetMethodInfoTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      var symbolLookup = new SymbolLookupService(Workspace);
      _tool = new GetMethodInfoTool(Workspace, symbolLookup);
   }

   [Fact]
   public async Task GetMethodInfo_ReturnsSignatureAsCode()
   {
      var result = await _tool.GetMethodInfoAsync(
         new LookupByName { Name = "SampleClass.Calculate" },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);

      Assert.Equal("Calculate", data.GetStringOrNull("methodName"));
      Assert.NotNull(data["overloads"]);

      var overloads = data["overloads"]?.AsArray();
      Assert.NotNull(overloads);
      Assert.NotEmpty(overloads);

      // Verify signature matches expected format: "public int Calculate(int a, int b)"
      var signature = overloads[0]?.GetStringOrNull("signature");
      Assert.NotNull(signature);
      Assert.Matches(@"public\s+int\s+Calculate\s*\(int\s+\w+,\s*int\s+\w+\)", signature);
   }

   [Fact]
   public async Task GetMethodInfo_ReturnsAllOverloads()
   {
      // SampleClass.Format has 3 overloads:
      // - Format(string value)
      // - Format(int value)
      // - Format(string value, string prefix)
      var result = await _tool.GetMethodInfoAsync(
         new LookupByName { Name = "SampleClass.Format" },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);

      var overloads = data["overloads"]?.AsArray();
      Assert.NotNull(overloads);
      Assert.Equal(3, overloads.Count);

      // Verify distinct overloads exist by checking signatures
      var signatures = overloads.Select(o => o?.GetStringOrNull("signature")).ToList();
      Assert.Contains(signatures, s => s != null && Regex.IsMatch(s, @"Format\s*\(string\s+\w+\)"));
      Assert.Contains(signatures, s => s != null && Regex.IsMatch(s, @"Format\s*\(int\s+\w+\)"));
      Assert.Contains(signatures, s => s != null && Regex.IsMatch(s, @"Format\s*\(string\s+\w+,\s*string\s+\w+\)"));
   }

   [Fact]
   public async Task GetMethodInfo_IncludesLocation()
   {
      var result = await _tool.GetMethodInfoAsync(
         new LookupByName { Name = "SampleClass.Calculate" },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);

      var overloads = data["overloads"]?.AsArray();
      Assert.NotNull(overloads);

      var location = overloads[0]?["location"];
      Assert.NotNull(location);
      Assert.NotNull(location?["filePath"]);
      Assert.True(location?.GetIntOrNull("startLine") > 0);
      Assert.True(location?.GetIntOrNull("endLine") > 0);
   }

   [Theory]
   [InlineData("SampleClass.Create", @"public\s+static\s+\S*SampleClass\s+Create")]
   [InlineData("AsyncService.FetchDataAsync", @"public\s+async\s+\S*Task")]
   public async Task GetMethodInfo_IncludesModifierKeywords(string methodName, string expectedPattern)
   {
      var result = await _tool.GetMethodInfoAsync(
         new LookupByName { Name = methodName },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);

      var overloads = data["overloads"]?.AsArray();
      var signature = overloads?[0]?.GetStringOrNull("signature");

      Assert.NotNull(signature);
      Assert.Matches(expectedPattern, signature);
   }

   [Fact]
   public async Task GetMethodInfo_MethodNotFound_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.GetMethodInfoAsync(
            new LookupByName { Name = "SampleClass.NonExistentMethod" },
            cancellationToken: CT),
         "SYMBOL_NOT_FOUND");
   }

   [Fact]
   public async Task GetMethodInfo_TypeNotFound_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.GetMethodInfoAsync(
            new LookupByName { Name = "NonExistentClass.SomeMethod" },
            cancellationToken: CT),
         "SYMBOL_NOT_FOUND");
   }

   [Fact]
   public async Task GetMethodInfo_PositionLookup_Works()
   {
      // Use a known file path from test fixtures
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");

      // SampleClass.Calculate is defined at line 48 in Classes.cs
      // Line 48: "   public int Calculate(int a, int b)"
      var posResult = await _tool.GetMethodInfoAsync(
         new LookupByPosition { FilePath = filePath, Line = 48, Column = 15 },
         cancellationToken: CT);

      AssertSuccess(posResult);
      var data = GetData(posResult);

      // Should find the Calculate method in SampleClass
      Assert.Equal("Calculate", data.GetStringOrNull("methodName"));

      var containingType = data.GetStringOrNull("containingType");
      Assert.NotNull(containingType);
      Assert.True(
         containingType == "SampleClass" || containingType.EndsWith(".SampleClass"),
         $"Expected SampleClass but got {containingType}");
   }

   [Fact]
   public async Task GetMethodInfo_Constructor_ReturnsAllOverloads()
   {
      // SampleClass has 3 constructors at lines 30, 32, 37 in Classes.cs:
      // - SampleClass()
      // - SampleClass(int id)
      // - SampleClass(int id, string name)
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");

      // Line 30 is "   public SampleClass() { }"
      var result = await _tool.GetMethodInfoAsync(
         new LookupByPosition { FilePath = filePath, Line = 30, Column = 10 },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);

      Assert.Equal(".ctor", data.GetStringOrNull("methodName"));

      var overloads = data["overloads"]?.AsArray();
      Assert.NotNull(overloads);
      Assert.Equal(3, overloads.Count);

      // Verify distinct constructor signatures
      var signatures = overloads.Select(o => o?.GetStringOrNull("signature")).ToList();
      Assert.Contains(signatures, s => s != null && Regex.IsMatch(s, @"SampleClass\s*\(\s*\)"));
      Assert.Contains(signatures, s => s != null && Regex.IsMatch(s, @"SampleClass\s*\(int\s+\w+\)"));
      Assert.Contains(signatures, s => s != null && Regex.IsMatch(s, @"SampleClass\s*\(int\s+\w+,\s*string\s+\w+\)"));
   }

   [Fact]
   public async Task GetMethodInfo_PositionLookup_InvalidPosition_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");

      // Line 9999 doesn't exist in the file
      await AssertThrowsToolExceptionAsync(
         () => _tool.GetMethodInfoAsync(
            new LookupByPosition { FilePath = filePath, Line = 9999, Column = 1 },
            cancellationToken: CT),
         "INVALID_PARAMETER");
   }

   [Fact]
   public async Task GetMethodInfo_PositionLookup_FileNotInSolution_ThrowsToolException()
   {
      var nonExistentPath = GetCoreTestProjectPath("Types/NonExistent.cs");

      await AssertThrowsToolExceptionAsync(
         () => _tool.GetMethodInfoAsync(
            new LookupByPosition { FilePath = nonExistentPath, Line = 1, Column = 1 },
            cancellationToken: CT),
         "FILE_NOT_IN_SOLUTION");
   }
}
