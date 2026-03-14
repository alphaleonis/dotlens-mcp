using DotLens.Infrastructure;
using DotLens.Tools.Search;
using DotLens.Tools.Shared;
using Xunit;

namespace DotLens.Tests.Search;

[Collection("TestFixtures")]
public class FindSymbolsToolTests : TestFixtureTestBase
{
   public FindSymbolsToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private FindSymbolsTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      _tool = new FindSymbolsTool(Workspace);
   }

   #region Name-based Search (formerly FindSymbolsTool)

   [Fact]
   public async Task FindSymbols_ByName_FindsTypes()
   {
      var result = await _tool.FindSymbolsAsync(name: "SampleClass", maxResults: 10, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      var hasMatch = items.Any(r =>
          r.GetStringOrNull("name")?.Contains("SampleClass") == true ||
          r.GetStringOrNull("fullyQualifiedName")?.Contains("SampleClass") == true);
      Assert.True(hasMatch, "Search should find symbols containing 'SampleClass'");
   }

   [Fact]
   public async Task FindSymbols_ByName_WithKindFilter_FiltersResults()
   {
      var result = await _tool.FindSymbolsAsync(name: "Get*", kinds: [SymbolKindFilter.Method], maxResults: 50, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      foreach (var symbol in items!)
      {
         Assert.Equal("method", symbol.GetStringOrNull("kind"));
      }
   }

   [Fact]
   public async Task FindSymbols_ByName_WithWildcard_FindsMultiple()
   {
      var result = await _tool.FindSymbolsAsync(name: "*Async", kinds: [SymbolKindFilter.Method], maxResults: 100, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      // Test fixtures contain multiple *Async methods
      Assert.True(items!.Count > 0, "Should find async methods in test fixtures");
   }

   [Fact]
   public async Task FindSymbols_ByName_FindsInterface()
   {
      var result = await _tool.FindSymbolsAsync(name: "IBasicService", kinds: [SymbolKindFilter.Interface], maxResults: 10, verbosity: ToolResultVerbosity.Full, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      var iface = items[0]!;
      Assert.Equal("IBasicService", iface.GetStringOrNull("name"));
      Assert.Contains("CoreTestProject.Types", iface.GetStringOrNull("fullyQualifiedName"));
   }

   [Fact]
   public async Task FindSymbols_ByName_FindsEnum()
   {
      var result = await _tool.FindSymbolsAsync(name: "Status", maxResults: 10, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);

      var hasStatusEnum = items.Any(r => r.GetStringOrNull("name") == "Status");
      Assert.True(hasStatusEnum, "Should find Status enum");
   }

   [Fact]
   public async Task FindSymbols_ByName_FindsRecord()
   {
      var result = await _tool.FindSymbolsAsync(name: "Person", maxResults: 10, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);

      var hasPerson = items.Any(r => r.GetStringOrNull("name") == "Person");
      Assert.True(hasPerson, "Should find Person record");
   }

   [Fact]
   public async Task FindSymbols_ByName_FindsGenericType()
   {
      var result = await _tool.FindSymbolsAsync(name: "Container", kinds: [SymbolKindFilter.Class], maxResults: 10, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);

      var hasContainer = items.Any(r => r.GetStringOrNull("name")?.Contains("Container") == true);
      Assert.True(hasContainer, "Should find Container generic class");
   }

   [Fact]
   public async Task FindSymbols_ByName_ReturnsPolymorphicResponse()
   {
      var result = await _tool.FindSymbolsAsync(name: "SampleClass", kinds: [SymbolKindFilter.Class], maxResults: 5, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      var typeSymbol = items[0]!;
      Assert.Equal("class", typeSymbol.GetStringOrNull("kind"));
   }

   [Fact]
   public async Task FindSymbols_ByName_MethodResponseIncludesSignature()
   {
      // Search for the DoWorkAsync method which has a unique signature in AsyncService
      var result = await _tool.FindSymbolsAsync(
         name: "DoWorkAsync",
         kinds: [SymbolKindFilter.Method],
         maxResults: 1,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArray("items");
      Assert.Single(items);

      var methodSymbol = items[0];
      Assert.Equal("method", methodSymbol.GetString("kind"));
      Assert.Equal("Task AsyncService.DoWorkAsync()", methodSymbol.GetString("signature"));
      Assert.Equal("System.Threading.Tasks.Task", methodSymbol.GetString("returnType"));
   }

   #endregion

   #region Property-based Search (formerly SemanticQueryTool)

   [Fact]
   public async Task FindSymbols_ByProperty_FindsAsyncMethods()
   {
      var result = await _tool.FindSymbolsAsync(modifiers: [ModifierFilter.Async], cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);
   }

   [Fact]
   public async Task FindSymbols_ByProperty_FiltersByAccessibility()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Method],
         accessibility: AccessibilityDto.Public,
         namespaceFilter: "CoreTestProject.Types",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 20,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      var classMethod = items!.FirstOrDefault(m =>
         m?.GetStringOrNull("declaration")?.StartsWith("public") == true);
      Assert.NotNull(classMethod);
   }

   [Fact]
   public async Task FindSymbols_FiltersByKind_Class()
   {
      var result = await _tool.FindSymbolsAsync(kinds: [SymbolKindFilter.Class], maxResults: 20, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      foreach (var symbol in items!)
      {
         Assert.Equal("class", symbol?.GetStringOrNull("kind"));
      }
   }

   [Fact]
   public async Task FindSymbols_FiltersByKind_Interface()
   {
      var result = await _tool.FindSymbolsAsync(kinds: [SymbolKindFilter.Interface], maxResults: 20, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      foreach (var symbol in items!)
      {
         Assert.Equal("interface", symbol?.GetStringOrNull("kind"));
      }
   }

   [Fact]
   public async Task FindSymbols_ClassFilter_IncludesRecordClasses()
   {
      // Records are now included in Class filter (they're classes with Record modifier)
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Class],
         namespaceFilter: "CoreTestProject.Types",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArray("items");
      Assert.NotEmpty(items);

      var names = items.Select(m => m.GetString("name")).ToList();
      // Person is a record class - should be included in Class filter
      Assert.Contains("Person", names);

      // Verify record classes have "record" in declaration and Record modifier
      var person = items.First(m => m.GetString("name") == "Person");
      var declaration = person.GetString("declaration");
      Assert.Contains("record", declaration);

      // Verify SymbolModifiers.Record flag is set
      var modifiersStr = person.GetStringOrNull("modifiers");
      Assert.NotNull(modifiersStr);
      var modifiers = Enum.Parse<SymbolModifiers>(modifiersStr, ignoreCase: true);
      Assert.True(modifiers.HasFlag(SymbolModifiers.Record));
   }

   [Fact]
   public async Task FindSymbols_FiltersByKind_Constructor()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Constructor],
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      foreach (var symbol in items!)
      {
         var kind = symbol?.GetStringOrNull("kind");
         Assert.Equal("constructor", kind);
      }
   }

   [Fact]
   public async Task FindSymbols_FiltersByNamespace()
   {
      var result = await _tool.FindSymbolsAsync(
         namespaceFilter: "CoreTestProject.Types",
         verbosity: ToolResultVerbosity.Full,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      foreach (var symbol in items!)
      {
         var ns = symbol?.GetStringOrNull("containingNamespace") ?? "";
         Assert.Contains("CoreTestProject.Types", ns);
      }
   }

   [Fact]
   public async Task FindSymbols_FiltersByNamespaceWildcard()
   {
      var result = await _tool.FindSymbolsAsync(
         namespaceFilter: "CoreTestProject.*",
         verbosity: ToolResultVerbosity.Full,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      foreach (var symbol in items!)
      {
         var ns = symbol?.GetStringOrNull("containingNamespace") ?? "";
         Assert.StartsWith("CoreTestProject", ns);
      }
   }

   [Fact]
   public async Task FindSymbols_FiltersByStatic()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Method],
         modifiers: [ModifierFilter.Static],
         verbosity: ToolResultVerbosity.Full,
         maxResults: 30,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      foreach (var symbol in items!)
      {
         var declaration = symbol?.GetStringOrNull("declaration");
         Assert.NotNull(declaration);
         Assert.Contains("static", declaration);
      }
   }

   [Fact]
   public async Task FindSymbols_FiltersByReturnType()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Method],
         returnType: "Task",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 30,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      foreach (var symbol in items!)
      {
         var declaration = symbol?.GetStringOrNull("declaration") ?? "";
         Assert.Contains("Task", declaration);
      }
   }

   [Fact]
   public async Task FindSymbols_FiltersByPropertyType()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Property],
         type: "string",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 30,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      foreach (var symbol in items!)
      {
         var declaration = symbol?.GetStringOrNull("declaration") ?? "";
         Assert.Contains("string", declaration, StringComparison.OrdinalIgnoreCase);
      }
   }

   [Fact]
   public async Task FindSymbols_FiltersByAttributes()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Class],
         attributes: ["ServiceAttribute"],
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);
   }

   #endregion

   #region Pagination and Verbosity

   [Fact]
   public async Task FindSymbols_RespectsMaxResults()
   {
      var result = await _tool.FindSymbolsAsync(accessibility: AccessibilityDto.Public, maxResults: 5, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);
      Assert.True(items!.Count <= 5);
   }

   [Fact]
   public async Task FindSymbols_Pagination_ReturnsHasMoreAndNextOffset()
   {
      var result1 = await _tool.FindSymbolsAsync(accessibility: AccessibilityDto.Public, maxResults: 3, offset: 0, cancellationToken: CT);

      AssertSuccess(result1);
      var data1 = GetData(result1);
      var hasMore = data1.GetBoolOrNull("hasMore") ?? false;
      Assert.True(hasMore, "Query should return more items than maxResults to test pagination");

      var nextOffset = data1.GetIntOrNull("nextOffset");
      Assert.NotNull(nextOffset);
      Assert.Equal(3, nextOffset);

      var result2 = await _tool.FindSymbolsAsync(accessibility: AccessibilityDto.Public, maxResults: 3, offset: nextOffset.Value, cancellationToken: CT);

      AssertSuccess(result2);
      var items2 = GetData(result2).GetArrayOrNull("items");
      Assert.NotNull(items2);
      Assert.NotEmpty(items2!);
   }

   [Fact]
   public async Task FindSymbols_SummaryVerbosity_ReturnsMinimalData()
   {
      var result = await _tool.FindSymbolsAsync(
         accessibility: AccessibilityDto.Public,
         verbosity: ToolResultVerbosity.Summary,
         maxResults: 5,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      var symbol = items![0];
      // Summary includes name and kind
      Assert.False(string.IsNullOrEmpty(symbol?.GetStringOrNull("name")), "Symbol should have a name");
      Assert.False(string.IsNullOrEmpty(symbol?.GetStringOrNull("kind")), "Symbol should have a kind");
      // Summary excludes declaration and fullyQualifiedName
      Assert.Null(symbol?["declaration"]);
      Assert.Null(symbol?["fullyQualifiedName"]);
   }

   [Fact]
   public async Task FindSymbols_CompactVerbosity_ExcludesDeclaration()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Method],
         verbosity: ToolResultVerbosity.Compact,
         maxResults: 5,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArray("items");
      Assert.NotEmpty(items);

      var symbol = items[0]!;
      // Compact includes location with file path and line
      var location = symbol.GetObject("location");
      Assert.False(string.IsNullOrEmpty(location.GetString("filePath")), "Location should have filePath");
      Assert.True(location.GetInt("line") > 0, "Location should have positive line number");

      // Compact excludes declaration and fullyQualifiedName
      Assert.Null(symbol["declaration"]);
      Assert.Null(symbol["fullyQualifiedName"]);
   }

   [Fact]
   public async Task FindSymbols_FullVerbosity_IncludesDeclaration()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Method],
         verbosity: ToolResultVerbosity.Full,
         maxResults: 5,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      var symbol = items![0];
      // Full verbosity includes fullyQualifiedName and declaration
      var fqn = symbol.GetString("fullyQualifiedName");
      Assert.Contains(".", fqn); // FQN should include namespace/type separator

      var declaration = symbol?.GetStringOrNull("declaration");
      Assert.False(string.IsNullOrEmpty(declaration), "Full verbosity should include declaration");
   }

   [Fact]
   public async Task FindSymbols_ReturnsSymbolLocation()
   {
      var result = await _tool.FindSymbolsAsync(kinds: [SymbolKindFilter.Class], maxResults: 5, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      var symbol = items![0];
      Assert.NotNull(symbol?["location"]);
      var location = symbol?["location"];
      Assert.NotNull(location?["filePath"]);
      Assert.NotNull(location?["line"]);
      Assert.NotNull(location?["column"]);
   }

   [Fact]
   public async Task FindSymbols_WithNoMatches_ReturnsEmptyResults()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Class],
         namespaceFilter: "NonExistent.Namespace.That.Does.Not.Exist",
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.Empty(items!);
   }

   #endregion

   #region Declaration Tests

   [Fact]
   public async Task FindSymbols_Declaration_PublicClass()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Class],
         namespaceFilter: "CoreTestProject.Types",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);

      var publicClass = items!.FirstOrDefault(m => m?.GetStringOrNull("name") == "SampleClass");
      Assert.NotNull(publicClass);
      var declaration = publicClass?.GetStringOrNull("declaration");
      Assert.NotNull(declaration);
      Assert.StartsWith("public", declaration);
      Assert.Contains("class", declaration);
      Assert.Contains("SampleClass", declaration);
   }

   [Fact]
   public async Task FindSymbols_Declaration_AbstractClass()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Class],
         namespaceFilter: "CoreTestProject.Types",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);

      var abstractClass = items!.FirstOrDefault(m => m?.GetStringOrNull("name") == "AbstractBase");
      Assert.NotNull(abstractClass);
      var declaration = abstractClass?.GetStringOrNull("declaration");
      Assert.NotNull(declaration);
      Assert.Contains("abstract", declaration);
      Assert.Contains("class", declaration);
   }

   [Fact]
   public async Task FindSymbols_Declaration_Interface()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Interface],
         namespaceFilter: "CoreTestProject.Types",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      var iface = items![0];
      var declaration = iface?.GetStringOrNull("declaration");
      Assert.NotNull(declaration);
      Assert.Contains("interface", declaration);
   }

   [Fact]
   public async Task FindSymbols_Declaration_Struct()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Struct],
         namespaceFilter: "CoreTestProject.Types",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      var structSymbol = items!.FirstOrDefault(m => m?.GetStringOrNull("name") == "Point");
      Assert.NotNull(structSymbol);
      var declaration = structSymbol?.GetStringOrNull("declaration");
      Assert.NotNull(declaration);
      Assert.Contains("struct", declaration);
      Assert.Contains("Point", declaration);
   }

   [Fact]
   public async Task FindSymbols_Declaration_Enum()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Enum],
         namespaceFilter: "CoreTestProject.Refactoring",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      var enumSymbol = items!.FirstOrDefault(m => m?.GetStringOrNull("name") == "LogLevel");
      Assert.NotNull(enumSymbol);
      var declaration = enumSymbol?.GetStringOrNull("declaration");
      Assert.NotNull(declaration);
      Assert.Contains("enum", declaration);
      Assert.Contains("LogLevel", declaration);
   }

   [Fact]
   public async Task FindSymbols_Declaration_PublicStaticMethod()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Method],
         modifiers: [ModifierFilter.Static],
         accessibility: AccessibilityDto.Public,
         verbosity: ToolResultVerbosity.Full,
         maxResults: 10,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      var method = items![0];
      var declaration = method?.GetStringOrNull("declaration");
      Assert.NotNull(declaration);
      Assert.StartsWith("public static", declaration);
   }

   [Fact]
   public async Task FindSymbols_Declaration_AsyncMethod_ContainsTaskReturnType()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Method],
         modifiers: [ModifierFilter.Async],
         verbosity: ToolResultVerbosity.Full,
         maxResults: 10,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      var method = items![0];
      var declaration = method?.GetStringOrNull("declaration");
      Assert.NotNull(declaration);
      Assert.Contains("Task", declaration);
   }

   [Fact]
   public async Task FindSymbols_Declaration_Property()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Property],
         accessibility: AccessibilityDto.Public,
         verbosity: ToolResultVerbosity.Full,
         maxResults: 10,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      var prop = items![0];
      var declaration = prop?.GetStringOrNull("declaration");
      Assert.NotNull(declaration);
      Assert.StartsWith("public", declaration);
      Assert.NotNull(prop?.GetStringOrNull("name"));
   }

   [Fact]
   public async Task FindSymbols_Declaration_Field()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Field],
         verbosity: ToolResultVerbosity.Full,
         maxResults: 20,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      var field = items![0];
      var declaration = field?.GetStringOrNull("declaration");
      Assert.NotNull(declaration);
      Assert.NotNull(field?.GetStringOrNull("name"));
   }

   [Fact]
   public async Task FindSymbols_Declaration_GenericClass()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Class],
         namespaceFilter: "CoreTestProject.Types",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 100,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);

      var genericClass = items!.FirstOrDefault(m => m?.GetStringOrNull("name") == "Container");
      Assert.NotNull(genericClass);
      var declaration = genericClass?.GetStringOrNull("declaration");
      Assert.NotNull(declaration);
      Assert.Contains("class", declaration);
      Assert.Contains("<T>", declaration);
   }

   #endregion

   #region Combined Name and Property Filters

   [Fact]
   public async Task FindSymbols_NameAndPropertyCombined_FindsAsyncFetchMethods()
   {
      var result = await _tool.FindSymbolsAsync(
         name: "Fetch*",
         kinds: [SymbolKindFilter.Method],
         modifiers: [ModifierFilter.Async],
         verbosity: ToolResultVerbosity.Full,
         maxResults: 20,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      foreach (var symbol in items!)
      {
         var name = symbol?.GetStringOrNull("name");
         Assert.NotNull(name);
         Assert.StartsWith("Fetch", name);

         var declaration = symbol?.GetStringOrNull("declaration") ?? "";
         Assert.Contains("Task", declaration);
      }
   }

   [Fact]
   public async Task FindSymbols_NameAndPropertyCombined_FindsPublicServices()
   {
      var result = await _tool.FindSymbolsAsync(
         name: "*Service",
         kinds: [SymbolKindFilter.Class],
         accessibility: AccessibilityDto.Public,
         verbosity: ToolResultVerbosity.Full,
         maxResults: 20,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      foreach (var symbol in items!)
      {
         var name = symbol?.GetStringOrNull("name");
         Assert.NotNull(name);
         Assert.EndsWith("Service", name);
         Assert.Equal("class", symbol?.GetStringOrNull("kind"));
      }
   }

   #endregion

   #region Sorting

   [Fact]
   public async Task FindSymbols_SortByName_ReturnsSortedResults()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Class],
         namespaceFilter: "CoreTestProject.*",
         sortBy: SymbolSortBy.Name,
         maxResults: 20,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      var names = items!.Select(s => s?.GetStringOrNull("name")).ToList();
      var sortedNames = names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
      Assert.Equal(sortedNames, names);
   }

   [Fact]
   public async Task FindSymbols_SortByKind_GroupsByKind()
   {
      var result = await _tool.FindSymbolsAsync(
         namespaceFilter: "CoreTestProject.Types",
         sortBy: SymbolSortBy.Kind,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      // Define expected kind order (from GetKindSortOrder)
      var kindOrder = new Dictionary<string, int>
      {
         ["interface"] = 0,
         ["class"] = 1,
         ["struct"] = 2,
         ["enum"] = 3,
         ["delegate"] = 4,
         ["constructor"] = 5,
         ["method"] = 6,
         ["property"] = 7,
         ["field"] = 8,
         ["event"] = 9
      };

      var kinds = items!.Select(s => s?.GetStringOrNull("kind")).ToList();
      Assert.NotNull(kinds);

      // Verify kinds are in non-decreasing order
      for (int i = 1; i < kinds.Count; i++)
      {
         var prevKind = kinds[i - 1];
         var currKind = kinds[i];
         Assert.NotNull(prevKind);
         Assert.NotNull(currKind);

         var prevOrder = kindOrder.GetValueOrDefault(prevKind!, 99);
         var currOrder = kindOrder.GetValueOrDefault(currKind!, 99);
         Assert.True(prevOrder <= currOrder,
            $"Kinds not sorted: {prevKind} (order {prevOrder}) should not come before {currKind} (order {currOrder}) at index {i}");
      }
   }

   #endregion

   #region Input Validation

   [Fact]
   public async Task FindSymbols_NoFilters_ThrowsToolException()
   {
      // Calling with no filters should throw, not enumerate everything
      await AssertThrowsToolExceptionAsync(
         async () => await _tool.FindSymbolsAsync(maxResults: 10, cancellationToken: CT),
         ErrorCodes.InvalidParameter);
   }

   [Fact]
   public async Task FindSymbols_OnlyMaxResults_ThrowsToolException()
   {
      // Just specifying maxResults/offset/verbosity doesn't count as a filter
      await AssertThrowsToolExceptionAsync(
         async () => await _tool.FindSymbolsAsync(
            maxResults: 50,
            offset: 0,
            verbosity: ToolResultVerbosity.Full,
            cancellationToken: CT),
         ErrorCodes.InvalidParameter);
   }

   #endregion

   #region Project Filter

   [Fact]
   public async Task FindSymbols_ProjectFilter_LimitsToProject()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Class],
         project: "CoreTestProject",
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);
   }

   [Fact]
   public async Task FindSymbols_InvalidProjectName_ReturnsEmptyResults()
   {
      // Non-existent project name should return empty results (not throw)
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Class],
         project: "NonExistentProject",
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.Empty(items!);
   }

   [Fact]
   public async Task FindSymbols_InvalidNamespacePattern_ReturnsEmptyResults()
   {
      // Namespace that doesn't exist should return empty results
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Class],
         namespaceFilter: "NonExistent.Namespace.*",
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.Empty(items!);
   }

   #endregion

   #region Parameter Filters

   [Fact]
   public async Task FindSymbols_ParameterIncludes_FindsMethodsWithCancellationToken()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Method],
         parameterIncludes: ["CancellationToken"],
         namespaceFilter: "CoreTestProject.*",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      // All returned methods should have CancellationToken parameter
      foreach (var symbol in items!)
      {
         var declaration = symbol?.GetStringOrNull("declaration") ?? "";
         Assert.Contains("CancellationToken", declaration);
      }
   }

   [Fact]
   public async Task FindSymbols_ParameterIncludes_MultipleTypes_RequiresAll()
   {
      // Methods with both string and CancellationToken parameters
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Method],
         parameterIncludes: ["string", "CancellationToken"],
         namespaceFilter: "CoreTestProject.*",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      // All methods should have both parameter types
      foreach (var symbol in items!)
      {
         var declaration = symbol?.GetStringOrNull("declaration") ?? "";
         Assert.Contains("string", declaration);
         Assert.Contains("CancellationToken", declaration);
      }
   }

   [Fact]
   public async Task FindSymbols_ParameterExcludes_ExcludesMethodsWithCancellationToken()
   {
      // Find async methods without CancellationToken
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Method],
         name: "*Async",
         parameterExcludes: ["CancellationToken"],
         namespaceFilter: "CoreTestProject.*",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      // None of the returned methods should have CancellationToken parameter
      foreach (var symbol in items!)
      {
         var declaration = symbol?.GetStringOrNull("declaration") ?? "";
         var name = symbol?.GetStringOrNull("name") ?? "";
         Assert.DoesNotContain("CancellationToken", declaration);
         Assert.EndsWith("Async", name);
      }
   }

   [Fact]
   public async Task FindSymbols_ParameterIncludesAndExcludes_Combined()
   {
      // Methods with string parameter but without CancellationToken
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Method],
         parameterIncludes: ["string"],
         parameterExcludes: ["CancellationToken"],
         namespaceFilter: "CoreTestProject.*",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArrayOrNull("items");
      Assert.NotNull(items);
      Assert.NotEmpty(items!);

      foreach (var symbol in items!)
      {
         var declaration = symbol?.GetStringOrNull("declaration") ?? "";
         Assert.Contains("string", declaration);
         Assert.DoesNotContain("CancellationToken", declaration);
      }
   }

   #endregion

   #region Modifier Filters

   [Fact]
   public async Task FindSymbols_ModifierFilter_Record_FindsRecordTypes()
   {
      var result = await _tool.FindSymbolsAsync(
         modifiers: [ModifierFilter.Record],
         namespaceFilter: "CoreTestProject.Types",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArray("items");
      Assert.NotEmpty(items);

      var names = items.Select(m => m.GetString("name")).ToList();
      // Person, Employee, Customer are record classes; Money is record struct
      Assert.Contains("Person", names);
      Assert.Contains("Employee", names);
      Assert.Contains("Customer", names);
      Assert.Contains("Money", names);

      // Non-record types should NOT be included
      Assert.DoesNotContain("SampleClass", names);
      Assert.DoesNotContain("Point", names);
   }

   [Fact]
   public async Task FindSymbols_ModifierFilter_Abstract_FindsAbstractMembers()
   {
      var result = await _tool.FindSymbolsAsync(
         modifiers: [ModifierFilter.Abstract],
         namespaceFilter: "CoreTestProject.Types",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArray("items");
      Assert.NotEmpty(items);

      var names = items.Select(m => m.GetString("name")).ToList();
      // AbstractBase is abstract class; AbstractMethod, AbstractProperty are abstract members
      Assert.Contains("AbstractBase", names);
      Assert.Contains("AbstractMethod", names);
      Assert.Contains("AbstractProperty", names);

      // Non-abstract types should not be included
      Assert.DoesNotContain("SampleClass", names);
   }

   [Fact]
   public async Task FindSymbols_ModifierFilter_Virtual_FindsVirtualMembers()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Method],
         modifiers: [ModifierFilter.Virtual],
         namespaceFilter: "CoreTestProject.Types",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArray("items");
      Assert.NotEmpty(items);

      var names = items.Select(m => m.GetString("name")).ToList();
      Assert.Contains("VirtualMethod", names);
      Assert.Contains("ProtectedVirtualMethod", names);

      // Override methods should NOT be included (they have Override modifier, not Virtual)
      // Abstract methods should NOT be included (Abstract, not Virtual)
      Assert.DoesNotContain("AbstractMethod", names);
   }

   [Fact]
   public async Task FindSymbols_ModifierFilter_Override_FindsOverrides()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Method],
         modifiers: [ModifierFilter.Override],
         namespaceFilter: "CoreTestProject.Types",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArray("items");
      Assert.NotEmpty(items);

      // ConcreteClass.AbstractMethod is an override
      var names = items.Select(m => m.GetString("name")).ToList();
      Assert.Contains("AbstractMethod", names);
      Assert.Contains("VirtualMethod", names);
   }

   [Fact]
   public async Task FindSymbols_ModifierFilter_Sealed_FindsSealedTypes()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Class],
         modifiers: [ModifierFilter.Sealed],
         namespaceFilter: "CoreTestProject.Types",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArray("items");
      Assert.NotEmpty(items);

      var names = items.Select(m => m.GetString("name")).ToList();
      Assert.Contains("SealedClass", names);

      // Non-sealed classes should NOT be included
      Assert.DoesNotContain("SampleClass", names);
      Assert.DoesNotContain("AbstractBase", names);
   }

   [Fact]
   public async Task FindSymbols_ModifierFilter_ReadOnly_FindsReadonlyFields()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Field],
         modifiers: [ModifierFilter.ReadOnly],
         namespaceFilter: "CoreTestProject.Types",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArray("items");
      Assert.NotEmpty(items);

      var names = items.Select(m => m.GetString("name")).ToList();
      Assert.Contains("PublicReadonlyField", names);
   }

   [Fact]
   public async Task FindSymbols_ModifierFilter_ReadOnly_FindsReadonlyStructs()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Struct],
         modifiers: [ModifierFilter.ReadOnly],
         namespaceFilter: "CoreTestProject.Types",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArray("items");
      Assert.NotEmpty(items);

      var names = items.Select(m => m.GetString("name")).ToList();
      Assert.Contains("ImmutablePoint", names);

      // Non-readonly structs should NOT be included
      Assert.DoesNotContain("Point", names);
   }

   [Fact]
   public async Task FindSymbols_ModifierFilter_Combined_StaticAndAsync()
   {
      // AND semantics: must be BOTH static AND async
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Method],
         modifiers: [ModifierFilter.Static, ModifierFilter.Async],
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArray("items");
      Assert.NotEmpty(items);

      var names = items.Select(m => m.GetString("name")).ToList();
      // ModifierUtils.StaticAsyncMethod is static async
      Assert.Contains("StaticAsyncMethod", names);

      // Verify all results have both Static and Async modifiers
      foreach (var item in items)
      {
         var declaration = item.GetString("declaration");
         Assert.Contains("static", declaration);

         // The async modifier is tracked semantically (not always in declaration string),
         // so verify via modifiers field instead
         var modifiersStr = item.GetStringOrNull("modifiers");
         Assert.NotNull(modifiersStr);
         var symbolModifiers = Enum.Parse<SymbolModifiers>(modifiersStr, ignoreCase: true);
         Assert.True(symbolModifiers.HasFlag(SymbolModifiers.Static));
         Assert.True(symbolModifiers.HasFlag(SymbolModifiers.Async));
      }
   }

   [Fact]
   public async Task FindSymbols_ModifierFilter_Const_FindsConstFields()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Field],
         modifiers: [ModifierFilter.Const],
         namespaceFilter: "CoreTestProject.Types",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArray("items");
      Assert.NotEmpty(items);

      var names = items.Select(m => m.GetString("name")).ToList();
      Assert.Contains("ConstantField", names);
   }

   [Fact]
   public async Task FindSymbols_ModifierFilter_Partial_FindsSingleFilePartialClass()
   {
      var result = await _tool.FindSymbolsAsync(
         name: "SingleFilePartialClass",
         modifiers: [ModifierFilter.Partial],
         namespaceFilter: "CoreTestProject.Types",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArray("items");

      // SingleFilePartialClass is declared with 'partial' keyword in a single file.
      // It must be detected as partial even though it has only one syntax reference.
      var names = items.Select(m => m.GetString("name")).ToList();
      Assert.Contains("SingleFilePartialClass", names);
   }

   [Fact]
   public async Task FindSymbols_ModifierFilter_Partial_ExcludesNonPartialClass()
   {
      var result = await _tool.FindSymbolsAsync(
         name: "NonPartialClass",
         modifiers: [ModifierFilter.Partial],
         namespaceFilter: "CoreTestProject.Types",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArray("items");
      Assert.Empty(items);
   }

   [Fact]
   public async Task FindSymbols_ModifierFilter_Volatile_FindsVolatileFields()
   {
      var result = await _tool.FindSymbolsAsync(
         kinds: [SymbolKindFilter.Field],
         modifiers: [ModifierFilter.Volatile],
         namespaceFilter: "CoreTestProject.Types",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArray("items");
      var names = items.Select(m => m.GetString("name")).ToList();
      Assert.Contains("VolatileCounter", names);
   }

   [Fact]
   public async Task FindSymbols_ModifierFilter_Extern_FindsExternMethods()
   {
      var result = await _tool.FindSymbolsAsync(
         modifiers: [ModifierFilter.Extern],
         namespaceFilter: "CoreTestProject.Types",
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var items = data.GetArray("items");
      var names = items.Select(m => m.GetString("name")).ToList();
      Assert.Contains("GetCurrentThreadId", names);
   }

   #endregion
}
