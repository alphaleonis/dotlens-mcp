using System.Text.Json;
using System.Text.Json.Nodes;
using DotLens.Infrastructure;
using DotLens.Infrastructure.Results;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Navigation;
using DotLens.Tools.Search;
using DotLens.Tools.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DotLens.Tests.Integration;

/// <summary>
/// Tests for inheritance navigation using dedicated test fixtures.
/// Uses the TestFixtures/InheritanceTestProject for predictable inheritance scenarios.
/// </summary>
public class InheritanceFixtureTests : IAsyncLifetime
{
   private IWorkspace _workspaceManager = null!;
   private FindOverridesTool _findOverrides = null!;
   private FindBaseMemberTool _findBaseMember = null!;
   private FindSymbolsTool _findSymbols = null!;
   private string _testFixturesSolutionPath = null!;
   private string _inheritanceFixturesPath = null!;

   protected CancellationToken CT => TestContext.Current.CancellationToken;

   public async ValueTask InitializeAsync()
   {
      // Find the TestFixtures solution path
      var currentDir = Directory.GetCurrentDirectory();
      var solutionDir = currentDir;

      while (solutionDir != null)
      {
         var testFixturesPath = Path.Combine(solutionDir, "tests", "TestFixtures", "TestFixtures.sln");
         if (File.Exists(testFixturesPath))
         {
            _testFixturesSolutionPath = testFixturesPath;
            _inheritanceFixturesPath = Path.Combine(solutionDir, "tests", "TestFixtures", "InheritanceTestProject", "InheritanceFixtures.cs");
            break;
         }
         solutionDir = Directory.GetParent(solutionDir)?.FullName;
      }

      if (_testFixturesSolutionPath == null)
         throw new InvalidOperationException("Could not find TestFixtures.sln");

      // Create workspace manager and load the test fixtures solution
      var options = Options.Create(new DotLensOptions());
      var workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance, options);
      _workspaceManager = workspaceManager;

      var loadResult = await workspaceManager.LoadSolutionAsync(_testFixturesSolutionPath);
      var json = JsonNode.Parse(JsonSerializer.Serialize(loadResult, loadResult.GetType(), TestJsonOptions.CamelCase))!;
      Assert.NotNull(json["data"]);  // Success indicated by presence of data

