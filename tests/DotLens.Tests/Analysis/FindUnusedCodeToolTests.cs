using DotLens.Infrastructure;
using DotLens.Tests;
using DotLens.Tools.Analysis;
using DotLens.Tools.Shared;
using Xunit;

namespace DotLens.Analysis.Tests;

[Collection("TestFixtures")]
public class FindUnusedCodeToolTests : TestFixtureTestBase
{
   public FindUnusedCodeToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private FindUnusedCodeTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      _tool = new FindUnusedCodeTool(Workspace);
   }

   #region Exact Match Tests - Types

   [Fact]
   public async Task FindUnusedCode_Classes_ReturnsExactlyExpectedUnusedClasses()
   {
      // CoreTestProject/Patterns/UnusedCode.cs defines these unused classes:
      // - UnusedInternalClass (internal, requires includeInternal=true)
      // - UnusedPrivateNestedClass (private nested class)
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: true,
         includeInternal: true,
         symbolKindFilter: SymbolKindFilter.Class,
         maxResults: 100,
         cancellationToken: CT);

      AssertSuccess(result);

      var actualNames = result.Data!.UnusedSymbols
         .Select(s => s.Name)
         .Where(n => n.Contains("Unused")) // Filter to our test fixtures
         .ToArray();

      // UnusedInternalClass is the only unused non-nested class
      // UnusedPrivateNestedClass is nested inside ContainerWithNestedUnused
      var expectedNames = new[] { "UnusedInternalClass", "UnusedPrivateNestedClass" };

      Assert.Equivalent(expectedNames, actualNames);

      // Guard against false positives: no unexpected symbols from UnusedCode.cs
      // Note: Some "consumer" classes in UnusedCode.cs are also legitimately unused
      // (they exist to call other symbols but are never instantiated themselves)
      var knownUnusedClasses = new HashSet<string>
      {
         "UnusedInternalClass", "UnusedPrivateNestedClass",
         "UnusedRecord", // Records are classes (record class)
         "ContainerWithNestedUnused", // Contains nested unused but has UseNested() called
         // Consumer classes are also technically unused (no external instantiation)
         "TypeConsumerUser", "VirtualConsumer", "ConstructorConsumer",
         "CallerOfHelperMethod", "PartiallyUsedImplementation", "InterfaceConsumer",
         // Static constructor test classes
         "ClassWithUnusedStaticConstructor", "ClassWithUsedStaticConstructor", "StaticConstructorConsumer"
      };

      var unexpectedFromFixture = result.Data!.UnusedSymbols
         .Where(s => s.Location?.FilePath?.Contains("UnusedCode.cs") == true)
         .Where(s => !knownUnusedClasses.Contains(s.Name))
         .Select(s => s.Name)
         .ToArray();

      Assert.Empty(unexpectedFromFixture);
   }

   [Fact]
   public async Task FindUnusedCode_Structs_ReturnsExactlyExpectedUnusedStructs()
   {
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: true,
         includeInternal: true,
         symbolKindFilter: SymbolKindFilter.Struct,
         maxResults: 100,
         cancellationToken: CT);

      AssertSuccess(result);

      var actualNames = result.Data!.UnusedSymbols
         .Select(s => s.Name)
         .Where(n => n.Contains("Unused"))
         .ToArray();

      var expectedNames = new[] { "UnusedStruct" };

      Assert.Equivalent(expectedNames, actualNames);

      // Guard against false positives: no unexpected structs from UnusedCode.cs
      var knownUnusedStructs = new HashSet<string> { "UnusedStruct" };
      var unexpectedFromFixture = result.Data!.UnusedSymbols
         .Where(s => s.Location?.FilePath?.Contains("UnusedCode.cs") == true)
         .Where(s => !knownUnusedStructs.Contains(s.Name))
         .Select(s => s.Name)
         .ToArray();
      Assert.Empty(unexpectedFromFixture);
   }

   [Fact]
   public async Task FindUnusedCode_ClassFilter_IncludesRecordsWithRecordModifier()
   {
      // Records are now included in Class filter (record classes are classes with Record modifier)
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: true,
         includeInternal: true,
         symbolKindFilter: SymbolKindFilter.Class,
         maxResults: 100,
         cancellationToken: CT);

      AssertSuccess(result);

      // Filter to just records (those with Record modifier)
      var unusedRecords = result.Data!.UnusedSymbols
         .Where(s => s.Modifiers?.HasFlag(SymbolModifiers.Record) == true)
         .ToList();

      // UnusedRecord should be found
      var unusedRecordNames = unusedRecords.Select(s => s.Name).ToArray();
      Assert.Contains("UnusedRecord", unusedRecordNames);

      // Guard against false positives: no unexpected records from UnusedCode.cs
      var knownUnusedRecords = new HashSet<string> { "UnusedRecord" };
      var unexpectedFromFixture = unusedRecords
         .Where(s => s.Location?.FilePath?.Contains("UnusedCode.cs") == true)
         .Where(s => !knownUnusedRecords.Contains(s.Name))
         .Select(s => s.Name)
         .ToArray();
      Assert.Empty(unexpectedFromFixture);

      // Verify it's returned as ClassSymbolResponse
      var unusedRecord = unusedRecords.First(s => s.Name == "UnusedRecord");
      Assert.IsType<ClassSymbolResponse>(unusedRecord);
   }

   [Fact]
   public async Task FindUnusedCode_Interfaces_ReturnsExactlyExpectedUnusedInterfaces()
   {
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: true,
         includeInternal: true,
         symbolKindFilter: SymbolKindFilter.Interface,
         maxResults: 100,
         cancellationToken: CT);

      AssertSuccess(result);

      var actualNames = result.Data!.UnusedSymbols
         .Select(s => s.Name)
         .Where(n => n.Contains("Unused"))
         .ToArray();

      var expectedNames = new[] { "IUnusedInterface" };

      Assert.Equivalent(expectedNames, actualNames);

      // Guard against false positives: no unexpected interfaces from UnusedCode.cs
      var knownUnusedInterfaces = new HashSet<string> { "IUnusedInterface" };
      var unexpectedFromFixture = result.Data!.UnusedSymbols
         .Where(s => s.Location?.FilePath?.Contains("UnusedCode.cs") == true)
         .Where(s => !knownUnusedInterfaces.Contains(s.Name))
         .Select(s => s.Name)
         .ToArray();
      Assert.Empty(unexpectedFromFixture);
   }

   [Fact]
   public async Task FindUnusedCode_Enums_ReturnsExactlyExpectedUnusedEnums()
   {
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: true,
         includeInternal: true,
         symbolKindFilter: SymbolKindFilter.Enum,
         maxResults: 100,
         cancellationToken: CT);

      AssertSuccess(result);

      var actualNames = result.Data!.UnusedSymbols
         .Select(s => s.Name)
         .Where(n => n.Contains("Unused"))
         .ToArray();

      var expectedNames = new[] { "UnusedEnum" };

      Assert.Equivalent(expectedNames, actualNames);

      // Guard against false positives: no unexpected enums from UnusedCode.cs
      var knownUnusedEnums = new HashSet<string> { "UnusedEnum" };
      var unexpectedFromFixture = result.Data!.UnusedSymbols
         .Where(s => s.Location?.FilePath?.Contains("UnusedCode.cs") == true)
         .Where(s => !knownUnusedEnums.Contains(s.Name))
         .Select(s => s.Name)
         .ToArray();
      Assert.Empty(unexpectedFromFixture);
   }

   [Fact]
   public async Task FindUnusedCode_Delegates_ReturnsExactlyExpectedUnusedDelegates()
   {
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: true,
         includeInternal: true,
         symbolKindFilter: SymbolKindFilter.Delegate,
         maxResults: 100,
         cancellationToken: CT);

      AssertSuccess(result);

      var actualNames = result.Data!.UnusedSymbols
         .Select(s => s.Name)
         .Where(n => n.Contains("Unused"))
         .ToArray();

      var expectedNames = new[] { "UnusedDelegate" };

      Assert.Equivalent(expectedNames, actualNames);

      // Guard against false positives: no unexpected delegates from UnusedCode.cs
      var knownUnusedDelegates = new HashSet<string> { "UnusedDelegate" };
      var unexpectedFromFixture = result.Data!.UnusedSymbols
         .Where(s => s.Location?.FilePath?.Contains("UnusedCode.cs") == true)
         .Where(s => !knownUnusedDelegates.Contains(s.Name))
         .Select(s => s.Name)
         .ToArray();
      Assert.Empty(unexpectedFromFixture);
   }

   #endregion

   #region Exact Match Tests - Members

   [Fact]
   public async Task FindUnusedCode_Methods_ReturnsExactlyExpectedUnusedMethods()
   {
      // CoreTestProject/Patterns/UnusedCode.cs defines these unused methods:
      // - UnusedPrivateMethod (in UnusedCodeExamples)
      // - UnusedCalculation (in UnusedCodeExamples)
      // Note: Virtual/override methods are excluded by design
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: true,
         includeInternal: true,
         symbolKindFilter: SymbolKindFilter.Method,
         maxResults: 500,
         cancellationToken: CT);

      AssertSuccess(result);

      // Filter to methods in UnusedCodeExamples class only
      var actualNames = result.Data!.UnusedSymbols
         .Where(s => s.ContainingType?.Contains("UnusedCodeExamples") == true)
         .Select(s => s.Name)
         .ToArray();

      var expectedNames = new[] { "UnusedPrivateMethod", "UnusedCalculation" };

      Assert.Equivalent(expectedNames, actualNames);

      // Guard against false positives: no unexpected methods from UnusedCode.cs
      // Note: Many methods in UnusedCode.cs are legitimately unused (they exist to make
      // other symbols "used" but are never called externally)
      var knownUnusedMethods = new HashSet<string>
      {
         "UnusedPrivateMethod", "UnusedCalculation",
         // Methods from unused classes or consumer classes that are never instantiated
         "DoSomething", "Process", "UnusedInterfaceMethod", "UseConsumers", "UseNested",
         "VirtualMethod", "UsedVirtualMethod", "DoWork", "Consume", "ValidateUser",
         "UsedInterfaceMethod", "UnusedInterfaceMethod", "NestedMethod"
      };
      var unexpectedFromFixture = result.Data!.UnusedSymbols
         .Where(s => s.Location?.FilePath?.Contains("UnusedCode.cs") == true)
         .Where(s => s is MethodSymbolResponse)
         .Where(s => !knownUnusedMethods.Contains(s.Name))
         .Select(s => s.Name)
         .ToArray();
      Assert.Empty(unexpectedFromFixture);
   }

   [Fact]
   public async Task FindUnusedCode_Properties_ReturnsExactlyExpectedUnusedProperties()
   {
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: true,
         includeInternal: true,
         symbolKindFilter: SymbolKindFilter.Property,
         maxResults: 500,
         cancellationToken: CT);

      AssertSuccess(result);

      // Filter to properties in UnusedCodeExamples class
      var actualNames = result.Data!.UnusedSymbols
         .Where(s => s.ContainingType?.Contains("UnusedCodeExamples") == true)
         .Select(s => s.Name)
         .ToArray();

      var expectedNames = new[] { "UnusedProperty" };

      Assert.Equivalent(expectedNames, actualNames);

      // Guard against false positives: no unexpected properties from UnusedCode.cs
      // Note: Properties from unused types are also legitimately unused
      var knownUnusedProperties = new HashSet<string>
      {
         "UnusedProperty",
         // Properties from unused types (structs, records, interfaces)
         "Value", "Name", "UnusedInterfaceProperty"
      };
      var unexpectedFromFixture = result.Data!.UnusedSymbols
         .Where(s => s.Location?.FilePath?.Contains("UnusedCode.cs") == true)
         .Where(s => s is PropertySymbolResponse)
         .Where(s => !knownUnusedProperties.Contains(s.Name))
         .Select(s => s.Name)
         .ToArray();
      Assert.Empty(unexpectedFromFixture);
   }

   [Fact]
   public async Task FindUnusedCode_Fields_ReturnsExactlyExpectedUnusedFields()
   {
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: true,
         includeInternal: true,
         symbolKindFilter: SymbolKindFilter.Field,
         maxResults: 500,
         cancellationToken: CT);

      AssertSuccess(result);

      // Filter to fields in UnusedCodeExamples class
      var actualNames = result.Data!.UnusedSymbols
         .Where(s => s.ContainingType?.Contains("UnusedCodeExamples") == true)
         .Select(s => s.Name)
         .ToArray();

      var expectedNames = new[] { "_unusedField", "_unusedReadonlyField" };

      Assert.Equivalent(expectedNames, actualNames);

      // Guard against false positives: no unexpected fields from UnusedCode.cs
      // Note: Enum members are also reported as fields, and backing fields from records
      var knownUnusedFields = new HashSet<string>
      {
         "_unusedField", "_unusedReadonlyField",
         // Enum members from unused enums (UnusedEnum, UsedEnum values that are never read)
         "Value1", "Value2", "Value3", "Active", "Inactive"
      };
      var unexpectedFromFixture = result.Data!.UnusedSymbols
         .Where(s => s.Location?.FilePath?.Contains("UnusedCode.cs") == true)
         .Where(s => s is FieldSymbolResponse)
         .Where(s => !knownUnusedFields.Contains(s.Name))
         .Select(s => s.Name)
         .ToArray();
      Assert.Empty(unexpectedFromFixture);
   }

   [Fact]
   public async Task FindUnusedCode_Events_ReturnsExactlyExpectedUnusedEvents()
   {
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: true,
         includeInternal: true,
         symbolKindFilter: SymbolKindFilter.Event,
         maxResults: 500,
         cancellationToken: CT);

      AssertSuccess(result);

      // Filter to events in UnusedCodeExamples class
      var actualNames = result.Data!.UnusedSymbols
         .Where(s => s.ContainingType?.Contains("UnusedCodeExamples") == true)
         .Select(s => s.Name)
         .ToArray();

      var expectedNames = new[] { "UnusedEvent" };

      Assert.Equivalent(expectedNames, actualNames);

      // Guard against false positives: no unexpected events from UnusedCode.cs
      var knownUnusedEvents = new HashSet<string> { "UnusedEvent" };
      var unexpectedFromFixture = result.Data!.UnusedSymbols
         .Where(s => s.Location?.FilePath?.Contains("UnusedCode.cs") == true)
         .Where(s => s is EventSymbolResponse)
         .Where(s => !knownUnusedEvents.Contains(s.Name))
         .Select(s => s.Name)
         .ToArray();
      Assert.Empty(unexpectedFromFixture);
   }

   [Fact]
   public async Task FindUnusedCode_Constructors_ReturnsExactlyExpectedUnusedConstructors()
   {
      // CoreTestProject/Patterns/UnusedCode.cs defines:
      // - ClassWithUnusedConstructor(string) - parameterized ctor, UNUSED
      // - ClassWithUnusedConstructor() - default ctor, USED by ConstructorConsumer
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: true,
         includeInternal: true,
         symbolKindFilter: SymbolKindFilter.Constructor,
         maxResults: 100,
         cancellationToken: CT);

      AssertSuccess(result);

      // Filter to constructors in our test class
      var actualCtors = result.Data!.UnusedSymbols
         .Where(s => s.ContainingType?.Contains("ClassWithUnusedConstructor") == true)
         .ToArray();

      // Should find exactly one unused parameterized constructor
      Assert.Single(actualCtors);

      // Verify it's the parameterized constructor (has "string" parameter in signature)
      var unusedCtor = actualCtors.First();
      Assert.IsType<ConstructorSymbolResponse>(unusedCtor);
      var ctorResponse = (ConstructorSymbolResponse)unusedCtor;
      Assert.Contains("string", ctorResponse.Signature); // Has "unusedParam" parameter
   }

   [Fact]
   public async Task FindUnusedCode_Constructors_IncludesUnusedStaticConstructors()
   {
      // CoreTestProject/Patterns/UnusedCode.cs defines:
      // - ClassWithUnusedStaticConstructor: has an explicit static ctor, class is never used
      // - ClassWithUsedStaticConstructor: has an explicit static ctor, class IS used (via StaticConstructorConsumer)
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: true,
         includeInternal: true,
         symbolKindFilter: SymbolKindFilter.Constructor,
         maxResults: 100,
         cancellationToken: CT);

      AssertSuccess(result);

      // Should find the UNUSED static constructor
      var unusedStaticCtor = result.Data!.UnusedSymbols
         .FirstOrDefault(s => s.ContainingType == "CoreTestProject.Patterns.ClassWithUnusedStaticConstructor");
      Assert.NotNull(unusedStaticCtor);
      Assert.IsType<ConstructorSymbolResponse>(unusedStaticCtor);

      // Should NOT find the USED static constructor (its containing type is referenced)
      var usedStaticCtor = result.Data.UnusedSymbols
         .FirstOrDefault(s => s.ContainingType == "CoreTestProject.Patterns.ClassWithUsedStaticConstructor");
      Assert.Null(usedStaticCtor);
   }

   #endregion

   #region False Positive Tests - Used Symbols Must Not Appear

   [Fact]
   public async Task FindUnusedCode_DoesNotReportUsedClasses()
   {
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: true,
         includeInternal: true,
         symbolKindFilter: SymbolKindFilter.Class,
         maxResults: 500,
         cancellationToken: CT);

      AssertSuccess(result);

      var usedClassNames = new[] { "UsedClass", "UsedTypesConsumer", "MemberConsumer" };
      var actualNames = result.Data!.UnusedSymbols.Select(s => s.Name).ToHashSet();

      foreach (var usedName in usedClassNames)
      {
         Assert.DoesNotContain(usedName, actualNames);
      }
   }

   [Fact]
   public async Task FindUnusedCode_DoesNotReportUsedStructs()
   {
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: true,
         includeInternal: true,
         symbolKindFilter: SymbolKindFilter.Struct,
         maxResults: 500,
         cancellationToken: CT);

      AssertSuccess(result);

      var actualNames = result.Data!.UnusedSymbols.Select(s => s.Name).ToHashSet();
      Assert.DoesNotContain("UsedStruct", actualNames);
   }

   [Fact]
   public async Task FindUnusedCode_DoesNotReportUsedRecords()
   {
      // Records are included in Class filter; verify used records aren't reported
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: true,
         includeInternal: true,
         symbolKindFilter: SymbolKindFilter.Class,
         maxResults: 500,
         cancellationToken: CT);

      AssertSuccess(result);

      var actualNames = result.Data!.UnusedSymbols.Select(s => s.Name).ToHashSet();
      Assert.DoesNotContain("UsedRecord", actualNames);
   }

   [Fact]
   public async Task FindUnusedCode_DoesNotReportUsedInterfaces()
   {
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: true,
         includeInternal: true,
         symbolKindFilter: SymbolKindFilter.Interface,
         maxResults: 500,
         cancellationToken: CT);

      AssertSuccess(result);

      var actualNames = result.Data!.UnusedSymbols.Select(s => s.Name).ToHashSet();
      Assert.DoesNotContain("IUsedInterface", actualNames);
   }

   [Fact]
   public async Task FindUnusedCode_DoesNotReportUsedEnums()
   {
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: true,
         includeInternal: true,
         symbolKindFilter: SymbolKindFilter.Enum,
         maxResults: 500,
         cancellationToken: CT);

      AssertSuccess(result);

      var actualNames = result.Data!.UnusedSymbols.Select(s => s.Name).ToHashSet();
      Assert.DoesNotContain("UsedEnum", actualNames);
   }

   [Fact]
   public async Task FindUnusedCode_DoesNotReportUsedDelegates()
   {
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: true,
         includeInternal: true,
         symbolKindFilter: SymbolKindFilter.Delegate,
         maxResults: 500,
         cancellationToken: CT);

      AssertSuccess(result);

      var actualNames = result.Data!.UnusedSymbols.Select(s => s.Name).ToHashSet();
      Assert.DoesNotContain("UsedDelegate", actualNames);
   }

   [Fact]
   public async Task FindUnusedCode_DoesNotReportUsedConstructors()
   {
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: true,
         includeInternal: true,
         symbolKindFilter: SymbolKindFilter.Constructor,
         maxResults: 500,
         cancellationToken: CT);

      AssertSuccess(result);

      // The default constructor of ClassWithUnusedConstructor is used - should NOT be reported
      var usedCtors = result.Data!.UnusedSymbols
         .OfType<ConstructorSymbolResponse>()
         .Where(c => c.ContainingType?.Contains("ClassWithUnusedConstructor") == true)
         .Where(c => c.Signature?.Contains("string") != true) // Filter out the parameterized ctor
         .ToArray();

      Assert.Empty(usedCtors); // The default constructor should NOT appear
   }

   [Fact]
   public async Task FindUnusedCode_DoesNotReportMethodCalledOnce()
   {
      // Regression test for bug 5qzu: Methods called exactly once were incorrectly
      // reported as unused (condition was referenceCount <= 1 instead of == 0)
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: true,
         includeInternal: true,
         symbolKindFilter: SymbolKindFilter.Method,
         maxResults: 500,
         cancellationToken: CT);

      AssertSuccess(result);

      // HelperWithUsedMethod.IsValidName is called once - should NOT be reported
      var falsePositive = result.Data!.UnusedSymbols
         .FirstOrDefault(s =>
            s.Name == "IsValidName" &&
            s.ContainingType?.Contains("HelperWithUsedMethod") == true);

      Assert.Null(falsePositive);
   }

   #endregion

   #region Accessibility Filter Tests

   [Fact]
   public async Task FindUnusedCode_ExcludeInternal_DoesNotReturnInternalClasses()
   {
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: true,
         includeInternal: false, // Exclude internal
         symbolKindFilter: SymbolKindFilter.Class,
         maxResults: 100,
         cancellationToken: CT);

      AssertSuccess(result);

      var actualNames = result.Data!.UnusedSymbols.Select(s => s.Name).ToHashSet();

      // UnusedInternalClass is internal, should NOT appear
      Assert.DoesNotContain("UnusedInternalClass", actualNames);
   }

   [Fact]
   public async Task FindUnusedCode_ExcludePrivate_DoesNotReturnPrivateMembers()
   {
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: false, // Exclude private
         includeInternal: true,
         symbolKindFilter: SymbolKindFilter.Method,
         maxResults: 500,
         cancellationToken: CT);

      AssertSuccess(result);

      // UnusedPrivateMethod and UnusedCalculation are private
      var privateUnusedMethods = result.Data!.UnusedSymbols
         .Where(s => s.ContainingType?.Contains("UnusedCodeExamples") == true)
         .Select(s => s.Name)
         .ToArray();

      // Should be empty - private methods excluded
      Assert.Empty(privateUnusedMethods);
   }

   #endregion

   #region Verbosity Tests

   [Fact]
   public async Task FindUnusedCode_VerbositySummary_ReturnsMinimalInfo()
   {
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includeInternal: true, // Include UnusedInternalClass
         verbosity: ToolResultVerbosity.Summary,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var symbols = data["unusedSymbols"]?.AsArray();
      Assert.NotNull(symbols);
      Assert.NotEmpty(symbols);

      // Summary verbosity: name and kind present
      var firstSymbol = symbols[0];
      Assert.NotNull(firstSymbol?["name"]);
      Assert.NotNull(firstSymbol?["kind"]);

      // Verify returned names are from expected unused symbols in UnusedCode.cs
      var expectedUnusedNames = new HashSet<string>
      {
         // Types
         "UnusedInternalClass", "UnusedPrivateNestedClass", "UnusedStruct", "UnusedRecord",
         "IUnusedInterface", "UnusedEnum", "UnusedDelegate",
         // Members
         "UnusedPrivateMethod", "UnusedCalculation", "UnusedProperty",
         "_unusedField", "_unusedReadonlyField", "UnusedEvent"
      };

      var names = symbols.Select(s => s?.GetStringOrNull("name")).Where(n => n != null).ToList();
      var knownNames = names.Where(n => expectedUnusedNames.Contains(n!)).ToList();

      // Should find a reasonable number of our known unused fixtures (at least 5)
      Assert.True(knownNames.Count >= 5,
         $"Expected at least 5 known unused symbols, found {knownNames.Count}: [{string.Join(", ", knownNames)}]");
   }

   [Fact]
   public async Task FindUnusedCode_VerbosityFull_IncludesLocation()
   {
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includeInternal: true, // Include UnusedInternalClass
         verbosity: ToolResultVerbosity.Full,
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var symbols = data["unusedSymbols"]?.AsArray();
      Assert.NotNull(symbols);
      Assert.NotEmpty(symbols);

      // Full verbosity: includes location
      var firstSymbol = symbols[0];
      Assert.NotNull(firstSymbol?["name"]);
      Assert.NotNull(firstSymbol?["kind"]);
      Assert.NotNull(firstSymbol?["location"]);

      // Verify location contains expected file path for symbols from UnusedCode.cs
      var expectedUnusedNames = new HashSet<string>
      {
         "UnusedInternalClass", "UnusedPrivateNestedClass", "UnusedStruct", "UnusedRecord",
         "IUnusedInterface", "UnusedEnum", "UnusedDelegate",
         "UnusedPrivateMethod", "UnusedCalculation", "UnusedProperty",
         "_unusedField", "_unusedReadonlyField", "UnusedEvent"
      };

      // Find a symbol from UnusedCode.cs and verify its location
      var knownSymbol = symbols
         .FirstOrDefault(s => expectedUnusedNames.Contains(s?.GetStringOrNull("name") ?? ""));
      Assert.NotNull(knownSymbol);

      var location = knownSymbol["location"];
      Assert.NotNull(location);
      var filePath = location.GetStringOrNull("filePath");
      Assert.NotNull(filePath);
      Assert.Contains("UnusedCode.cs", filePath);
   }

   #endregion

   #region Edge Cases

   [Fact]
   public async Task FindUnusedCode_InvalidProjectName_ReturnsEmptyResults()
   {
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "NonExistentProject",
         maxResults: 50,
         cancellationToken: CT);

      AssertSuccess(result);
      Assert.Empty(result.Data!.UnusedSymbols);
   }

   [Fact]
   public async Task FindUnusedCode_CountByKind_MatchesReturnedSymbols()
   {
      var result = await _tool.FindUnusedCodeAsync(
         projectName: "CoreTestProject",
         includePrivate: true,
         includeInternal: true,
         maxResults: 100,
         cancellationToken: CT);

      AssertSuccess(result);

      // Group returned symbols by kind using pattern matching on the polymorphic type
      var actualCountsByKind = result.Data!.UnusedSymbols
         .GroupBy(s => GetSymbolKind(s))
         .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

      // Verify counts match
      foreach (var kvp in result.Data.CountByKind)
      {
         var reported = kvp.Value;
         var actual = actualCountsByKind.GetValueOrDefault(kvp.Key, 0);
         Assert.Equal(reported, actual);
      }
   }

   private static string GetSymbolKind(SymbolResponse symbol) => symbol switch
   {
      // Types use TypeKind names
      ClassSymbolResponse => "Class",
      InterfaceSymbolResponse => "Interface",
      StructSymbolResponse => "Struct",
      EnumSymbolResponse => "Enum",
      DelegateSymbolResponse => "Delegate",
      // Members use SymbolKind names
      MethodSymbolResponse => "Method",
      ConstructorSymbolResponse => "Constructor",
      PropertySymbolResponse => "Property",
      FieldSymbolResponse => "Field",
      EventSymbolResponse => "Event",
      _ => "Unknown"
   };

   #endregion
}
