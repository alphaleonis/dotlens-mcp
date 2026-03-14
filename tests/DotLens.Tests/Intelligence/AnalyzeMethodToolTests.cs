using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Intelligence;
using Xunit;

namespace DotLens.Tests.Intelligence;

[Collection("TestFixtures")]
public class AnalyzeMethodToolTests : TestFixtureTestBase
{
   public AnalyzeMethodToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private AnalyzeMethodTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      var symbolLookup = new SymbolLookupService(Workspace);
      _tool = new AnalyzeMethodTool(Workspace, symbolLookup);
   }

   private static LookupByName ByName(string name) => new() { Name = name };

   [Fact]
   public async Task AnalyzeMethod_ByName_ReturnsSignatureAndCallers()
   {
      var result = await _tool.AnalyzeMethodAsync(
         ByName("SampleClass.Calculate"),
         includeCalls: IncludeCalls.Incoming,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("Calculate", data.GetStringOrNull("methodName"));
      Assert.Equal("CoreTestProject.Types.SampleClass", data.GetStringOrNull("typeName"));

      Assert.Equal("public int Calculate(int a, int b)", data.GetString("signature"));

      // Calculate has no callers in the test fixtures
      Assert.Equal(0, data.GetInt("callerCount"));
      // location should point to the method definition in Classes.cs
      var location = data.GetObject("location");
      Assert.Contains("Classes.cs", location.GetString("filePath"));
      Assert.True(location.GetInt("line") > 0, "Location should have positive line number");
   }

   [Fact]
   public async Task AnalyzeMethod_WithAsyncMethod_SignatureContainsTaskReturnType()
   {
      var result = await _tool.AnalyzeMethodAsync(
         ByName("AsyncService.DoWorkAsync"),
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("DoWorkAsync", data.GetStringOrNull("methodName"));
      // Async keyword is implementation detail, not in signature - just the return type
      Assert.Equal("public Task DoWorkAsync()", data.GetString("signature"));
   }

   [Fact]
   public async Task AnalyzeMethod_WithOutgoingCalls_ReturnsOutgoingCallInfo()
   {
      // Calculate is a simple method with no outgoing calls (just returns a + b)
      var result = await _tool.AnalyzeMethodAsync(
         ByName("SampleClass.Calculate"),
         includeCalls: IncludeCalls.Outgoing,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);

      // Calculate has no outgoing calls (just returns a + b)
      Assert.Equal(0, data.GetInt("outgoingCallCount"));
      var outgoingCalls = data.GetArray("outgoingCalls");
      Assert.Empty(outgoingCalls);
   }

   [Fact]
   public async Task AnalyzeMethod_SummaryVerbosity_ReturnsCountsOnly()
   {
      var result = await _tool.AnalyzeMethodAsync(
         ByName("SampleClass.Calculate"),
         includeCalls: IncludeCalls.Both,
         verbosity: ToolResultVerbosity.Summary,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      // Summary should have counts but no detailed lists
      // Calculate has no callers and no outgoing calls
      Assert.Equal(0, data.GetInt("callerCount"));
      Assert.Equal(0, data.GetInt("outgoingCallCount"));
      Assert.Null(data["callers"]);
      Assert.Null(data["outgoingCalls"]);
   }

   [Fact]
   public async Task AnalyzeMethod_CompactVerbosity_ReturnsDetailedLists()
   {
      var result = await _tool.AnalyzeMethodAsync(
         ByName("SampleClass.Calculate"),
         includeCalls: IncludeCalls.Both,
         verbosity: ToolResultVerbosity.Compact,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      // Compact should have all details (counts and arrays)
      // Calculate has no callers and no outgoing calls
      Assert.Equal(0, data.GetInt("callerCount"));
      Assert.Equal(0, data.GetInt("outgoingCallCount"));
      // Arrays should be present but empty
      Assert.Empty(data.GetArray("callers"));
      Assert.Empty(data.GetArray("outgoingCalls"));
   }

   [Fact]
   public async Task AnalyzeMethod_IncludeCallsNone_ReturnsNoCallInfo()
   {
      var result = await _tool.AnalyzeMethodAsync(
         ByName("SampleClass.Calculate"),
         includeCalls: IncludeCalls.None,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Null(data["callerCount"]);
      Assert.Null(data["callers"]);
      Assert.Null(data["outgoingCallCount"]);
      Assert.Null(data["outgoingCalls"]);
   }

   [Fact]
   public async Task AnalyzeMethod_StaticMethod_SignatureContainsStatic()
   {
      var result = await _tool.AnalyzeMethodAsync(
         ByName("SampleClass.Create"),
         includeCalls: IncludeCalls.Outgoing,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);

      Assert.Equal("Create", data.GetStringOrNull("methodName"));
      Assert.Equal("CoreTestProject.Types.SampleClass", data.GetStringOrNull("typeName"));

      Assert.Equal("public static SampleClass Create(int id)", data.GetString("signature"));

      // Create calls exactly one constructor: new(id)
      Assert.Equal(1, data.GetInt("outgoingCallCount"));
   }

   [Fact]
   public async Task AnalyzeMethod_MethodNotFound_ThrowsToolException()
   {
      var ex = await Assert.ThrowsAsync<DotLens.Infrastructure.ToolException>(() =>
         _tool.AnalyzeMethodAsync(
            ByName("SampleClass.NonExistentMethod"),
            cancellationToken: CT));

      Assert.Equal(DotLens.Infrastructure.ErrorCodes.SymbolNotFound, ex.Code);
   }
}
