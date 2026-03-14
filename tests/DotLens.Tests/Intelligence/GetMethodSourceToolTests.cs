using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Intelligence;
using DotLens.Tools.Shared;
using Xunit;

namespace DotLens.Tests.Intelligence;

[Collection("TestFixtures")]
public class GetMethodSourceToolTests : TestFixtureTestBase
{
   public GetMethodSourceToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetMethodSourceTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      var symbolLookup = new SymbolLookupService(Workspace);
      _tool = new GetMethodSourceTool(Workspace, symbolLookup);
   }

   [Fact]
   public async Task GetMethodSource_ByName_ReturnsSourceCode()
   {
      var result = await _tool.GetMethodSourceAsync(
         new LookupByName { Name = "SampleClass.Calculate" },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Contains("Calculate", GetStringProperty(data, "Source"));
   }

   [Fact]
   public async Task GetMethodSource_ByPosition_ReturnsSourceCode()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      // SampleClass.Calculate is at line 48 in Classes.cs
      var result = await _tool.GetMethodSourceAsync(
         new LookupByPosition { FilePath = filePath, Line = 48, Column = 15 },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Contains("Calculate", GetStringProperty(data, "Source"));
   }

   [Fact]
   public async Task GetMethodSource_FullVerbosity_ReturnsSource()
   {
      var result = await _tool.GetMethodSourceAsync(
         new LookupByName { Name = "SampleClass.DoSomething" },
         verbosity: ToolResultVerbosity.Full,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var source = GetStringProperty(data, "Source");
      Assert.False(string.IsNullOrEmpty(source));
      Assert.Contains("DoSomething", source);
   }

   [Fact]
   public async Task GetMethodSource_WithAsyncMethod_ReturnsSource()
   {
      var result = await _tool.GetMethodSourceAsync(
         new LookupByName { Name = "AsyncService.DoWorkAsync" },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Contains("async", GetStringProperty(data, "Source"));
   }

   [Fact]
   public async Task GetMethodSource_OverloadedMethod_ThrowsAmbiguous()
   {
      // With overloadIndex removed, ambiguous lookups should throw
      await AssertThrowsToolExceptionAsync(
         () => _tool.GetMethodSourceAsync(
            new LookupByName { Name = "SampleClass.Format" },
            cancellationToken: CT),
         "AMBIGUOUS_SYMBOL");
   }

   [Fact]
   public async Task GetMethodSource_SymbolNotFound_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.GetMethodSourceAsync(
            new LookupByName { Name = "NonexistentType.Method" },
            cancellationToken: CT),
         "SYMBOL_NOT_FOUND");
   }

   [Fact]
   public async Task GetMethodSource_CompactVerbosity_StripsLeadingWhitespace()
   {
      // Compact is default - strips leading whitespace
      var compactResult = await _tool.GetMethodSourceAsync(
         new LookupByName { Name = "SampleClass.Calculate" },
         verbosity: ToolResultVerbosity.Compact,
         cancellationToken: CT);

      // Full verbosity preserves original formatting
      var fullResult = await _tool.GetMethodSourceAsync(
         new LookupByName { Name = "SampleClass.Calculate" },
         verbosity: ToolResultVerbosity.Full,
         cancellationToken: CT);

      AssertSuccess(compactResult);
      AssertSuccess(fullResult);

      var compactSource = GetStringProperty(GetData(compactResult), "Source");
      var fullSource = GetStringProperty(GetData(fullResult), "Source");

      // Compact output should be shorter or equal (less whitespace)
      Assert.True(compactSource.Length <= fullSource.Length,
         "Compact output should be shorter or equal in length");

      // Both should contain the same code content
      Assert.Contains("Calculate", compactSource);
   }

   #region Method Kind Tests

   [Fact]
   public async Task GetMethodSource_Constructor_ReturnsSource()
   {
      var filePath = GetCoreTestProjectPath("Types/MethodKinds.cs");
      // Instance constructor at line 18: "   public MethodKindsFixture()"
      // Position on "MethodKindsFixture" at column 11
      var result = await _tool.GetMethodSourceAsync(
         new LookupByPosition { FilePath = filePath, Line = 18, Column = 11 },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Contains("MethodKindsFixture()", GetStringProperty(data, "Source"));
   }

   [Fact]
   public async Task GetMethodSource_StaticConstructor_ReturnsSource()
   {
      var filePath = GetCoreTestProjectPath("Types/MethodKinds.cs");
      // Static constructor is at line 12
      var result = await _tool.GetMethodSourceAsync(
         new LookupByPosition { FilePath = filePath, Line = 12, Column = 5 },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Contains("static", GetStringProperty(data, "Source"));
      Assert.Contains("_initialized = true", GetStringProperty(data, "Source"));
   }

   [Fact]
   public async Task GetMethodSource_Destructor_ReturnsSource()
   {
      var filePath = GetCoreTestProjectPath("Types/MethodKinds.cs");
      // Destructor is at line 23
      var result = await _tool.GetMethodSourceAsync(
         new LookupByPosition { FilePath = filePath, Line = 23, Column = 5 },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Contains("~MethodKindsFixture", GetStringProperty(data, "Source"));
   }

   [Fact]
   public async Task GetMethodSource_ImplicitConversionOperator_ReturnsSource()
   {
      var filePath = GetCoreTestProjectPath("Types/MethodKinds.cs");
      // Implicit conversion operator is at line 29
      var result = await _tool.GetMethodSourceAsync(
         new LookupByPosition { FilePath = filePath, Line = 29, Column = 20 },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Contains("implicit operator string", GetStringProperty(data, "Source"));
   }

   [Fact]
   public async Task GetMethodSource_ExplicitConversionOperator_ReturnsSource()
   {
      var filePath = GetCoreTestProjectPath("Types/MethodKinds.cs");
      // Explicit conversion operator is at line 35
      var result = await _tool.GetMethodSourceAsync(
         new LookupByPosition { FilePath = filePath, Line = 35, Column = 20 },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Contains("explicit operator int", GetStringProperty(data, "Source"));
   }

   [Fact]
   public async Task GetMethodSource_UserDefinedOperator_ReturnsSource()
   {
      var filePath = GetCoreTestProjectPath("Types/MethodKinds.cs");
      // User-defined + operator at line 41: "   public static MethodKindsFixture operator +(...)"
      // Position on 'operator' keyword at column 40
      var result = await _tool.GetMethodSourceAsync(
         new LookupByPosition { FilePath = filePath, Line = 41, Column = 40 },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Contains("operator +", GetStringProperty(data, "Source"));
   }

   [Fact]
   public async Task GetMethodSource_LocalFunction_ReturnsSource()
   {
      var filePath = GetCoreTestProjectPath("Types/MethodKinds.cs");
      // Local function at line 52: "      int LocalDouble(int n) => n * 2;"
      // Position on "LocalDouble" at column 11
      var result = await _tool.GetMethodSourceAsync(
         new LookupByPosition { FilePath = filePath, Line = 52, Column = 11 },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Contains("LocalDouble", GetStringProperty(data, "Source"));
   }

   [Fact]
   public async Task GetMethodSource_ExplicitInterfaceImplementation_ReturnsSource()
   {
      var filePath = GetCoreTestProjectPath("Types/MethodKinds.cs");
      // Explicit impl at line 70: "   void IMethodKinds.DoWork()"
      // Position on "DoWork" at column 24
      var result = await _tool.GetMethodSourceAsync(
         new LookupByPosition { FilePath = filePath, Line = 70, Column = 24 },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Contains("IMethodKinds.DoWork", GetStringProperty(data, "Source"));
   }

   #endregion
}
