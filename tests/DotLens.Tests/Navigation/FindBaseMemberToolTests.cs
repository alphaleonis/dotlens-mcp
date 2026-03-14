using System.Text.Json;
using System.Text.Json.Nodes;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Navigation;
using DotLens.Tools.Search;
using DotLens.Tools.Shared;
using Xunit;

namespace DotLens.Tests.Navigation;

[Collection("TestFixtures")]
public class FindBaseMemberToolTests : TestFixtureTestBase
{
   public FindBaseMemberToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private FindBaseMemberTool _tool = null!;
   private FindSymbolsTool _findSymbols = null!;

   public override async ValueTask InitializeAsync()
   {
      var symbolLookup = new SymbolLookupService(Workspace);
      _tool = new FindBaseMemberTool(Workspace, symbolLookup);
      _findSymbols = new FindSymbolsTool(Workspace);
   }

   private LookupByPosition CreateLookup(string filePath, int line, int column)
      => new() { FilePath = filePath, Line = line, Column = column };

   [Fact]
   public async Task FindBaseMember_RegularMethod_ReturnsNoBaseMember()
   {
      // Search for DoSomething which doesn't override anything
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "DoSomething", kinds: [SymbolKindFilter.Method], cancellationToken: CT);

      var data = GetData(searchResult);
      var symbols = data["items"]!.AsArray();
      Assert.NotEmpty(symbols);

      var method = symbols[0]!;
      var location = method["location"]!;
      var relativePath = location.GetStringOrNull("filePath")!;
      var filePath = GetAbsolutePath(relativePath!);
      var line = location.GetIntOrNull("line")!.Value;
      var column = location.GetIntOrNull("column")!.Value;

      var result = await _tool.FindBaseMemberAsync(CreateLookup(filePath, line, column), cancellationToken: CT);

