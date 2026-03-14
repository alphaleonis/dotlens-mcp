using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tests;
using DotLens.Tools.Analysis;
using Xunit;

namespace DotLens.Analysis.Tests;

[Collection("TestFixtures")]
public class AnalyzeDataFlowToolTests : TestFixtureTestBase
{
   public AnalyzeDataFlowToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private AnalyzeDataFlowTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      var symbolLookup = new SymbolLookupService(Workspace);
      _tool = new AnalyzeDataFlowTool(Workspace, symbolLookup);
   }

   [Fact]
   public async Task AnalyzeDataFlow_ByMethod_AnalyzesEntireMethodBody()
   {
      // Test the method-based input mode - analyze SumRange method
      var target = new DataFlowTargetByMethod
      {
         Method = new LookupByName { Name = "ControlFlowExamples.SumRange" }
      };

      var result = await _tool.AnalyzeDataFlowAsync(target, CT);

      AssertSuccess(result);
      var data = GetData(result);

      // Should detect the local variables
      var variablesDeclared = data["variablesDeclared"]?.AsArray();
      Assert.NotNull(variablesDeclared);
      Assert.NotEmpty(variablesDeclared);
      Assert.Contains(variablesDeclared, v => v.GetStringValue() == "sum");
      Assert.Contains(variablesDeclared, v => v.GetStringValue() == "i");

      // Should detect data flowing in (parameters)
      var dataFlowsIn = data["dataFlowsIn"]?.AsArray();
      Assert.NotNull(dataFlowsIn);
      Assert.NotEmpty(dataFlowsIn);
      Assert.Contains(dataFlowsIn, v => v.GetStringValue() == "start");
      Assert.Contains(dataFlowsIn, v => v.GetStringValue() == "end");
   }

   [Fact]
   public async Task AnalyzeDataFlow_ByLines_OnLinqExpression_ReturnsFlowInfo()
   {
      // Use GetActiveProductNames method body - file lines 20-22 (1-based)
      var filePath = GetCoreTestProjectPath("Patterns/LinqPatterns.cs");
      var target = new DataFlowTargetByLines
      {
         FilePath = filePath,
         StartLine = 20,
         EndLine = 22
      };

      var result = await _tool.AnalyzeDataFlowAsync(target, CT);

      AssertSuccess(result);
      var data = GetData(result);

      // Should have region info
      var region = data["region"];
      Assert.NotNull(region);
      Assert.Equal(20, region.GetIntOrNull("startLine"));
      Assert.Equal(22, region.GetIntOrNull("endLine"));

      // Should have data flow analysis results (non-null arrays)
      Assert.NotNull(data["variablesDeclared"]?.AsArray());
      Assert.NotNull(data["dataFlowsIn"]?.AsArray());
      Assert.NotNull(data["readInside"]?.AsArray());
   }

   [Fact]
   public async Task AnalyzeDataFlow_ByLines_FileNotFound_ThrowsException()
   {
      // File not in solution - throws ToolException with FILE_NOT_IN_SOLUTION
      var target = new DataFlowTargetByLines
      {
         FilePath = "nonexistent.cs",
         StartLine = 1,
         EndLine = 10
      };

      await AssertThrowsToolExceptionAsync(() =>
         _tool.AnalyzeDataFlowAsync(target, CT),
         ErrorCodes.FileNotInSolution);
   }

   [Fact]
   public async Task AnalyzeDataFlow_ByLines_MethodBodyWithLoop_ShouldAnalyzeSuccessfully()
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
      var target = new DataFlowTargetByLines
      {
         FilePath = filePath,
         StartLine = 77,
         EndLine = 82
      };

      var result = await _tool.AnalyzeDataFlowAsync(target, CT);

      AssertSuccess(result);
      var data = GetData(result);

      // Should detect the local variables
      var variablesDeclared = data["variablesDeclared"]?.AsArray();
      Assert.NotNull(variablesDeclared);
      Assert.NotEmpty(variablesDeclared);
      Assert.Contains(variablesDeclared, v => v.GetStringValue() == "sum");
   }

   [Fact]
   public async Task AnalyzeDataFlow_ByMethod_NotFound_ThrowsException()
   {
      // Try to analyze a method that doesn't exist
      var target = new DataFlowTargetByMethod
      {
         Method = new LookupByName { Name = "NonExistentClass.NonExistentMethod" }
      };

      var ex = await Assert.ThrowsAsync<ToolException>(() =>
         _tool.AnalyzeDataFlowAsync(target, CT));

      Assert.Equal(ErrorCodes.SymbolNotFound, ex.Code);
   }
}
