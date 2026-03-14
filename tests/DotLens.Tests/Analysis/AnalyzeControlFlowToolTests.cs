using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tests;
using DotLens.Tools.Analysis;
using Xunit;

namespace DotLens.Analysis.Tests;

[Collection("TestFixtures")]
public class AnalyzeControlFlowToolTests : TestFixtureTestBase
{
   public AnalyzeControlFlowToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private AnalyzeControlFlowTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      var symbolLookup = new SymbolLookupService(Workspace);
      _tool = new AnalyzeControlFlowTool(Workspace, symbolLookup);
   }

   [Fact]
   public async Task AnalyzeControlFlow_ByMethod_AnalyzesEntireMethodBody()
   {
      // Test the method-based input mode - analyze SumRange method
      var target = new ControlFlowTargetByMethod
      {
         Method = new LookupByName { Name = "ControlFlowExamples.SumRange" }
      };

      var result = await _tool.AnalyzeControlFlowAsync(target, CT);

      AssertSuccess(result);
      var data = GetData(result);

      // Should have region info
      var region = data["region"];
      Assert.NotNull(region);

      // Should analyze the control flow
      Assert.NotNull(data["entryPoints"]?.AsArray());
      Assert.NotNull(data["exitPoints"]?.AsArray());
      Assert.NotNull(data["returnStatements"]?.AsArray());

      // Return statements should include the method's return
      var returnStatements = data["returnStatements"]?.AsArray();
      Assert.NotNull(returnStatements);
      Assert.NotEmpty(returnStatements);
   }

   [Fact]
   public async Task AnalyzeControlFlow_ByLines_OnConditionalMethod_ReturnsFlowInfo()
   {
      // Use CountDigits method - file line 90 (1-based): "number = Math.Abs(number);"
      // Single statement for simplest valid control flow analysis
      var filePath = GetCoreTestProjectPath("Patterns/ControlFlow.cs");
      var target = new ControlFlowTargetByLines
      {
         FilePath = filePath,
         StartLine = 90,
         EndLine = 90
      };

      var result = await _tool.AnalyzeControlFlowAsync(target, CT);

      AssertSuccess(result);
      var data = GetData(result);

      // Should have region info
      var region = data["region"];
      Assert.NotNull(region);

      // Should have control flow analysis results
      Assert.NotNull(data["entryPoints"]?.AsArray());
      Assert.NotNull(data["exitPoints"]?.AsArray());
   }

   [Fact]
   public async Task AnalyzeControlFlow_ByLines_FileNotFound_ThrowsToolException()
   {
      // File not in solution - throws ToolException with FileNotInSolution code
      var target = new ControlFlowTargetByLines
      {
         FilePath = "nonexistent.cs",
         StartLine = 1,
         EndLine = 10
      };

      var ex = await Assert.ThrowsAsync<ToolException>(() =>
         _tool.AnalyzeControlFlowAsync(target, CT));
      Assert.Equal(ErrorCodes.FileNotInSolution, ex.Code);
   }

   [Fact]
   public async Task AnalyzeControlFlow_ByLines_MethodBodyWithLoop_ShouldAnalyzeSuccessfully()
   {
      // Regression test for "statements not within the same statement list" bug
      // SumRange method in ControlFlow.cs (1-based lines 77-82):
      //   line 77: int sum = 0;
      //   line 78: for (int i = start; i <= end; i++)
      //   line 79: {
      //   line 80:    sum += i;
      //   line 81: }
      //   line 82: return sum;
      var filePath = GetCoreTestProjectPath("Patterns/ControlFlow.cs");
      var target = new ControlFlowTargetByLines
      {
         FilePath = filePath,
         StartLine = 77,
         EndLine = 82
      };

      var result = await _tool.AnalyzeControlFlowAsync(target, CT);

      AssertSuccess(result);
      var data = GetData(result);

      // Should have analyzed the control flow
      Assert.NotNull(data["entryPoints"]?.AsArray());
      Assert.NotNull(data["exitPoints"]?.AsArray());
      var returnStatements = data["returnStatements"]?.AsArray();
      Assert.NotNull(returnStatements);
      Assert.NotEmpty(returnStatements);

      // EndPointIsReachable should be false because the method ends with a return statement
      // that's always executed (it returns sum)
      Assert.False(data.GetBoolOrNull("endPointIsReachable"));
   }

   [Fact]
   public async Task AnalyzeControlFlow_ByMethod_NotFound_ThrowsException()
   {
      // Try to analyze a method that doesn't exist
      var target = new ControlFlowTargetByMethod
      {
         Method = new LookupByName { Name = "NonExistentClass.NonExistentMethod" }
      };

      var ex = await Assert.ThrowsAsync<ToolException>(() =>
         _tool.AnalyzeControlFlowAsync(target, CT));

      Assert.Equal(ErrorCodes.SymbolNotFound, ex.Code);
   }

   [Fact]
   public async Task AnalyzeControlFlow_ByMethod_ReachabilityInfo()
   {
      // CountDigits method: has early return on line 88, final return on line 99
      var target = new ControlFlowTargetByMethod
      {
         Method = new LookupByName { Name = "ControlFlowExamples.CountDigits" }
      };

      var result = await _tool.AnalyzeControlFlowAsync(target, CT);

      AssertSuccess(result);
      var data = GetData(result);

      // Start point is always reachable
      Assert.True(data.GetBoolOrNull("startPointIsReachable"));

      // End point is NOT reachable because both paths end with return statements
      Assert.False(data.GetBoolOrNull("endPointIsReachable"));
   }

   [Fact]
   public async Task AnalyzeControlFlow_ByMethod_ExpressionBodied_ThrowsToolException()
   {
      // GetDayType is expression-bodied (uses switch expression)
      var target = new ControlFlowTargetByMethod
      {
         Method = new LookupByName { Name = "ControlFlowExamples.GetDayType" }
      };

      var ex = await Assert.ThrowsAsync<ToolException>(() =>
         _tool.AnalyzeControlFlowAsync(target, CT));

      Assert.Equal(ErrorCodes.AnalysisFailed, ex.Code);
   }

   [Fact]
   public async Task AnalyzeControlFlow_ByMethod_MetadataSymbol_ThrowsToolException()
   {
      // Attempting to analyze a BCL method should fail with SymbolInMetadata
      var target = new ControlFlowTargetByMethod
      {
         Method = new LookupByName { Name = "System.String.IsNullOrEmpty" }
      };

      var ex = await Assert.ThrowsAsync<ToolException>(() =>
         _tool.AnalyzeControlFlowAsync(target, CT));

      Assert.Equal(ErrorCodes.SymbolInMetadata, ex.Code);
   }
}
