using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Navigation;
using Xunit;

namespace DotLens.Tests.Navigation;

[Collection("TestFixtures")]
public class GetTypeHierarchyToolTests : TestFixtureTestBase
{
   public GetTypeHierarchyToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetTypeHierarchyTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      var symbolLookupService = new SymbolLookupService(Workspace);
      _tool = new GetTypeHierarchyTool(Workspace, symbolLookupService);
   }

   #region Name-Based Lookup Tests

   [Fact]
   public async Task GetTypeHierarchy_ByName_ReturnsInheritanceInfo()
   {
      var target = new LookupByName { Name = "ConcreteClass" };
      var result = await _tool.GetTypeHierarchyAsync(target, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      // Should return baseTypes array (ConcreteClass inherits from AbstractBase)
      var baseTypes = data.GetArray("baseTypes");
      Assert.NotEmpty(baseTypes);
      // typeName should match what we searched for
      Assert.Contains("ConcreteClass", data.GetString("typeName"));
   }

   [Fact]
   public async Task GetTypeHierarchy_ByName_ReturnsFullHierarchy()
   {
      // DerivedClass -> ConcreteClass -> AbstractBase
      var target = new LookupByName { Name = "DerivedClass" };
      var result = await _tool.GetTypeHierarchyAsync(target, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      // Should have baseTypes, interfaces, and derivedTypes arrays
      // DerivedClass -> ConcreteClass -> AbstractBase = 2 base types
      var baseTypes = data.GetArray("baseTypes");
      Assert.Equal(2, baseTypes.Count);
      // DerivedClass doesn't implement any interfaces directly
      var interfaces = data.GetArray("interfaces");
      Assert.Empty(interfaces);
   }

   [Fact]
   public async Task GetTypeHierarchy_ByName_WithDeepInheritance_ReturnsAllBaseTypes()
   {
      // DerivedClass -> ConcreteClass -> AbstractBase
      var target = new LookupByName { Name = "DerivedClass" };
      var result = await _tool.GetTypeHierarchyAsync(target, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var baseTypes = data.GetArray("baseTypes");
      // DerivedClass -> ConcreteClass -> AbstractBase = exactly 2 base types
      Assert.Equal(2, baseTypes.Count);
      // Verify expected base types are present
      var baseTypeNames = baseTypes.Select(t => t.GetString("name")).ToList();
      Assert.Equivalent(new[] { "ConcreteClass", "AbstractBase" }, baseTypeNames);
   }

   [Fact]
   public async Task GetTypeHierarchy_ByName_WithNoExplicitBaseClass_ReturnsEmptyBaseTypes()
   {
      var target = new LookupByName { Name = "SealedClass" };
      var result = await _tool.GetTypeHierarchyAsync(target, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var baseTypes = data.GetArray("baseTypes");
      // System.Object is excluded, so should be empty
      Assert.Empty(baseTypes);
   }

   [Fact]
   public async Task GetTypeHierarchy_ByName_ForInterface_ReturnsInterfaceInfo()
   {
      var target = new LookupByName { Name = "IBasicService" };
      var result = await _tool.GetTypeHierarchyAsync(target, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Contains("IBasicService", data.GetString("typeName"));
   }

   [Fact]
   public async Task GetTypeHierarchy_ForInterface_ReturnsImplementingTypes()
   {
      var target = new LookupByName { Name = "IBasicService" };
      var result = await _tool.GetTypeHierarchyAsync(target, includeDerived: true, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var derivedTypes = data.GetArray("derivedTypes");
      var derivedTypeNames = derivedTypes.Select(t => t.GetString("name")).ToList();
      // IBasicService is implemented by BasicServiceImpl, ExplicitImplementation, and IncompleteServiceImpl
      // (PartialImplementation is abstract and may be excluded by the tool)
      Assert.Equivalent(new[] { "BasicServiceImpl", "ExplicitImplementation", "IncompleteServiceImpl" }, derivedTypeNames);
   }

   #endregion

   #region Position-Based Lookup Tests

   [Fact]
   public async Task GetTypeHierarchy_ByPosition_ReturnsInheritanceInfo()
   {
      var filePath = GetCoreTestProjectPath("Types/AbstractAndVirtual.cs");
      // Line 40 where ConcreteClass is defined: "public class ConcreteClass : AbstractBase"
      var target = new LookupByPosition { FilePath = filePath, Line = 40, Column = 14 };
      var result = await _tool.GetTypeHierarchyAsync(target, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      // Should find ConcreteClass and its base types
      var baseTypes = data.GetArray("baseTypes");
      Assert.NotEmpty(baseTypes);
      Assert.Contains("ConcreteClass", data.GetString("typeName"));
   }

   #endregion

   #region includeDerived Parameter Tests

   [Fact]
   public async Task GetTypeHierarchy_WithIncludeDerivedFalse_ExcludesDerivedTypes()
   {
      var target = new LookupByName { Name = "AbstractBase" };
      var result = await _tool.GetTypeHierarchyAsync(target, includeDerived: false, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      // derivedTypes should be null when includeDerived=false
      Assert.Null(data["derivedTypes"]);
      // derivedTypeCount should also be null
      Assert.Null(data["derivedTypeCount"]);
      // But base types should still be present (empty for abstract base class)
      var baseTypes = data.GetArray("baseTypes");
      Assert.NotNull(baseTypes);
   }

   [Fact]
   public async Task GetTypeHierarchy_WithIncludeDerivedTrue_ReturnsDerivedTypes()
   {
      var target = new LookupByName { Name = "AbstractBase" };
      var result = await _tool.GetTypeHierarchyAsync(target, includeDerived: true, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var derivedTypes = data.GetArray("derivedTypes");
      var derivedTypeNames = derivedTypes.Select(t => t.GetString("name")).ToList();
      // AbstractBase has only ConcreteClass as a direct derived type (DerivedClass derives from ConcreteClass)
      Assert.Equivalent(new[] { "ConcreteClass" }, derivedTypeNames);
   }

   [Fact]
   public async Task GetTypeHierarchy_RespectsMaxDerivedTypes()
   {
      var target = new LookupByName { Name = "AbstractBase" };
      var result = await _tool.GetTypeHierarchyAsync(target, includeDerived: true, maxDerivedTypes: 2, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var derivedTypes = data.GetArray("derivedTypes");
      Assert.True(derivedTypes.Count <= 2, "Should respect maxDerivedTypes limit");
   }

   #endregion

   #region Verbosity Tests

   [Fact]
   public async Task GetTypeHierarchy_WithSummaryVerbosity_ReturnsCountsOnly()
   {
      var target = new LookupByName { Name = "DerivedClass" };
      var result = await _tool.GetTypeHierarchyAsync(target, verbosity: ToolResultVerbosity.Summary, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      // DerivedClass -> ConcreteClass -> AbstractBase = 2 base types, 0 interfaces
      Assert.Equal(2, data.GetInt("baseTypeCount"));
      Assert.Equal(0, data.GetInt("interfaceCount"));
      // Should NOT have the arrays in summary mode
      Assert.Null(data["baseTypes"]);
      Assert.Null(data["interfaces"]);
   }

   [Fact]
   public async Task GetTypeHierarchy_WithCompactVerbosity_ReturnsLists()
   {
      var target = new LookupByName { Name = "DerivedClass" };
      var result = await _tool.GetTypeHierarchyAsync(target, verbosity: ToolResultVerbosity.Compact, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      // DerivedClass -> ConcreteClass -> AbstractBase = 2 base types, 0 interfaces
      Assert.Equal(2, data.GetInt("baseTypeCount"));
      var baseTypes = data.GetArray("baseTypes");
      Assert.Equal(2, baseTypes.Count);
      var interfaces = data.GetArray("interfaces");
      Assert.Empty(interfaces);
   }

   #endregion

   #region Error Cases

   [Fact]
   public async Task GetTypeHierarchy_WithUnknownType_ThrowsToolException()
   {
      var target = new LookupByName { Name = "NonExistentType12345" };
      await AssertThrowsToolExceptionAsync(
         () => _tool.GetTypeHierarchyAsync(target, cancellationToken: CT),
         "SYMBOL_NOT_FOUND");
   }

   [Fact]
   public async Task GetTypeHierarchy_ByPosition_WithInvalidFilePath_ThrowsToolException()
   {
      var target = new LookupByPosition { FilePath = "NonExistent/path/file.cs", Line = 1, Column = 1 };
      await AssertThrowsToolExceptionAsync(
         () => _tool.GetTypeHierarchyAsync(target, cancellationToken: CT),
         "FILE_NOT_IN_SOLUTION");
   }

   [Fact]
   public async Task GetTypeHierarchy_ByPosition_WithInvalidLine_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var target = new LookupByPosition { FilePath = filePath, Line = 99999, Column = 1 };
      await AssertThrowsToolExceptionAsync(
         () => _tool.GetTypeHierarchyAsync(target, cancellationToken: CT),
         "INVALID_PARAMETER");
   }

   #endregion
}
