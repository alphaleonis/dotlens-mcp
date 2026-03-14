using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Analysis;
using Xunit;

namespace DotLens.Tests.Analysis;

[Collection("TestFixtures")]
public class GetComplexityMetricsToolTests : TestFixtureTestBase
{
   public GetComplexityMetricsToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetComplexityMetricsTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      var symbolLookup = new SymbolLookupService(Workspace);
      _tool = new GetComplexityMetricsTool(Workspace, symbolLookup);
   }

   [Fact]
   public async Task GetComplexityMetrics_File_ReturnsMetrics()
   {
      var filePath = GetCoreTestProjectPath("Patterns/ControlFlow.cs");
      var result = await _tool.GetComplexityMetricsAsync(
         new ComplexityTargetFile { FilePath = filePath },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.NotNull(GetNode(data, "Methods"));
      Assert.True(GetProperty<int>(data, "MethodCount") > 0);
   }

   [Fact]
   public async Task GetComplexityMetrics_SpecificMethod_ReturnsMetrics()
   {
      var filePath = GetCoreTestProjectPath("Patterns/ControlFlow.cs");
      // Line 15, column 7 is inside the Categorize method body (on the 'if' statement)
      var result = await _tool.GetComplexityMetricsAsync(
         new ComplexityTargetMethod
         {
            Method = new LookupByPosition { FilePath = filePath, Line = 15, Column = 7 }
         },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.NotNull(GetNode(data, "Metrics"));
   }

   [Fact]
   public async Task GetComplexityMetrics_InvalidPosition_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      await AssertThrowsToolExceptionAsync(
         () => _tool.GetComplexityMetricsAsync(
            new ComplexityTargetMethod
            {
               Method = new LookupByPosition { FilePath = filePath, Line = 99999, Column = 0 }
            },
            cancellationToken: CT),
         "INVALID_PARAMETER");
   }

   [Fact]
   public async Task GetComplexityMetrics_FileNotFound_ThrowsException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.GetComplexityMetricsAsync(
            new ComplexityTargetFile { FilePath = "NonExistent/file.cs" },
            cancellationToken: CT),
         "FILE_NOT_IN_SOLUTION");
   }

   [Fact]
   public async Task GetComplexityMetrics_SpecificMetrics_ReturnsOnlyRequested()
   {
      var filePath = GetCoreTestProjectPath("Patterns/ControlFlow.cs");
      var result = await _tool.GetComplexityMetricsAsync(
         new ComplexityTargetFile { FilePath = filePath },
         metrics: [MetricType.Cyclomatic, MetricType.Loc],
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var methods = GetNode(data, "Methods")?.AsArray();
      Assert.NotNull(methods);
      Assert.NotEmpty(methods);

      // Check that only requested metrics are populated
      var firstMethod = methods[0];
      var metrics = GetNode(firstMethod, "Metrics");
      Assert.NotNull(metrics);
      Assert.NotNull(GetProperty<int>(metrics, "Cyclomatic"));
      Assert.NotNull(GetProperty<int>(metrics, "Loc"));
      // Nesting, Parameters, Cognitive should be null (not requested)
      Assert.Null(GetProperty<int>(metrics, "Nesting"));
      Assert.Null(GetProperty<int>(metrics, "Parameters"));
      Assert.Null(GetProperty<int>(metrics, "Cognitive"));
   }

   [Fact]
   public async Task GetComplexityMetrics_SortByCyclomatic_ReturnsSortedResults()
   {
      var filePath = GetCoreTestProjectPath("Patterns/ControlFlow.cs");
      var result = await _tool.GetComplexityMetricsAsync(
         new ComplexityTargetFile { FilePath = filePath },
         sortBy: ComplexitySortBy.Cyclomatic,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var methods = GetNode(data, "Methods")?.AsArray();
      Assert.NotNull(methods);
      Assert.True(methods.Count >= 2, "Need at least 2 methods to test sorting");

      // Verify descending order by cyclomatic complexity
      var complexities = methods
         .Select(m => GetProperty<int>(GetNode(m, "Metrics"), "Cyclomatic") ?? 0)
         .ToList();

      for (int i = 0; i < complexities.Count - 1; i++)
      {
         Assert.True(complexities[i] >= complexities[i + 1],
            $"Methods not sorted by cyclomatic complexity: {complexities[i]} < {complexities[i + 1]}");
      }
   }

   [Fact]
   public async Task GetComplexityMetrics_WithThreshold_FiltersResults()
   {
      var filePath = GetCoreTestProjectPath("Patterns/ControlFlow.cs");
      var target = new ComplexityTargetFile { FilePath = filePath };

      // First get all methods to find a reasonable threshold
      var allResult = await _tool.GetComplexityMetricsAsync(target, cancellationToken: CT);
      AssertSuccess(allResult);
      var allData = GetData(allResult);
      var totalCount = GetProperty<int>(allData, "MethodCount");

      // Now filter with a threshold
      var result = await _tool.GetComplexityMetricsAsync(
         target,
         threshold: 2, // Only methods with cyclomatic >= 2
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);

      // FilteredCount should be populated
      var filteredCount = GetProperty<int>(data, "FilteredCount");
      Assert.NotNull(filteredCount);
      Assert.True(filteredCount <= totalCount, "Filtered count should be <= total count");

      // All returned methods should meet threshold
      var methods = GetNode(data, "Methods")?.AsArray();
      Assert.NotNull(methods);
      Assert.NotEmpty(methods);
      foreach (var method in methods)
      {
         var cyclomatic = GetProperty<int>(GetNode(method, "Metrics"), "Cyclomatic") ?? 0;
         Assert.True(cyclomatic >= 2, $"Method has cyclomatic {cyclomatic} < threshold 2");
      }
   }

   [Fact]
   public async Task GetComplexityMetrics_SummaryVerbosity_OmitsMethods()
   {
      var filePath = GetCoreTestProjectPath("Patterns/ControlFlow.cs");
      var result = await _tool.GetComplexityMetricsAsync(
         new ComplexityTargetFile { FilePath = filePath },
         verbosity: ToolResultVerbosity.Summary,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);

      // Should have totals but no individual methods
      Assert.NotNull(GetNode(data, "FileTotals"));
      Assert.True(GetProperty<int>(data, "MethodCount") > 0);
      Assert.Null(GetNode(data, "Methods")); // Null in Summary mode
   }

   [Fact]
   public async Task GetComplexityMetrics_MaxMethods_LimitsResults()
   {
      var filePath = GetCoreTestProjectPath("Patterns/ControlFlow.cs");
      var result = await _tool.GetComplexityMetricsAsync(
         new ComplexityTargetFile { FilePath = filePath },
         maxMethods: 2,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var methods = GetNode(data, "Methods")?.AsArray();
      Assert.NotNull(methods);
      Assert.True(methods.Count <= 2, $"Expected at most 2 methods, got {methods.Count}");

      // MethodCount should still reflect total methods in file
      var methodCount = GetProperty<int>(data, "MethodCount");
      Assert.True(methodCount >= methods.Count, "MethodCount should be >= returned methods");
   }

   [Fact]
   public async Task GetComplexityMetrics_FileTotals_IncludesCognitiveMetrics()
   {
      var filePath = GetCoreTestProjectPath("Patterns/ControlFlow.cs");
      var result = await _tool.GetComplexityMetricsAsync(
         new ComplexityTargetFile { FilePath = filePath },
         metrics: [MetricType.Cognitive],
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var fileTotals = GetNode(data, "FileTotals");
      Assert.NotNull(fileTotals);

      // Should have cognitive metrics in totals
      Assert.NotNull(GetProperty<double>(fileTotals, "AvgCognitive"));
      Assert.NotNull(GetProperty<int>(fileTotals, "MaxCognitive"));

      // Should NOT have cyclomatic metrics (not requested)
      Assert.Null(GetProperty<double>(fileTotals, "AvgCyclomatic"));
      Assert.Null(GetProperty<int>(fileTotals, "MaxCyclomatic"));
   }

   [Fact]
   public async Task GetComplexityMetrics_File_IncludesAllCallableMethodKinds()
   {
      // MethodKinds.cs has: static constructor, instance constructor, destructor,
      // implicit operator, explicit operator, user-defined operator, ordinary method,
      // local function, and explicit interface implementation
      var filePath = GetCoreTestProjectPath("Types/MethodKinds.cs");
      var result = await _tool.GetComplexityMetricsAsync(
         new ComplexityTargetFile { FilePath = filePath },
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var methods = GetNode(data, "Methods")?.AsArray();
      Assert.NotNull(methods);

      // Should find all callable methods: 2 constructors (static + instance), destructor,
      // 2 conversion operators, 1 user-defined operator, 1 ordinary method with local function,
      // and 1 explicit interface implementation = at least 8 methods
      // (local functions may or may not be included depending on implementation)
      var methodCount = GetProperty<int>(data, "MethodCount");
      Assert.True(methodCount >= 8, $"Expected at least 8 callable methods, got {methodCount}");

      // Verify constructors are included by checking for names
      var methodNames = methods.Select(m => GetStringProperty(m, "Name")).ToList();
      Assert.Contains(".ctor", methodNames); // Instance constructor
      Assert.Contains(".cctor", methodNames); // Static constructor
   }
}