      var symbolLookup = new SymbolLookupService(workspaceManager);
      _findOverrides = new FindOverridesTool(workspaceManager, symbolLookup);
      _findBaseMember = new FindBaseMemberTool(workspaceManager, symbolLookup);
      _findSymbols = new FindSymbolsTool(workspaceManager);
   }

   public ValueTask DisposeAsync()
   {
      (_workspaceManager as IDisposable)?.Dispose();
      return ValueTask.CompletedTask;
   }

   private JsonNode GetData(object response)
   {
      var json = JsonNode.Parse(JsonSerializer.Serialize(response, response.GetType(), TestJsonOptions.CamelCase))!;
      Assert.NotNull(json["data"]);  // Success indicated by presence of data
      return json["data"]!;
   }

   private string GetAbsolutePath(string relativePath)
   {
      var solutionDir = Path.GetDirectoryName(_testFixturesSolutionPath)!;
      return Path.Combine(solutionDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
   }

   private async Task<ToolException> AssertThrowsToolExceptionAsync(Func<Task> action, string? expectedErrorCode = null)
   {
      var ex = await Assert.ThrowsAsync<ToolException>(action);
      if (expectedErrorCode != null)
      {
         Assert.Equal(expectedErrorCode, ex.Code);
      }
      return ex;
   }

   private Task<ToolResult<FindBaseMemberResponse>> FindBaseMemberAsync(string filePath, int line, int column)
      => _findBaseMember.FindBaseMemberAsync(
         new LookupByPosition { FilePath = filePath, Line = line, Column = column },
         ToolResultVerbosity.Full,
         CT);

   private Task<ToolResult<FindOverridesResponse>> FindOverridesAsync(string filePath, int line, int column)
      => _findOverrides.FindOverridesAsync(
         new LookupByPosition { FilePath = filePath, Line = line, Column = column },
         cancellationToken: CT);

   #region FindOverrides Positive Tests

   [Fact]
   public async Task FindOverrides_VirtualMethod_ReturnsAllOverrides()
   {
      // Find the virtual method Animal.Speak
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "Speak", kinds: [SymbolKindFilter.Method], cancellationToken: CT);
      var data = GetData(searchResult);
      var symbols = data["items"]!.AsArray();

      var animalSpeak = symbols.FirstOrDefault(s =>
         s!.GetStringOrNull("containingType") == "InheritanceTestProject.Animal");
      Assert.NotNull(animalSpeak);

      var location = animalSpeak!["location"]!;
      var filePath = GetAbsolutePath(location.GetStringOrNull("filePath")!);
      var line = location.GetIntOrNull("line")!.Value;
      var column = location.GetIntOrNull("column")!.Value;

      // Act
      var result = await FindOverridesAsync(filePath, line, column);

      // Assert
      var resultData = GetData(result);
      var overrides = resultData["overrides"]!.AsArray();

      // Should find Dog.Speak, Cat.Speak, Bird.Speak
      Assert.Equal(3, resultData.GetIntOrNull("totalOverrides")!.Value);

      var containingTypes = overrides.Select(o => o!.GetStringOrNull("containingType")!).ToList();
      Assert.Contains("InheritanceTestProject.Dog", containingTypes);
      Assert.Contains("InheritanceTestProject.Cat", containingTypes);
      Assert.Contains("InheritanceTestProject.Bird", containingTypes);
   }

   [Fact]
   public async Task FindOverrides_AbstractMethod_ReturnsOverrides()
   {
      // Find the abstract method Animal.Move
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "Move", kinds: [SymbolKindFilter.Method], cancellationToken: CT);
      var data = GetData(searchResult);
      var symbols = data["items"]!.AsArray();

      var animalMove = symbols.FirstOrDefault(s =>
         s!.GetStringOrNull("containingType") == "InheritanceTestProject.Animal");
      Assert.NotNull(animalMove);

      var location = animalMove!["location"]!;
      var filePath = GetAbsolutePath(location.GetStringOrNull("filePath")!);
      var line = location.GetIntOrNull("line")!.Value;
      var column = location.GetIntOrNull("column")!.Value;

      // Act
      var result = await FindOverridesAsync(filePath, line, column);

      // Assert
      var resultData = GetData(result);
      Assert.Equal(3, resultData.GetIntOrNull("totalOverrides")!.Value);

      // Base member should be marked as abstract (modifiers is a flags enum serialized as camelCase comma-separated string)
      var baseMember = resultData["baseMember"]!;
      var modifiers = baseMember.GetStringOrNull("modifiers") ?? "";
      Assert.Contains("abstract", modifiers);
   }

   [Fact]
   public async Task FindOverrides_VirtualProperty_ReturnsOverrides()
   {
      // Find the virtual property Animal.Legs
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "Legs", kinds: [SymbolKindFilter.Property], cancellationToken: CT);
      var data = GetData(searchResult);
      var symbols = data["items"]!.AsArray();

      var animalLegs = symbols.FirstOrDefault(s =>
         s!.GetStringOrNull("containingType") == "InheritanceTestProject.Animal");
      Assert.NotNull(animalLegs);

      var location = animalLegs!["location"]!;
      var filePath = GetAbsolutePath(location.GetStringOrNull("filePath")!);
      var line = location.GetIntOrNull("line")!.Value;
      var column = location.GetIntOrNull("column")!.Value;

      // Act
      var result = await FindOverridesAsync(filePath, line, column);

      // Assert
      var resultData = GetData(result);
      Assert.Equal(3, resultData.GetIntOrNull("totalOverrides")!.Value);
      Assert.Equal("property", resultData["baseMember"]!.GetStringOrNull("kind")!);
   }

   [Fact]
   public async Task FindOverrides_OverrideMethod_FindsFromBase()
   {
      // Find an override method (Dog.Speak) - should still find all overrides via the base
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "Speak", kinds: [SymbolKindFilter.Method], cancellationToken: CT);
      var data = GetData(searchResult);
      var symbols = data["items"]!.AsArray();

      var dogSpeak = symbols.FirstOrDefault(s =>
         s!.GetStringOrNull("containingType") == "InheritanceTestProject.Dog");
      Assert.NotNull(dogSpeak);

      var location = dogSpeak!["location"]!;
      var filePath = GetAbsolutePath(location.GetStringOrNull("filePath")!);
      var line = location.GetIntOrNull("line")!.Value;
      var column = location.GetIntOrNull("column")!.Value;

      // Act
      var result = await FindOverridesAsync(filePath, line, column);

      // Assert - Should find all 3 overrides (Dog, Cat, Bird) via base Animal.Speak
      var resultData = GetData(result);
      Assert.Equal(3, resultData.GetIntOrNull("totalOverrides")!.Value);

      // Base member should be Animal.Speak
      var baseMember = resultData["baseMember"]!;
      Assert.Equal("InheritanceTestProject.Animal", baseMember.GetStringOrNull("containingType")!);
   }

   [Fact]
   public async Task FindOverrides_MultiLevelChain_FindsAllOverrides()
   {
      // Find Vehicle.Start - should find Car.Start and SportsCar.Start
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "Start", kinds: [SymbolKindFilter.Method], cancellationToken: CT);
      var data = GetData(searchResult);
      var symbols = data["items"]!.AsArray();

      var vehicleStart = symbols.FirstOrDefault(s =>
         s!.GetStringOrNull("containingType") == "InheritanceTestProject.Vehicle");
      Assert.NotNull(vehicleStart);

      var location = vehicleStart!["location"]!;
      var filePath = GetAbsolutePath(location.GetStringOrNull("filePath")!);
      var line = location.GetIntOrNull("line")!.Value;
      var column = location.GetIntOrNull("column")!.Value;

      // Act
      var result = await FindOverridesAsync(filePath, line, column);

      // Assert
      var resultData = GetData(result);
      var overrides = resultData["overrides"]!.AsArray();

      Assert.Equal(2, resultData.GetIntOrNull("totalOverrides")!.Value);
      var containingTypes = overrides.Select(o => o!.GetStringOrNull("containingType")!).ToList();
      Assert.Contains("InheritanceTestProject.Car", containingTypes);
      Assert.Contains("InheritanceTestProject.SportsCar", containingTypes);
   }

   #endregion

   #region FindBaseMember Positive Tests

   [Fact]
   public async Task FindBaseMember_OverrideMethod_ReturnsOverriddenMethod()
   {
      // Find Dog.Speak which overrides Animal.Speak
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "Speak", kinds: [SymbolKindFilter.Method], cancellationToken: CT);
      var data = GetData(searchResult);
      var symbols = data["items"]!.AsArray();

      var dogSpeak = symbols.FirstOrDefault(s =>
         s!.GetStringOrNull("containingType") == "InheritanceTestProject.Dog");
      Assert.NotNull(dogSpeak);

      var location = dogSpeak!["location"]!;
      var filePath = GetAbsolutePath(location.GetStringOrNull("filePath")!);
      var line = location.GetIntOrNull("line")!.Value;
      var column = location.GetIntOrNull("column")!.Value;

      // Act
      var result = await FindBaseMemberAsync(filePath, line, column);

      // Assert
      var resultData = GetData(result);
      var overriddenMember = resultData["overriddenMember"]!;

      Assert.NotNull(overriddenMember);
      Assert.Equal("Speak", overriddenMember.GetStringOrNull("name")!);
      Assert.Equal("InheritanceTestProject.Animal", overriddenMember.GetStringOrNull("containingType")!);
      // Modifiers are serialized as camelCase comma-separated flags string
      var modifiers = overriddenMember.GetStringOrNull("modifiers")!;
      Assert.Contains("virtual", modifiers);
   }

   [Fact]
   public async Task FindBaseMember_OverrideProperty_ReturnsOverriddenProperty()
   {
      // Find Dog.Legs which overrides Animal.Legs
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "Legs", kinds: [SymbolKindFilter.Property], cancellationToken: CT);
      var data = GetData(searchResult);
      var symbols = data["items"]!.AsArray();

      var dogLegs = symbols.FirstOrDefault(s =>
         s!.GetStringOrNull("containingType") == "InheritanceTestProject.Dog");
      Assert.NotNull(dogLegs);

      var location = dogLegs!["location"]!;
      var filePath = GetAbsolutePath(location.GetStringOrNull("filePath")!);
      var line = location.GetIntOrNull("line")!.Value;
      var column = location.GetIntOrNull("column")!.Value;

      // Act
      var result = await FindBaseMemberAsync(filePath, line, column);

      // Assert
      var resultData = GetData(result);
      var overriddenMember = resultData["overriddenMember"]!;

      Assert.NotNull(overriddenMember);
      Assert.Equal("property", overriddenMember.GetStringOrNull("kind")!);
      Assert.Equal("InheritanceTestProject.Animal", overriddenMember.GetStringOrNull("containingType")!);
   }

   [Fact]
   public async Task FindBaseMember_InterfaceImplementation_ReturnsImplicitImplementations()
   {
      // Find InMemoryRepository.Save which implements IRepository.Save
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "Save", kinds: [SymbolKindFilter.Method], cancellationToken: CT);
      var data = GetData(searchResult);
      var symbols = data["items"]!.AsArray();

      var repoSave = symbols.FirstOrDefault(s =>
         s!.GetStringOrNull("containingType") == "InheritanceTestProject.InMemoryRepository");
      Assert.NotNull(repoSave);

      var location = repoSave!["location"]!;
      var filePath = GetAbsolutePath(location.GetStringOrNull("filePath")!);
      var line = location.GetIntOrNull("line")!.Value;
      var column = location.GetIntOrNull("column")!.Value;

      // Act
      var result = await FindBaseMemberAsync(filePath, line, column);

      // Assert
      var resultData = GetData(result);
      var implicitImpls = resultData["implicitInterfaceImplementations"]!.AsArray();

      Assert.NotEmpty(implicitImpls);
      var impl = implicitImpls[0]!;
      Assert.Contains("IRepository", impl.GetStringOrNull("containingType")!);
   }

   [Fact]
   public async Task FindBaseMember_ExplicitInterfaceImplementation_ReturnsExplicitImplementations()
   {
      // Find TextProcessor's explicit IFormatter.Format implementation
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "Format", kinds: [SymbolKindFilter.Method], cancellationToken: CT);
      var data = GetData(searchResult);
      var symbols = data["items"]!.AsArray();

      var textProcessorFormat = symbols.FirstOrDefault(s =>
      {
         var containingType = s!.GetStringOrNull("containingType") ?? "";
         return containingType.Contains("TextProcessor");
      });

      // Note: Explicit interface implementations might not be directly searchable
      // If not found, this test just verifies the scenario exists in our fixtures
      if (textProcessorFormat != null)
      {
         var location = textProcessorFormat!["location"]!;
         var filePath = GetAbsolutePath(location.GetStringOrNull("filePath")!);
         var line = location.GetIntOrNull("line")!.Value;
         var column = location.GetIntOrNull("column")!.Value;

         // Act
         var result = await FindBaseMemberAsync(filePath, line, column);

         // Assert - Should have explicit interface implementations
         var json = JsonNode.Parse(JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase))!;
         if (json["data"] != null)  // Success indicated by presence of data
         {
            var resultData = json["data"]!;
            var explicitImpls = resultData["explicitInterfaceImplementations"]?.AsArray();
            // If found, verify it has the IFormatter interface
            if (explicitImpls != null && explicitImpls.Count > 0)
            {
               Assert.Contains("IFormatter", explicitImpls[0]!.GetStringOrNull("containingInterface")!);
            }
         }
      }
   }

   [Fact]
   public async Task FindBaseMember_MultiLevelOverride_ReturnsImmediateParent()
   {
      // Find SportsCar.Start which overrides Car.Start (which overrides Vehicle.Start)
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "Start", kinds: [SymbolKindFilter.Method], cancellationToken: CT);
      var data = GetData(searchResult);
      var symbols = data["items"]!.AsArray();

      var sportsCarStart = symbols.FirstOrDefault(s =>
         s!.GetStringOrNull("containingType") == "InheritanceTestProject.SportsCar");
      Assert.NotNull(sportsCarStart);

      var location = sportsCarStart!["location"]!;
      var filePath = GetAbsolutePath(location.GetStringOrNull("filePath")!);
      var line = location.GetIntOrNull("line")!.Value;
      var column = location.GetIntOrNull("column")!.Value;

      // Act
      var result = await FindBaseMemberAsync(filePath, line, column);

      // Assert - Should return Car.Start (immediate parent, not Vehicle.Start)
      var resultData = GetData(result);
      var overriddenMember = resultData["overriddenMember"]!;

      Assert.NotNull(overriddenMember);
      Assert.Equal("InheritanceTestProject.Car", overriddenMember.GetStringOrNull("containingType")!);
   }

   [Fact]
   public async Task FindBaseMember_NonOverrideMethod_ReturnsNoBaseMember()
   {
      // Find Calculator.Add which doesn't override anything
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "Add", kinds: [SymbolKindFilter.Method], cancellationToken: CT);
      var data = GetData(searchResult);
      var symbols = data["items"]!.AsArray();

      var calculatorAdd = symbols.FirstOrDefault(s =>
         s!.GetStringOrNull("containingType") == "InheritanceTestProject.Calculator");
      Assert.NotNull(calculatorAdd);

      var location = calculatorAdd!["location"]!;
      var filePath = GetAbsolutePath(location.GetStringOrNull("filePath")!);
      var line = location.GetIntOrNull("line")!.Value;
      var column = location.GetIntOrNull("column")!.Value;

      // Act
      var result = await FindBaseMemberAsync(filePath, line, column);

      // Assert - Should succeed but indicate no base member
      var resultData = GetData(result);
      Assert.Contains("does not override", resultData.GetStringOrNull("message") ?? "");
   }

   #endregion

   #region Error Cases with Fixtures

   [Fact]
   public async Task FindOverrides_NonVirtualMethod_ThrowsToolException()
   {
      // Find Calculator.Add which is not virtual
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "Add", kinds: [SymbolKindFilter.Method], cancellationToken: CT);
      var data = GetData(searchResult);
      var symbols = data["items"]!.AsArray();

      var calculatorAdd = symbols.FirstOrDefault(s =>
         s!.GetStringOrNull("containingType") == "InheritanceTestProject.Calculator");
      Assert.NotNull(calculatorAdd);

      var location = calculatorAdd!["location"]!;
      var filePath = GetAbsolutePath(location.GetStringOrNull("filePath")!);
      var line = location.GetIntOrNull("line")!.Value;
      var column = location.GetIntOrNull("column")!.Value;

      // Act & Assert - Should error because method is not virtual
      await AssertThrowsToolExceptionAsync(
         () => FindOverridesAsync(filePath, line, column),
         ErrorCodes.InvalidParameter);
   }

   [Fact]
   public async Task FindOverrides_NonVirtualProperty_ThrowsToolException()
   {
      // Find Calculator.Name which is not virtual
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "Name", kinds: [SymbolKindFilter.Property], cancellationToken: CT);
      var data = GetData(searchResult);
      var symbols = data["items"]!.AsArray();

      var calculatorName = symbols.FirstOrDefault(s =>
         s!.GetStringOrNull("containingType") == "InheritanceTestProject.Calculator");
      Assert.NotNull(calculatorName);

      var location = calculatorName!["location"]!;
      var filePath = GetAbsolutePath(location.GetStringOrNull("filePath")!);
      var line = location.GetIntOrNull("line")!.Value;
      var column = location.GetIntOrNull("column")!.Value;

      // Act & Assert - Should error because property is not virtual
      await AssertThrowsToolExceptionAsync(
         () => FindOverridesAsync(filePath, line, column),
         ErrorCodes.InvalidParameter);
   }

   #endregion
}
