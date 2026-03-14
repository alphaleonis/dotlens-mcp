using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Search;
using Xunit;

namespace DotLens.Tests.Search;

[Collection("TestFixtures")]
public class GetTypeMembersToolTests : TestFixtureTestBase
{
   public GetTypeMembersToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetTypeMembersTool _tool = null!;

   private static LookupByName ByName(string name) => new() { Name = name };
   private static LookupByPosition ByPosition(string filePath, int line, int column = 1)
      => new() { FilePath = filePath, Line = line, Column = column };

   public override async ValueTask InitializeAsync()
   {
      var symbolLookup = new SymbolLookupService(Workspace);
      _tool = new GetTypeMembersTool(Workspace, symbolLookup);
   }

   [Fact]
   public async Task GetTypeMembers_ByName_ReturnsAllMembers()
   {
      // Use SampleClass from CoreTestProject which has various members
      var result = await _tool.GetTypeMembersAsync(ByName("SampleClass"), cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Contains("SampleClass", data.GetStringOrNull("typeName"));
      // SampleClass has 23 members: 5 fields + 4 properties + 3 constructors + 8 methods + 3 property accessors
      Assert.Equal(23, data.GetInt("totalMembers"));
   }

   [Fact]
   public async Task GetTypeMembers_ByPosition_ReturnsMembers()
   {
      // SampleClass is defined at line 12 in Types/Classes.cs
      var classesPath = GetCoreTestProjectPath("Types/Classes.cs");
      var result = await _tool.GetTypeMembersAsync(ByPosition(classesPath, 12), cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Contains("SampleClass", data.GetStringOrNull("typeName"));
   }

   [Fact]
   public async Task GetTypeMembers_WithKindFilter_FiltersResults()
   {
      // Filter to only methods
      var result = await _tool.GetTypeMembersAsync(ByName("SampleClass"), memberKind: MemberKindFilter.Method, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var members = data["members"]?.AsArray();
      Assert.NotNull(members);

      foreach (var member in members!)
      {
         Assert.Equal("Method", member.GetStringOrNull("kind"));
      }
   }

   [Fact]
   public async Task GetTypeMembers_ReturnsProperties()
   {
      var result = await _tool.GetTypeMembersAsync(ByName("SampleClass"), memberKind: MemberKindFilter.Property, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var members = data["members"]?.AsArray();
      Assert.NotNull(members);
      Assert.NotEmpty(members!);

      // Should find Id, Name, IsActive, DisplayName properties
      var propertyNames = members.Select(m => m!.GetStringOrNull("name")).ToList();
      Assert.Contains("Id", propertyNames);
   }

   [Fact]
   public async Task GetTypeMembers_ReturnsFields()
   {
      var result = await _tool.GetTypeMembersAsync(ByName("SampleClass"), memberKind: MemberKindFilter.Field, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var members = data["members"]?.AsArray();
      Assert.NotNull(members);
      Assert.NotEmpty(members!);

      // SampleClass has _privateField, _protectedField, _internalField, PublicReadonlyField, ConstantField
      var fieldNames = members.Select(m => m!.GetStringOrNull("name")).ToList();
      Assert.Contains("_privateField", fieldNames);
      Assert.Contains("PublicReadonlyField", fieldNames);
   }

   [Fact]
   public async Task GetTypeMembers_Interface_ReturnsInterfaceMembers()
   {
      var result = await _tool.GetTypeMembersAsync(ByName("IBasicService"), cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Contains("IBasicService", data.GetStringOrNull("typeName"));

      var members = data["members"]?.AsArray();
      Assert.NotNull(members);
      Assert.NotEmpty(members!);
   }

   [Fact]
   public async Task GetTypeMembers_GenericType_ReturnsMembers()
   {
      var result = await _tool.GetTypeMembersAsync(ByName("Container"), cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Contains("Container", data.GetStringOrNull("typeName"));
   }

   [Fact]
   public async Task GetTypeMembers_TypeNotFound_ThrowsToolException()
   {
      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _tool.GetTypeMembersAsync(ByName("NonExistentType"), cancellationToken: CT));

      Assert.Equal(ErrorCodes.SymbolNotFound, ex.Code);
   }

   [Fact]
   public async Task GetTypeMembers_SummaryVerbosity_ReturnsMinimalInfo()
   {
      var result = await _tool.GetTypeMembersAsync(
         ByName("SampleClass"),
         verbosity: ToolResultVerbosity.Summary,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var members = data["members"]?.AsArray();
      Assert.NotNull(members);
      Assert.NotEmpty(members!);

      // Summary should have name and kind but no signature
      var firstMember = members[0]!;
      var name = firstMember.GetStringOrNull("name");
      var kind = firstMember.GetStringOrNull("kind");
      Assert.False(string.IsNullOrEmpty(name), "Member should have a name");
      Assert.False(string.IsNullOrEmpty(kind), "Member should have a kind");
      // Summary verbosity should NOT include signature
      Assert.Null(firstMember["signature"]);
   }
}
