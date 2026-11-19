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

   [Fact]
   public async Task Cognitive_RecursionCycleAddsOnePerMethod()
   {
      Assert.Equal<(string, int?, int?)>(
         [
            ("IsEvenCount", 6, 2),
            ("IsOddCount", 9, 2),
            ("IsEvenEntry", 12, 0),
            ("StepA", 15, 2),
            ("StepB", 18, 1),
            ("StepC", 21, 1),
            ("Walk", 24, 2),
            ("Wander", 31, 1),
            ("Bounce", 34, 4),
            ("Serve", 38, 2),
            ("Volley", 41, 1),
            ("Launch", 45, 0),
            ("PassOn", 48, 1),
            ("Proceed", 54, 0),
            ("Proceed", 59, 0),
            ("Relay", 62, 0),
         ],
         await GetCognitiveComplexitiesAsync("Analysis/CognitiveIndirectRecursion.cs"));
   }

   /// <summary>
   /// Each type closes a cycle, or fails to, through another form of call: a constructed generic, a partial method, a
   /// nested local function, an operator, an extension method, a constructor initializer, a virtual or interface call,
   /// or a compiler-generated record member.
   /// </summary>
   [Fact]
   public async Task Cognitive_RecursionCycleMatchesEachCallToItsDeclaration()
   {
      Assert.Equal<(string, int?, int?)>(
         [
            ("Outward", 6, 2),
            ("Inward", 9, 1),
            ("TurnLeft", 15, 1),
            ("TurnRight", 18, 2),
            ("Descend", 24, 0),
            ("Descend", 26, 2),
            ("Climb", 29, 1),
            ("Spiral", 35, 5),
            ("EnterLevel", 39, 4),
            ("LeaveLevel", 43, 2),
            ("Defer", 48, 1),
            ("CallsBack", 51, 0),
            ("Back", 56, 0),
            ("Apply", 60, 0),
            (".ctor", 66, 0),
            ("op_Explicit", 71, 2),
            ("op_Addition", 74, 1),
            ("Halve", 80, 2),
            ("Shrink", 83, 1),
            ("Fold", 91, 2),
            ("Unfold", 94, 1),
            ("Spin", 100, 2),
            (".ctor", 107, 0),
            (".ctor", 109, 1),
            (".ctor", 114, 2),
            (".ctor", 123, 2),
            (".ctor", 132, 1),
            ("Visit", 140, 2),
            ("Revisit", 143, 1),
            ("Visit", 149, 1),
            ("VisitAgain", 152, 1),
            ("Visit", 158, 0),
            ("VisitAsBase", 161, 0),
            ("Tick", 167, 2),
            ("Tock", 170, 1),
            ("Equals", 176, 1),
            ("GetHashCode", 179, 0),
         ],
         await GetCognitiveComplexitiesAsync("Analysis/CognitiveRecursionIdentity.cs"));
   }

   [Fact]
   public async Task Cognitive_RecursionCycleRunsThroughAccessors()
   {
      Assert.Equal<(string, int?, int?)>(
         [
            ("ReadComputed", 11, 1),
            ("AddTally", 20, 1),
            ("IncrementCounter", 29, 1),
            ("AssignSettable", 38, 1),
            ("ReadSettable", 41, 0),
            ("Plant", 50, 1),
            ("SwapIn", 59, 1),
            ("Lookup", 68, 1),
            ("Store", 77, 1),
            ("LookupLong", 83, 1),
            ("LastItem", 89, 1),
            ("Subscribe", 98, 1),
            ("Unsubscribe", 101, 1),
            ("ReadShared", 107, 1),
            ("ReadDeferred", 113, 1),
            ("SlotOwner", 119, 1),
            ("ReadPlain", 127, 0),
            ("NameOfNamed", 133, 0),
            ("ReadEcho", 142, 0),
            ("WriteMirror", 151, 0),
            ("Evaluate", 157, 0),
            ("MeasureExact", 171, 1),
            ("MeasureAny", 174, 0),
            (".ctor", 185, 0),
            ("Rebuild", 188, 0),
            ("Reseed", 203, 0),
         ],
         await GetCognitiveComplexitiesAsync("Analysis/CognitiveAccessorRecursion.cs"));
   }

   [Fact]
   public async Task Cognitive_RecursionCycleRunsThroughCallsTheCompilerInserts()
   {
      Assert.Equal<(string, int?, int?)>(
         [
            ("GetEnumerator", 8, 1),
            ("DrainSource", 11, 2),
            ("MoveNext", 23, 0),
            ("GetEnumerator", 30, 0),
            ("DrainMoveNext", 33, 2),
            ("MoveNext", 45, 1),
            ("GetEnumerator", 52, 0),
            ("DrainCurrent", 55, 2),
            ("MoveNext", 67, 0),
            ("GetEnumerator", 74, 0),
            ("DrainDispose", 77, 2),
            ("MoveNext", 89, 0),
            ("Dispose", 92, 1),
            ("GetAsyncEnumerator", 99, 0),
            ("DrainAsync", 102, 2),
            ("MoveNextAsync", 114, 1),
            ("GetAwaiter", 121, 0),
            ("AwaitResult", 124, 1),
            ("GetResult", 132, 1),
            ("OnCompleted", 135, 0),
            ("Deconstruct", 143, 1),
            ("Split", 146, 1),
            ("Deconstruct", 155, 1),
            ("SplitAll", 158, 2),
            ("Deconstruct", 168, 2),
            ("Match", 171, 1),
            ("Add", 177, 1),
            ("FillWithInitializer", 180, 1),
            ("GetEnumerator", 183, 0),
            ("System.Collections.IEnumerable.GetEnumerator", 186, 0),
            (".ctor", 192, 1),
            ("Build", 195, 1),
            ("Add", 198, 0),
            ("GetEnumerator", 202, 0),
            ("System.Collections.IEnumerable.GetEnumerator", 205, 0),
            ("op_Implicit", 213, 1),
            ("ToCelsius", 216, 1),
            ("op_True", 225, 0),
            ("op_False", 228, 1),
            ("op_BitwiseAnd", 231, 0),
            ("Check", 234, 2),
            (".ctor", 243, 1),
            (".ctor", 253, 2),
            ("Sow", 268, 1),
            (".ctor", 276, 1),
            ("Grow", 280, 1),
            ("Equals", 286, 2),
            ("Matches", 289, 1),
            ("Differs", 292, 0),
            ("GetHashCode", 295, 0),
            ("Equals", 301, 1),
            ("Differs", 304, 1),
            ("GetHashCode", 307, 0),
            ("Describe", 316, 1),
            ("Unpack", 330, 1),
            (".ctor", 339, 1),
            ("Revise", 342, 1),
            (".ctor", 348, 1),
            ("Show", 366, 1),
            ("Equals", 372, 1),
            ("GetHashCode", 375, 1),
            ("Compare", 381, 1),
            ("Hash", 384, 1),
            ("Probe", 393, 1),
            ("op_True", 402, 1),
            ("op_False", 405, 0),
            ("op_BitwiseOr", 408, 0),
            ("Flip", 411, 2),
            (".ctor", 420, 0),
            (".ctor", 423, 1),
            (".ctor", 433, 1),
            (".ctor", 436, 0),
            (".ctor", 447, 1),
            (".ctor", 457, 1),
            (".ctor", 465, 1),
            (".ctor", 479, 1),
            (".ctor", 493, 1),
         ],
         await GetCognitiveComplexitiesAsync("Analysis/CognitiveImplicitCallRecursion.cs"));
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

   [Fact]
   public async Task Cognitive_MethodTarget_CountsRecursionCycle()
   {
      var result = await _tool.GetComplexityMetricsAsync(
         new ComplexityTargetMethod { Method = new LookupByName { Name = "CognitiveIndirectRecursion.StepB" } },
         metrics: [MetricType.Cognitive],
         cancellationToken: CT);

      var data = Assert.IsType<MethodComplexityResponse>(result.Data);
      Assert.Equal("StepB", data.Name);
      Assert.Equal(1, data.Metrics.Cognitive);
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