      var json = JsonNode.Parse(JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase))!;
      Assert.NotNull(json["data"]);  // Success indicated by presence of data
      var resultData = json["data"]!;
      Assert.Contains("does not override", resultData.GetStringOrNull("message") ?? "");
   }

   [Fact]
   public async Task FindBaseMember_OverrideMethod_FindsBaseMethod()
   {
      // Search for VirtualMethod in ConcreteClass which overrides AbstractBase.VirtualMethod
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "VirtualMethod", kinds: [SymbolKindFilter.Method], cancellationToken: CT);

      var data = GetData(searchResult);
      var symbols = data["items"]!.AsArray();

      // Find the override in ConcreteClass
      var overrideMethod = symbols.FirstOrDefault(s =>
      {
         var containingType = s!.GetStringOrNull("containingType") ?? "";
         return containingType.Contains("ConcreteClass");
      });

      // Fail fast if fixture is missing expected data
      Assert.NotNull(overrideMethod);

      var location = overrideMethod!["location"]!;
      var relativePath = location.GetStringOrNull("filePath")!;
      var filePath = GetAbsolutePath(relativePath!);
      var line = location.GetIntOrNull("line")!.Value;
      var column = location.GetIntOrNull("column")!.Value;

      var result = await _tool.FindBaseMemberAsync(CreateLookup(filePath, line, column), cancellationToken: CT);

      var json = JsonNode.Parse(JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase))!;
      Assert.NotNull(json["data"]);  // Success indicated by presence of data

      var resultData = json["data"]!;
      // Should find the base method in AbstractBase
      var overriddenMethod = resultData["overriddenMember"];
      Assert.NotNull(overriddenMethod);
      Assert.Contains("AbstractBase", overriddenMethod.GetStringOrNull("containingType")!);
   }

   [Fact]
   public async Task FindBaseMember_InterfaceImplementation_ReturnsImplementedInterface()
   {
      // Search for Execute method in BasicServiceImpl which implements IBasicService
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "Execute", kinds: [SymbolKindFilter.Method], cancellationToken: CT);

      var data = GetData(searchResult);
      var symbols = data["items"]!.AsArray();

      var implMethod = symbols.FirstOrDefault(s =>
      {
         var containingType = s!.GetStringOrNull("containingType") ?? "";
         return containingType.Contains("BasicServiceImpl");
      });

      // Fail fast if fixture is missing expected data
      Assert.NotNull(implMethod);

      var location = implMethod!["location"]!;
      var relativePath = location.GetStringOrNull("filePath")!;
      var filePath = GetAbsolutePath(relativePath!);
      var line = location.GetIntOrNull("line")!.Value;
      var column = location.GetIntOrNull("column")!.Value;

      var result = await _tool.FindBaseMemberAsync(CreateLookup(filePath, line, column), cancellationToken: CT);

      var json = JsonNode.Parse(JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase))!;
      Assert.NotNull(json["data"]);  // Success indicated by presence of data

      var resultData = json["data"]!;
      var implicitImpls = resultData["implicitInterfaceImplementations"]?.AsArray();
      Assert.NotNull(implicitImpls);
      Assert.True(implicitImpls.Count > 0, "Expected at least one implicit interface implementation");
      var firstImpl = implicitImpls[0]!;
      Assert.Contains("IBasicService", firstImpl.GetStringOrNull("containingType")!);
   }

   [Fact]
   public async Task FindBaseMember_InvalidPosition_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      await AssertThrowsToolExceptionAsync(
         () => _tool.FindBaseMemberAsync(CreateLookup(filePath, 99999, 1), cancellationToken: CT),
         "INVALID_PARAMETER");
   }

   [Fact]
   public async Task FindBaseMember_NonMethodSymbol_ThrowsToolException()
   {
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "SampleClass", kinds: [SymbolKindFilter.Class], cancellationToken: CT);

      var data = GetData(searchResult);
      var symbols = data["items"]!.AsArray();
      Assert.NotEmpty(symbols);

      var classSymbol = symbols[0]!;
      var location = classSymbol["location"]!;
      var relativePath = location.GetStringOrNull("filePath")!;
      var filePath = GetAbsolutePath(relativePath!);
      var line = location.GetIntOrNull("line")!.Value;
      var column = location.GetIntOrNull("column")!.Value;

      var ex = await AssertThrowsToolExceptionAsync(
         () => _tool.FindBaseMemberAsync(CreateLookup(filePath, line, column), cancellationToken: CT));
      // SymbolLookupService filter failure returns SymbolNotFound; FindBaseMemberTool guard returns NotAMethod
      Assert.True(
         ex.Code == ErrorCodes.SymbolNotFound || ex.Code == ErrorCodes.NotAMethod,
         $"Expected SymbolNotFound or NotAMethod, got {ex.Code}: {ex.Message}");
   }

   [Fact]
   public async Task FindBaseMember_FileNotFound_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.FindBaseMemberAsync(CreateLookup("NonExistent/file.cs", 1, 1), cancellationToken: CT),
         "FILE_NOT_IN_SOLUTION");
   }

   [Fact]
   public async Task FindBaseMember_ByName_FindsMethod()
   {
      // Test the new name-based lookup
      var result = await _tool.FindBaseMemberAsync(
         new LookupByName { Name = "ConcreteClass.VirtualMethod" },
         cancellationToken: CT);

      var json = JsonNode.Parse(JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase))!;
      Assert.NotNull(json["data"]);

      var resultData = json["data"]!;
      var member = resultData["member"]!;
      Assert.Equal("VirtualMethod", member.GetStringOrNull("name")!);
   }

   [Fact]
   public async Task FindBaseMember_SummaryVerbosity_ReturnsCountsOnly()
   {
      // Test summary verbosity returns counts but no detailed lists
      var result = await _tool.FindBaseMemberAsync(
         new LookupByName { Name = "BasicServiceImpl.Execute" },
         verbosity: ToolResultVerbosity.Summary,
         cancellationToken: CT);

      var json = JsonNode.Parse(JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase))!;
      Assert.NotNull(json["data"]);

      var resultData = json["data"]!;
      // Should have counts
      Assert.NotNull(resultData["implicitInterfaceCount"]);
      // Lists should be null at summary verbosity
      Assert.Null(resultData["implicitInterfaceImplementations"]);
   }

   [Fact]
   public async Task FindBaseMember_OverrideEvent_FindsBaseEvent()
   {
      // Test override event finding base event
      var result = await _tool.FindBaseMemberAsync(
         new LookupByName { Name = "ConcreteEventSource.StateChanged" },
         cancellationToken: CT);

      var json = JsonNode.Parse(JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase))!;
      Assert.NotNull(json["data"]);

      var resultData = json["data"]!;
      // Should find the base event in EventSourceBase
      var overriddenMember = resultData["overriddenMember"];
      Assert.NotNull(overriddenMember);
      Assert.Contains("EventSourceBase", overriddenMember.GetStringOrNull("containingType")!);
   }

   [Fact]
   public async Task FindBaseMember_EventImplementsInterface_FindsInterfaceEvent()
   {
      // Test event implementing interface
      var result = await _tool.FindBaseMemberAsync(
         new LookupByName { Name = "ConcreteEventSource.EventRaised" },
         cancellationToken: CT);

      var json = JsonNode.Parse(JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase))!;
      Assert.NotNull(json["data"]);

      var resultData = json["data"]!;
      var implicitImpls = resultData["implicitInterfaceImplementations"]?.AsArray();
      Assert.NotNull(implicitImpls);
      Assert.True(implicitImpls.Count > 0, "Expected at least one implicit interface implementation");
      var firstImpl = implicitImpls[0]!;
      Assert.Contains("IEventSource", firstImpl.GetStringOrNull("containingType")!);
   }

   [Fact]
   public async Task FindBaseMember_AbstractEventOverride_FindsAbstractEvent()
   {
      // Test override of abstract event
      var result = await _tool.FindBaseMemberAsync(
         new LookupByName { Name = "ConcreteEventSource.ActionRequired" },
         cancellationToken: CT);

      var json = JsonNode.Parse(JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase))!;
      Assert.NotNull(json["data"]);

      var resultData = json["data"]!;
      // Should find the abstract base event in EventSourceBase
      var overriddenMember = resultData["overriddenMember"];
      Assert.NotNull(overriddenMember);
      Assert.Contains("EventSourceBase", overriddenMember.GetStringOrNull("containingType")!);
   }
}
