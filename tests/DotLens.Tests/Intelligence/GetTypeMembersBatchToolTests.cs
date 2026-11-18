using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Intelligence;
using Xunit;

namespace DotLens.Tests.Intelligence;

[Collection("TestFixtures")]
public class GetTypeMembersBatchToolTests : TestFixtureTestBase
{
   public GetTypeMembersBatchToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetTypeMembersBatchTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      _tool = new GetTypeMembersBatchTool(Workspace, new SymbolLookupService(Workspace));
   }

   [Fact]
   public async Task GetTypeMembersBatch_ValidTypes_ReturnsMembers()
   {
      var typeNames = new List<string> { "SampleClass", "AsyncService" };

      var result = await _tool.GetTypeMembersBatchAsync(typeNames, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal(2, data.GetIntOrNull("requested"));
      Assert.Equal(2, data.GetIntOrNull("succeeded"));
      Assert.Equal(0, data.GetIntOrNull("failed"));

      // Verify actual content
      var results = data.GetArrayOrNull("results");
      Assert.NotNull(results);
      Assert.Equal(2, results.Count);

      // Verify exact type names are returned
      var typeNamesReturned = results.Select(r => r.GetString("typeName")).ToList();
      Assert.Equivalent(
         new[] { "CoreTestProject.Types.SampleClass", "CoreTestProject.Patterns.AsyncService" },
         typeNamesReturned);
   }

   [Fact]
   public async Task GetTypeMembersBatch_MixedValidInvalid_ReturnsPartialResults()
   {
      var typeNames = new List<string> { "SampleClass", "NonexistentType" };

      var result = await _tool.GetTypeMembersBatchAsync(typeNames, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal(2, data.GetIntOrNull("requested"));
      Assert.Equal(1, data.GetIntOrNull("succeeded"));
      Assert.Equal(1, data.GetIntOrNull("failed"));

      // Verify the error contains the missing type name
      var errors = data.GetArrayOrNull("errors");
      Assert.NotNull(errors);
      Assert.Single(errors);
      Assert.Equal("NonexistentType", errors[0]?.GetStringOrNull("typeName"));
      Assert.Contains("not found", errors[0]?.GetStringOrNull("error"), StringComparison.OrdinalIgnoreCase);
   }

   [Fact]
   public async Task GetTypeMembersBatch_FilterByMemberKind_FiltersResults()
   {
      var typeNames = new List<string> { "SampleClass" };

      var result = await _tool.GetTypeMembersBatchAsync(typeNames, memberKind: MemberKindFilter.Method, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var results = data.GetArrayOrNull("results");
      Assert.NotNull(results);
      Assert.Single(results);

      // Verify members are actually filtered - should only contain methods
      var members = results[0].GetArray("members");
      Assert.NotEmpty(members);

      // All returned members should be methods
      foreach (var member in members)
      {
         Assert.Equal("method", member.GetString("kind"), ignoreCase: true);
      }
      // Verify some expected methods are returned
      var methodNames = members.Select(m => m.GetString("name")).ToList();
      Assert.Contains("Calculate", methodNames);
      Assert.Contains("DoSomething", methodNames);
   }

   [Fact]
   public async Task GetTypeMembersBatch_FilterByProperty_ReturnsOnlyProperties()
   {
      var typeNames = new List<string> { "SampleClass" };

      var result = await _tool.GetTypeMembersBatchAsync(typeNames, memberKind: MemberKindFilter.Property, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var results = data.GetArrayOrNull("results");
      Assert.NotNull(results);
      Assert.Single(results);

      var members = results[0].GetArray("members");
      Assert.NotEmpty(members);

      // All returned members should be properties
      foreach (var member in members)
      {
         Assert.Equal("property", member.GetString("kind"), ignoreCase: true);
      }
      // Verify expected properties are returned
      var propertyNames = members.Select(m => m.GetString("name")).ToList();
      Assert.Contains("Id", propertyNames);
      Assert.Contains("Name", propertyNames);
   }

   [Fact]
   public async Task GetTypeMembersBatch_FilterByField_ReturnsOnlyFields()
   {
      var typeNames = new List<string> { "SampleClass" };

      var result = await _tool.GetTypeMembersBatchAsync(typeNames, memberKind: MemberKindFilter.Field, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var results = data.GetArrayOrNull("results");
      Assert.NotNull(results);
      Assert.Single(results);

      var members = results[0].GetArray("members");
      Assert.NotEmpty(members);

      // All returned members should be fields
      foreach (var member in members)
      {
         Assert.Equal("field", member.GetString("kind"), ignoreCase: true);
      }
      // Verify expected fields are returned
      var fieldNames = members.Select(m => m.GetString("name")).ToList();
      Assert.Contains("_privateField", fieldNames);
   }

   [Fact]
   public async Task GetTypeMembersBatch_FilterByEvent_ReturnsOnlyEvents()
   {
      var typeNames = new List<string> { "EventPublisher" };

      var result = await _tool.GetTypeMembersBatchAsync(typeNames, memberKind: MemberKindFilter.Event, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var results = data.GetArrayOrNull("results");
      Assert.NotNull(results);
      Assert.Single(results);

      var members = results[0].GetArray("members");
      Assert.NotEmpty(members);

      // All returned members should be events
      foreach (var member in members)
      {
         Assert.Equal("event", member.GetString("kind"), ignoreCase: true);
      }
      // EventPublisher has exactly 3 events
      var eventNames = members.Select(m => m.GetString("name")).ToList();
      Assert.Equivalent(new[] { "StatusChanged", "DataReceived", "MessageSent" }, eventNames);
   }

   [Fact]
   public async Task GetTypeMembersBatch_EmptyList_ReturnsEmptyResults()
   {
      var result = await _tool.GetTypeMembersBatchAsync(new List<string>(), cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal(0, data.GetIntOrNull("requested"));
      Assert.Equal(0, data.GetIntOrNull("succeeded"));
      Assert.Equal(0, data.GetIntOrNull("failed"));
   }

   [Fact]
   public async Task GetTypeMembersBatch_WithInterface_ReturnsMembers()
   {
      var typeNames = new List<string> { "IBasicService" };

      var result = await _tool.GetTypeMembersBatchAsync(typeNames, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal(1, data.GetIntOrNull("succeeded"));

      // Verify interface members are returned
      var results = data.GetArray("results");
      Assert.Single(results);

      var members = results[0].GetArray("members");
      // IBasicService has ExecuteAsync and Calculate
      var memberNames = members.Select(m => m.GetString("name")).ToList();
      Assert.Equivalent(new[] { "ExecuteAsync", "Calculate" }, memberNames);
   }

   [Fact]
   public async Task GetTypeMembersBatch_MaxResultsPerType_LimitsMembers()
   {
      var typeNames = new List<string> { "SampleClass" };

      var result = await _tool.GetTypeMembersBatchAsync(typeNames, maxResultsPerType: 2, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var results = data.GetArray("results");
      Assert.Single(results);

      var memberCount = results[0].GetInt("memberCount");
      Assert.True(memberCount <= 2, $"Expected at most 2 members due to maxResultsPerType=2, got {memberCount}");
   }

   [Fact]
   public async Task GetTypeMembersBatch_IncludeInherited_IncludesBaseMembers()
   {
      // DerivedClass inherits from ConcreteClass (which inherits from AbstractBase)
      var typeNames = new List<string> { "DerivedClass" };

      var resultWithoutInherited = await _tool.GetTypeMembersBatchAsync(typeNames, includeInherited: false, cancellationToken: CT);
      var resultWithInherited = await _tool.GetTypeMembersBatchAsync(typeNames, includeInherited: true, cancellationToken: CT);

      AssertSuccess(resultWithoutInherited);
      AssertSuccess(resultWithInherited);

      var dataWithout = GetData(resultWithoutInherited);
      var dataWith = GetData(resultWithInherited);

      var resultsWithout = dataWithout.GetArray("results");
      var resultsWith = dataWith.GetArray("results");
      Assert.Single(resultsWithout);
      Assert.Single(resultsWith);

      var countWithout = resultsWithout[0].GetInt("memberCount");
      var countWith = resultsWith[0].GetInt("memberCount");

      // With inherited should have MORE members (DerivedClass inherits from ConcreteClass which has members)
      Assert.True(countWith > countWithout, $"Expected inherited count ({countWith}) > non-inherited count ({countWithout})");
   }
}
