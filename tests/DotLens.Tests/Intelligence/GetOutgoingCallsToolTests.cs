using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Intelligence;
using DotLens.Tools.Shared;
using Xunit;

namespace DotLens.Tests.Intelligence;

[Collection("TestFixtures")]
public class GetOutgoingCallsToolTests : TestFixtureTestBase
{
   public GetOutgoingCallsToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetOutgoingCallsTool _tool = null!;
   private SymbolLookupService _symbolLookup = null!;

   public override async ValueTask InitializeAsync()
   {
      _symbolLookup = new SymbolLookupService(Workspace);
      _tool = new GetOutgoingCallsTool(Workspace, _symbolLookup);
   }

   [Fact]
   public async Task GetOutgoingCalls_ByPosition_ReturnsCalledMethods()
   {
      // Find a method with outgoing calls using position lookup
      var target = new LookupByPosition
      {
         FilePath = GetCoreTestProjectPath("Patterns/AsyncPatterns.cs"),
         Line = 37, // Inside ProcessSequentialAsync (var step1 = await CalculateAsync(input);)
         Column = 10
      };

      var result = await _tool.GetOutgoingCallsAsync(target, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      // ProcessSequentialAsync calls only CalculateAsync (3 call sites, but 1 unique method)
      Assert.Equal(1, data.GetInt("outgoingCallCount"));
      var calls = data.GetArray("outgoingCalls");
      var callNames = calls.Select(c => c.GetString("name")).ToList();
      Assert.Equivalent(new[] { "CalculateAsync" }, callNames);
   }

   [Fact]
   public async Task GetOutgoingCalls_ByName_ReturnsCalledMethods()
   {
      // Use name-based lookup
      var target = new LookupByName { Name = "AsyncService.ProcessSequentialAsync" };

      var result = await _tool.GetOutgoingCallsAsync(target, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("ProcessSequentialAsync", data.GetString("methodName"));
      // ProcessSequentialAsync calls only CalculateAsync (3 call sites, but 1 unique method)
      var calls = data.GetArray("outgoingCalls");
      var callNames = calls.Select(c => c.GetString("name")).ToList();
      Assert.Equivalent(new[] { "CalculateAsync" }, callNames);
   }

   [Fact]
   public async Task GetOutgoingCalls_WithSummaryVerbosity_ReturnsCountOnly()
   {
      var target = new LookupByName { Name = "AsyncService.ProcessSequentialAsync" };

      var result = await _tool.GetOutgoingCallsAsync(target, ToolResultVerbosity.Summary, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      // ProcessSequentialAsync calls only CalculateAsync
      Assert.Equal(1, data.GetInt("outgoingCallCount"));
      Assert.Null(data["outgoingCalls"]); // No details in summary mode
   }

   [Fact]
   public async Task GetOutgoingCalls_WithFullVerbosity_IncludesLocations()
   {
      var target = new LookupByName { Name = "AsyncService.ProcessSequentialAsync" };

      var result = await _tool.GetOutgoingCallsAsync(target, ToolResultVerbosity.Full, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var calls = data.GetArray("outgoingCalls");

      // Full verbosity should include locations for source symbols
      // ProcessSequentialAsync calls methods in the same file, so they should have locations
      var callWithLocation = calls.FirstOrDefault(c => c["location"] != null);
      Assert.NotNull(callWithLocation); // Should have at least one call with location
      var location = callWithLocation.GetObject("location");
      Assert.Contains("AsyncPatterns.cs", location.GetString("filePath"));
      Assert.True(location.GetInt("line") > 0, "Call location should have positive line number");
   }

   [Fact]
   public async Task GetOutgoingCalls_FileNotFound_ThrowsToolException()
   {
      var target = new LookupByPosition
      {
         FilePath = "NonExistent/file.cs",
         Line = 10,
         Column = 1
      };

      var ex = await Assert.ThrowsAsync<ToolException>(() =>
         _tool.GetOutgoingCallsAsync(target, cancellationToken: CT));

      Assert.Equal(ErrorCodes.FileNotInSolution, ex.Code);
   }

   [Fact]
   public async Task GetOutgoingCalls_SymbolNotFound_ThrowsToolException()
   {
      var target = new LookupByName { Name = "NonExistent.Method" };

      var ex = await Assert.ThrowsAsync<ToolException>(() =>
         _tool.GetOutgoingCallsAsync(target, cancellationToken: CT));

      Assert.Equal(ErrorCodes.SymbolNotFound, ex.Code);
   }

   [Fact]
   public async Task GetOutgoingCalls_DetectsEventSubscriptions()
   {
      // EventSubscriber constructor subscribes to events (lines 114-122)
      // _publisher.StatusChanged += OnStatusChanged;
      // _publisher.DataReceived += OnDataReceived;
      // _publisher.MessageSent += OnMessageSent;
      var target = new LookupByPosition
      {
         FilePath = GetCoreTestProjectPath("Patterns/EventsAndDelegates.cs"),
         Line = 119, // Inside constructor, on event subscription line
         Column = 10
      };

      var result = await _tool.GetOutgoingCallsAsync(target, ToolResultVerbosity.Full, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var calls = data.GetArray("outgoingCalls");

      // Should detect event subscriptions as outgoing calls (kind is lowercase "event")
      var eventCalls = calls.Where(c =>
         string.Equals(c.GetStringOrNull("kind"), "event", StringComparison.OrdinalIgnoreCase)).ToList();

      // Verify we found exactly the 3 expected events
      var eventNames = eventCalls.Select(c => c.GetString("name")).ToList();
      Assert.Equivalent(new[] { "StatusChanged", "DataReceived", "MessageSent" }, eventNames);
   }
}
