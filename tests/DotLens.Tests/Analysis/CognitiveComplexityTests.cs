using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Analysis;
using Xunit;

namespace DotLens.Tests.Analysis;

/// <summary>
/// Covers cognitive complexity as SonarSource's Cognitive Complexity white paper defines it, on the
/// fixtures in <c>CoreTestProject/Analysis/Cognitive*.cs</c>, whose comments give each increment.
/// </summary>
[Collection("TestFixtures")]
public class CognitiveComplexityTests : TestFixtureTestBase
{
   public CognitiveComplexityTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetComplexityMetricsTool _tool = null!;

   public override ValueTask InitializeAsync()
   {
      _tool = new GetComplexityMetricsTool(Workspace, new SymbolLookupService(Workspace));
      return ValueTask.CompletedTask;
   }

   [Fact]
   public async Task Cognitive_GotoAddsFundamentalIncrement()
   {
      Assert.Equal<(string, int?, int?)>(
         [("FirstNegative", 6, 4), ("DescribeCode", 21, 3)],
         await GetCognitiveComplexitiesAsync("Analysis/CognitiveJumps.cs"));
   }

   [Fact]
   public async Task Cognitive_DoWhileAndDeconstructingForEachAreLoops()
   {
      Assert.Equal<(string, int?, int?)>(
         [("CountDigits", 6, 1), ("SumUntilZero", 19, 6), ("SumProducts", 38, 3)],
         await GetCognitiveComplexitiesAsync("Analysis/CognitiveLoops.cs"));
   }

   [Fact]
   public async Task Cognitive_ElseIfAndElseAddOneWithoutNestingPenalty()
   {
      Assert.Equal<(string, int?, int?)>(
         [("Sign", 6, 3), ("Classify", 16, 7)],
         await GetCognitiveComplexitiesAsync("Analysis/CognitiveElseChains.cs"));
   }

   [Fact]
   public async Task Cognitive_CountsSequencesOfLikeLogicalOperators()
   {
      Assert.Equal<(string, int?, int?)>(
         [
            ("AllPositive", 6, 1),
            ("MixedSequences", 9, 4),
            ("NegatedGroup", 16, 3),
            ("ParenthesizedSequence", 23, 1),
            ("ParenthesizedMix", 26, 2),
            ("InSmallRange", 29, 2),
            ("IsSmallPrime", 32, 1),
         ],
         await GetCognitiveComplexitiesAsync("Analysis/CognitiveLogicalOperators.cs"));
   }

   [Fact]
   public async Task Cognitive_LambdasAndLocalFunctionsRaiseNesting()
   {
      Assert.Equal<(string, int?, int?)>(
         [("MakeClamp", 6, 2), ("MakeLogger", 16, 2), ("SumPositive", 25, 3), ("Add", 32, 1), ("CountAbove", 39, 4)],
         await GetCognitiveComplexitiesAsync("Analysis/CognitiveNestedFunctions.cs"));
   }

   [Fact]
   public async Task Cognitive_NestedTernaryGetsNestingPenalty()
   {
      Assert.Equal<(string, int?, int?)>(
         [("Compare", 6, 3)],
         await GetCognitiveComplexitiesAsync("Analysis/CognitiveTernaries.cs"));
   }

   [Fact]
   public async Task Cognitive_SwitchExpressionCountsLikeSwitch()
   {
      Assert.Equal<(string, int?, int?)>(
         [("DescribeSign", 6, 1), ("DescribeAll", 13, 6)],
         await GetCognitiveComplexitiesAsync("Analysis/CognitiveSwitchExpressions.cs"));
   }

   [Fact]
   public async Task Cognitive_DirectRecursionAddsOnePerMethod()
   {
      Assert.Equal<(string, int?, int?)>(
         [
            ("Factorial", 6, 2),
            ("Fibonacci", 13, 2),
            ("SumFrom", 16, 0),
            ("SumFrom", 19, 2),
            ("CountNodes", 26, 3),
            ("Count", 30, 2),
            ("Height", 37, 1),
            ("Countdown", 43, 0),
            ("Countdown", 45, 2),
         ],
         await GetCognitiveComplexitiesAsync("Analysis/CognitiveRecursion.cs"));
   }

   /// <summary>
   /// A single-method target resolves recursion through its own semantic model.
   /// </summary>
   [Fact]
   public async Task Cognitive_MethodTarget_CountsDirectRecursion()
   {
      var result = await _tool.GetComplexityMetricsAsync(
         new ComplexityTargetMethod { Method = new LookupByName { Name = "CognitiveRecursion.Factorial" } },
         metrics: [MetricType.Cognitive],
         cancellationToken: CT);

      var data = Assert.IsType<MethodComplexityResponse>(result.Data);
      Assert.Equal("Factorial", data.Name);
      Assert.Equal(2, data.Metrics.Cognitive);
   }

   private async Task<List<(string Name, int? Line, int? Cognitive)>> GetCognitiveComplexitiesAsync(string relativePath)
   {
      var result = await _tool.GetComplexityMetricsAsync(
         new ComplexityTargetFile { FilePath = GetCoreTestProjectPath(relativePath) },
         metrics: [MetricType.Cognitive],
         cancellationToken: CT);

      var data = Assert.IsType<FileComplexityResponse>(result.Data);
      Assert.NotNull(data.Methods);
      return data.Methods.Select(m => (m.Name, m.Line, m.Metrics.Cognitive)).ToList();
   }
}
