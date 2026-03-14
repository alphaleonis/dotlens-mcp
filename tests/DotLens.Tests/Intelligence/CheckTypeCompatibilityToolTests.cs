using DotLens.Infrastructure;
using DotLens.Tools.Intelligence;
using Xunit;

namespace DotLens.Tests.Intelligence;

[Collection("TestFixtures")]
public class CheckTypeCompatibilityToolTests : TestFixtureTestBase
{
   public CheckTypeCompatibilityToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private CheckTypeCompatibilityTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      _tool = new CheckTypeCompatibilityTool(Workspace);
   }

   [Fact]
   public async Task CheckTypeCompatibility_WithCompatibleTypes_ReturnsTrue()
   {
      var result = await _tool.CheckTypeCompatibilityAsync("System.String", "System.Object", CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.True(data.GetBoolOrNull("compatible"));
   }

   [Fact]
   public async Task CheckTypeCompatibility_WithIncompatibleTypes_ReturnsFalse()
   {
      var result = await _tool.CheckTypeCompatibilityAsync("System.Int32", "System.String", CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.False(data.GetBoolOrNull("compatible"));
   }

   [Fact]
   public async Task CheckTypeCompatibility_WithInheritedTypes_ReturnsTrue()
   {
      // ConcreteClass : AbstractBase
      var result = await _tool.CheckTypeCompatibilityAsync(
         "CoreTestProject.Types.ConcreteClass",
         "CoreTestProject.Types.AbstractBase",
         CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.True(data.GetBoolOrNull("compatible"));
   }

   [Fact]
   public async Task CheckTypeCompatibility_WithInterfaceImplementation_ReturnsTrue()
   {
      // BasicServiceImpl : IBasicService (class is named BasicServiceImpl in test fixtures)
      var result = await _tool.CheckTypeCompatibilityAsync(
         "CoreTestProject.Types.BasicServiceImpl",
         "CoreTestProject.Types.IBasicService",
         CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.True(data.GetBoolOrNull("compatible"));
   }

   [Fact]
   public async Task CheckTypeCompatibility_WithTypesFromNonFirstProject_ReturnsCorrectResult()
   {
      // Dog : Animal are in InheritanceTestProject, which is NOT the first project in the solution.
      // The solution order is: AnalyzerTestProject, InheritanceTestProject, CoreTestProject
      // This test verifies the tool uses the correct project's compilation, not just First().
      var result = await _tool.CheckTypeCompatibilityAsync(
         "InheritanceTestProject.Dog",
         "InheritanceTestProject.Animal",
         CT);

      AssertSuccess(result);
      var data = GetData(result);
      // Dog inherits from Animal, so it should be compatible
      Assert.True(data.GetBoolOrNull("compatible") == true, "Dog should be compatible with Animal");
      Assert.True(data.GetBoolOrNull("isDirectlyAssignable") == true, "Dog should be directly assignable to Animal");
   }

   [Fact]
   public async Task CheckTypeCompatibility_WithUserDefinedImplicitConversion_DetectsConversion()
   {
      // Celsius has implicit operator to Fahrenheit - defined in InheritanceTestProject (not the First() project)
      // This test verifies the tool correctly detects user-defined conversions.
      // Bug: Using First() project's compilation won't see the conversion operators.
      var result = await _tool.CheckTypeCompatibilityAsync(
         "InheritanceTestProject.Celsius",
         "InheritanceTestProject.Fahrenheit",
         CT);

      AssertSuccess(result);
      var data = GetData(result);
      // Should detect the implicit user-defined conversion
      Assert.True(data.GetBoolOrNull("compatible") == true, "Celsius should be convertible to Fahrenheit via implicit operator");
      Assert.True(data.GetBoolOrNull("isImplicit") == true, "Conversion should be implicit");
      Assert.Contains("user-defined", data.GetStringOrNull("explanation")?.ToLower() ?? "", StringComparison.OrdinalIgnoreCase);
   }

   [Fact]
   public async Task CheckTypeCompatibility_WithUserDefinedExplicitConversion_DetectsConversion()
   {
      // Fahrenheit has explicit operator to Celsius - defined in InheritanceTestProject (not the First() project)
      var result = await _tool.CheckTypeCompatibilityAsync(
         "InheritanceTestProject.Fahrenheit",
         "InheritanceTestProject.Celsius",
         CT);

      AssertSuccess(result);
      var data = GetData(result);
      // Should detect the explicit user-defined conversion
      Assert.True(data.GetBoolOrNull("compatible") == true, "Fahrenheit should be convertible to Celsius via explicit operator");
      Assert.True(data.GetBoolOrNull("requiresCast") == true, "Conversion should require cast (explicit)");
   }

   [Fact]
   public async Task CheckTypeCompatibility_WithAmbiguousTypeName_ThrowsAmbiguousTypeError()
   {
      // CoreTestProject.Types.AmbiguousTestClass exists in BOTH CoreTestProject and ConflictingTypesProject
      // with the same fully-qualified name. This should trigger an ambiguity error.
      var ex = await Assert.ThrowsAsync<ToolException>(async () =>
         await _tool.CheckTypeCompatibilityAsync(
            "CoreTestProject.Types.AmbiguousTestClass",
            "System.Object",
            CT));

      Assert.Equal(ErrorCodes.AmbiguousType, ex.Code);
      // Error code is sufficient - removed fragile message assertion
   }

   [Fact]
   public async Task CheckTypeCompatibility_WithUniqueTypeFromConflictingProject_Succeeds()
   {
      // UniqueConflictingType only exists in ConflictingTypesProject, no ambiguity
      var result = await _tool.CheckTypeCompatibilityAsync(
         "ConflictingTypesProject.UniqueConflictingType",
         "System.Object",
         CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.True(data.GetBoolOrNull("compatible"));
   }

   [Fact]
   public async Task CheckTypeCompatibility_WithAmbiguousSourceOnly_ThrowsAmbiguousTypeError()
   {
      // AmbiguousTestClass is defined in both CoreTestProject and ConflictingTypesProject
      // with the same FQN but different implementations.
      // Even when target is unambiguous (System.String), source ambiguity should fail.
      var ex = await Assert.ThrowsAsync<ToolException>(async () =>
         await _tool.CheckTypeCompatibilityAsync(
            "CoreTestProject.Types.AmbiguousTestClass", // Ambiguous source
            "System.String",                             // Unambiguous target
            CT));

      Assert.Equal(ErrorCodes.AmbiguousType, ex.Code);
   }

   [Fact]
   public async Task CheckTypeCompatibility_WithFrameworkSourceAndProjectTarget_ChecksVisibility()
   {
      // When source is a framework type (System.String), the target lookup
      // happens from the framework's compilation. Project types like AmbiguousTestClass
      // are not visible from there, resulting in TYPE_NOT_FOUND.
      // This verifies that the "lookup target from source compilation" logic works correctly.
      var ex = await Assert.ThrowsAsync<ToolException>(async () =>
         await _tool.CheckTypeCompatibilityAsync(
            "System.String",                             // Framework type as source
            "CoreTestProject.Types.AmbiguousTestClass", // Project type as target
            CT));

      Assert.Equal(ErrorCodes.TypeNotFound, ex.Code);
   }

   [Fact]
   public async Task CheckTypeCompatibility_WithTargetNotVisibleFromSourceProject_ThrowsTypeNotFound()
   {
      // CoreTestProject has no reference to InheritanceTestProject, so InheritanceTestProject.Animal
      // is not visible from CoreTestProject's compilation.
      // This tests that we correctly report when types are in different, unrelated projects.
      var ex = await Assert.ThrowsAsync<ToolException>(async () =>
         await _tool.CheckTypeCompatibilityAsync(
            "CoreTestProject.Types.ConcreteClass",      // Source in CoreTestProject
            "InheritanceTestProject.Animal",             // Target in InheritanceTestProject (no reference)
            CT));

      Assert.Equal(ErrorCodes.TypeNotFound, ex.Code);
   }

   [Fact]
   public async Task CheckTypeCompatibility_WithFrameworkType_AlwaysVisible()
   {
      // Framework types like System.Object should always be visible from any compilation.
      // This tests that looking up target from source compilation still works for BCL types.
      var result = await _tool.CheckTypeCompatibilityAsync(
         "InheritanceTestProject.Animal",   // Source from any project
         "System.Object",                    // Target is BCL type
         CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.True(data.GetBoolOrNull("compatible") == true, "Any class should be compatible with System.Object");
   }

   [Fact]
   public async Task CheckTypeCompatibility_WithSameProjectTypes_BothSymbolsFromSameCompilation()
   {
      // When source and target are in the same project, both should come from the same compilation.
      // This is the normal case where SymbolEqualityComparer.Default works correctly.
      var result = await _tool.CheckTypeCompatibilityAsync(
         "InheritanceTestProject.Dog",
         "InheritanceTestProject.Animal",
         CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.True(data.GetBoolOrNull("compatible"));
      Assert.True(data.GetBoolOrNull("isDirectlyAssignable") == true,
         "IsDirectlyAssignable should work correctly when both symbols are from the same compilation");
   }
}
