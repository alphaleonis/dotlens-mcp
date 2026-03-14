using DotLens.Infrastructure;
using DotLens.Tools.Search;
using Xunit;

namespace DotLens.Tests.Search;

[Collection("TestFixtures")]
public class GetContainingMemberToolTests : TestFixtureTestBase
{
   public GetContainingMemberToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetContainingMemberTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      _tool = new GetContainingMemberTool(Workspace);
   }

   [Fact]
   public async Task GetContainingMember_ReturnsEnclosingSymbol()
   {
      // Line 45 (1-based) in Classes.cs is the body of DoSomething method (`_privateField++;`)
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var result = await _tool.GetContainingMemberAsync(filePath, 45, 7, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var member = data["member"]?.AsObject();
      Assert.NotNull(member);
      Assert.Equal("DoSomething", member.GetStringOrNull("name"));
      Assert.Equal("method", member.GetStringOrNull("kind"));
   }

   [Fact]
   public async Task GetContainingMember_InMethod_ReturnsMethodInfo()
   {
      // Line 21 (1-based) in AsyncPatterns.cs is inside CalculateAsync method
      var filePath = GetCoreTestProjectPath("Patterns/AsyncPatterns.cs");
      var result = await _tool.GetContainingMemberAsync(filePath, 21, 10, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var member = data["member"]?.AsObject();
      Assert.NotNull(member);
      // SymbolResponse uses lowercase discriminator values
      Assert.Equal("method", member.GetStringOrNull("kind"));
      Assert.Equal("CalculateAsync", member.GetStringOrNull("name"));
   }

   [Fact]
   public async Task GetContainingMember_OnFieldDeclaration_ReturnsContainingClass()
   {
      // Line 12 (1-based) in Classes.cs is a field declaration inside SampleClass
      // Fields are not immediate members, so the containing class is returned
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var result = await _tool.GetContainingMemberAsync(filePath, 12, 5, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var member = data["member"]?.AsObject();
      Assert.NotNull(member);
      Assert.Equal("SampleClass", member.GetStringOrNull("name"));
      Assert.Equal("class", member.GetStringOrNull("kind"));
   }

   [Fact]
   public async Task GetContainingMember_InProperty_ReturnsPropertyInfo()
   {
      // Line 27 (1-based) in Classes.cs is the DisplayName expression-bodied property
      // public string DisplayName => $"{Name} ({Id})";
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var result = await _tool.GetContainingMemberAsync(filePath, 27, 30, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var member = data["member"]?.AsObject();
      Assert.NotNull(member);
      Assert.Equal("property", member.GetStringOrNull("kind"));
      Assert.Equal("DisplayName", member.GetStringOrNull("name"));
   }

   [Fact]
   public async Task GetContainingMember_WithVerbosity_ReturnsExpectedDetail()
   {
      var filePath = GetCoreTestProjectPath("Patterns/AsyncPatterns.cs");

      // Summary: just name
      var summaryResult = await _tool.GetContainingMemberAsync(
         filePath, 21, 10, verbosity: ToolResultVerbosity.Summary, cancellationToken: CT);
      AssertSuccess(summaryResult);
      var summaryData = GetData(summaryResult);
      var summaryMember = summaryData["member"]?.AsObject();
      Assert.NotNull(summaryMember);
      Assert.Equal("CalculateAsync", summaryMember.GetStringOrNull("name"));
      Assert.Null(summaryMember["location"]); // No location in summary

      // Full: includes more detail
      var fullResult = await _tool.GetContainingMemberAsync(
         filePath, 21, 10, verbosity: ToolResultVerbosity.Full, cancellationToken: CT);
      AssertSuccess(fullResult);
      var fullData = GetData(fullResult);
      var fullMember = fullData["member"]?.AsObject();
      Assert.NotNull(fullMember);
      Assert.Equal("CalculateAsync", fullMember.GetStringOrNull("name"));
      Assert.NotNull(fullMember["location"]); // Has location in full
   }

   [Fact]
   public async Task GetContainingMember_WithHierarchy_ReturnsAllEnclosingScopes()
   {
      // Line 45 (1-based) in Classes.cs is inside DoSomething method in SampleClass
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var result = await _tool.GetContainingMemberAsync(filePath, 45, 7, includeHierarchy: true, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);

      // Should have member
      var member = data["member"]?.AsObject();
      Assert.NotNull(member);
      Assert.Equal("method", member.GetStringOrNull("kind"));
      Assert.Equal("DoSomething", member.GetStringOrNull("name"));

      // Should have hierarchy
      var hierarchy = data["hierarchy"]?.AsArray();
      Assert.NotNull(hierarchy);
      Assert.NotEmpty(hierarchy);
      Assert.True(hierarchy.Count >= 2, "Expected at least method and class in hierarchy");

      // First should be the method (innermost)
      var first = hierarchy[0]?.AsObject();
      Assert.NotNull(first);
      Assert.Equal("DoSomething", first.GetStringOrNull("name"));

      // Should include the class
      var kinds = hierarchy.Select(h => h?.AsObject()?.GetStringOrNull("kind")).ToList();
      Assert.Contains("class", kinds);

      // Should have context path
      var contextPath = data.GetStringOrNull("contextPath");
      Assert.NotNull(contextPath);
      Assert.Contains("SampleClass", contextPath);
      Assert.Contains("DoSomething", contextPath);
   }

   [Fact]
   public async Task GetContainingMember_WithoutHierarchy_DoesNotIncludeHierarchyFields()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var result = await _tool.GetContainingMemberAsync(filePath, 45, 7, includeHierarchy: false, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);

      // Should have member
      Assert.NotNull(data["member"]);

      // Should NOT have hierarchy or contextPath
      Assert.Null(data["hierarchy"]);
      Assert.Null(data["contextPath"]);
   }

   [Fact]
   public async Task GetContainingMember_FileNotFound_ThrowsToolException()
   {
      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _tool.GetContainingMemberAsync("nonexistent.cs", 1, 1, cancellationToken: CT));
      Assert.Equal(ErrorCodes.FileNotInSolution, ex.Code);
   }
}
