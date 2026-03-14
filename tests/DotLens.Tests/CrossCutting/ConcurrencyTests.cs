using System.Text.Json;
using System.Text.Json.Nodes;
using DotLens.Infrastructure;
using DotLens.Infrastructure.Results;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Analysis;
using DotLens.Tools.Navigation;
using DotLens.Tools.Search;
using DotLens.Tools.Shared;
using Xunit;

namespace DotLens.Tests.CrossCutting;

/// <summary>
/// Tests for concurrent access scenarios.
/// Verifies thread-safety of workspace and tool operations.
/// </summary>
public class ConcurrencyTests : IsolatedProjectTestBase
{

   private GetSymbolInfoTool _getSymbolInfo = null!;
   private FindReferencesTool _findReferences = null!;
   private FindSymbolsTool _findSymbols = null!;
   private GetTypeMembersTool _getTypeMembers = null!;
   private GetDiagnosticsTool _getDiagnostics = null!;

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      var symbolLookup = new SymbolLookupService(WorkspaceManager);
      _getSymbolInfo = new GetSymbolInfoTool(WorkspaceManager, symbolLookup);
      _findReferences = new FindReferencesTool(WorkspaceManager, symbolLookup);
      _findSymbols = new FindSymbolsTool(WorkspaceManager);
      _getTypeMembers = new GetTypeMembersTool(WorkspaceManager, symbolLookup);
      _getDiagnostics = new GetDiagnosticsTool(WorkspaceManager);
   }

   #region Concurrent Read Operations

   [Fact]
   public async Task ConcurrentSymbolSearches_AllSucceed()
   {
      // Run multiple symbol searches concurrently
      var tasks = new[]
      {
         _findSymbols.FindSymbolsAsync(name: "*Service", cancellationToken: CT),
         _findSymbols.FindSymbolsAsync(name: "*Class*", cancellationToken: CT),
         _findSymbols.FindSymbolsAsync(name: "*Enum", cancellationToken: CT),
         _findSymbols.FindSymbolsAsync(name: "*Interface", cancellationToken: CT),
         _findSymbols.FindSymbolsAsync(name: "*Helper", cancellationToken: CT)
      };

      var results = await Task.WhenAll(tasks);

      // All should complete without errors
      foreach (var result in results)
      {
         var json = JsonNode.Parse(JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase))!;
         Assert.NotNull(json["data"]);  // Success indicated by presence of data
      }
   }

   [Fact]
   public async Task ConcurrentGetSymbolInfo_AllSucceed()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      // Run multiple GetSymbolInfo calls on different lines concurrently
      // Some may throw ToolException if no symbol at position - that's OK for concurrency testing
      var tasks = Enumerable.Range(12, 10)
          .Select(async line =>
          {
             try
             {
                return await _getSymbolInfo.GetSymbolInfoAsync(
                    new LookupByPosition { FilePath = filePath, Line = line, Column = 0 },
                    cancellationToken: CT);
             }
             catch (ToolException)
             {
                // No symbol at position is acceptable for concurrency testing
                return null;
             }
          })
          .ToArray();

      var results = await Task.WhenAll(tasks);

      // All should complete without crashing (null means no symbol found, which is OK)
      Assert.Equal(10, results.Length);
   }

   [Fact]
   public async Task ConcurrentDiagnosticChecks_AllSucceed()
   {
      var classesPath = GetCoreTestProjectPath("Types/Classes.cs");
      var asyncPath = GetCoreTestProjectPath("Patterns/AsyncPatterns.cs");
      // Run diagnostics on multiple files concurrently
      var tasks = new[]
      {
         _getDiagnostics.GetDiagnosticsAsync(filePath: classesPath, cancellationToken: CT),
         _getDiagnostics.GetDiagnosticsAsync(filePath: asyncPath, cancellationToken: CT),
         _getDiagnostics.GetDiagnosticsAsync(cancellationToken: CT) // All projects
      };

      var results = await Task.WhenAll(tasks);

      foreach (var result in results)
      {
         var json = JsonNode.Parse(JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase))!;
         Assert.NotNull(json["data"]);  // Success indicated by presence of data
      }
   }

   #endregion

   #region Concurrent Mixed Operations

   [Fact]
   public async Task ConcurrentMixedReadOperations_AllSucceed()
   {
      var classesPath = GetCoreTestProjectPath("Types/Classes.cs");
      var asyncPath = GetCoreTestProjectPath("Patterns/AsyncPatterns.cs");
      // Mix of different read operations running concurrently
      // Some operations may throw ToolException if no symbol found - that's OK
      var tasks = new Task<object?>[]
      {
         Task.Run(async () => (object?)await _findSymbols.FindSymbolsAsync(name: "*", maxResults: 10, cancellationToken: CT)),
         Task.Run<object?>(async () =>
         {
            try { return await _getSymbolInfo.GetSymbolInfoAsync(new LookupByPosition { FilePath = classesPath, Line = 15, Column = 10 }, cancellationToken: CT); }
            catch (ToolException) { return null; }
         }),
         Task.Run(async () => (object?)await _getDiagnostics.GetDiagnosticsAsync(filePath: classesPath, cancellationToken: CT)),
         Task.Run(async () => (object?)await _getTypeMembers.GetTypeMembersAsync(new LookupByName { Name = "SampleClass" }, cancellationToken: CT)),
         Task.Run<object?>(async () =>
         {
            try { return await _findReferences.FindReferencesAsync(new LookupByPosition { FilePath = asyncPath, Line = 30, Column = 1 }, cancellationToken: CT); }
            catch (ToolException) { return null; }
         })
      };

      var results = await Task.WhenAll(tasks);

      // All should complete without exceptions (null is acceptable for "no symbol found")
      Assert.Equal(5, results.Length);
   }

   [Fact]
   public async Task HighConcurrencySymbolLookups_Stable()
   {
      // Simulate high concurrency with many simultaneous lookups
      const int concurrentCalls = 20;

      var tasks = Enumerable.Range(0, concurrentCalls)
          .Select(i => _findSymbols.FindSymbolsAsync(
              name: $"*{(char)('A' + (i % 26))}*",
              maxResults: 5,
              cancellationToken: CT))
          .ToArray();

      var results = await Task.WhenAll(tasks);

      // All should complete
      Assert.Equal(concurrentCalls, results.Length);
   }

   #endregion

   #region Document Cache Concurrency

   [Fact]
   public async Task ConcurrentDocumentAccess_CacheStable()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      // Access the same document from multiple operations concurrently
      // Some may throw ToolException if no symbol at position - that's OK for cache testing
      var tasks = Enumerable.Range(1, 10)
          .Select(async i =>
          {
             try
             {
                return await _getSymbolInfo.GetSymbolInfoAsync(
                    new LookupByPosition { FilePath = filePath, Line = 10 + i * 2, Column = 0 },
                    cancellationToken: CT);
             }
             catch (ToolException)
             {
                // No symbol at position is acceptable for cache testing
                return null;
             }
          })
          .ToArray();

      var results = await Task.WhenAll(tasks);

      // Document cache should handle concurrent access (null means no symbol found, which is OK)
      Assert.Equal(10, results.Length);
   }

   [Fact]
   public async Task ConcurrentSyncDocuments_Stable()
   {
      var classesPath = GetCoreTestProjectPath("Types/Classes.cs");
      var asyncPath = GetCoreTestProjectPath("Patterns/AsyncPatterns.cs");
      // Sync operations running concurrently shouldn't corrupt state
      var tasks = new[]
      {
         WorkspaceManager.SyncDocumentsAsync(null),
         WorkspaceManager.SyncDocumentsAsync([classesPath]),
         WorkspaceManager.SyncDocumentsAsync([asyncPath])
      };

      var results = await Task.WhenAll(tasks);

      foreach (var result in results)
      {
         var json = JsonNode.Parse(JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase))!;
         Assert.NotNull(json["data"]);  // Success indicated by presence of data
      }
   }

   #endregion

   #region Stress Tests

   [Fact]
   public async Task RapidSuccessiveCalls_NoRaceConditions()
   {
      // Rapid successive calls to test for race conditions
      var tasks = new List<Task<ToolResult<PagedResponse<SymbolResponse>>>>();

      for (int i = 0; i < 50; i++)
      {
         tasks.Add(_findSymbols.FindSymbolsAsync(name: "*", maxResults: 1, cancellationToken: CT));
      }

      var results = await Task.WhenAll(tasks);

      // All should succeed - errors would throw ToolException
      Assert.Equal(50, results.Length);
   }

   [Fact]
   public async Task ParallelAnalysisOperations_ThreadSafe()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      // Run analysis operations in parallel
      var symbolLookup = new SymbolLookupService(WorkspaceManager);
      var analyzeDataFlow = new AnalyzeDataFlowTool(WorkspaceManager, symbolLookup);
      var analyzeControlFlow = new AnalyzeControlFlowTool(WorkspaceManager, symbolLookup);
      var getComplexityMetrics = new GetComplexityMetricsTool(WorkspaceManager, symbolLookup);

      // Create operations that handle ToolException (analysis may fail with invalid ranges)
      // Line numbers are 1-based
      var operations = new Func<Task<object?>>[]
      {
         async () => await _getDiagnostics.GetDiagnosticsAsync(cancellationToken: CT),
         async () =>
         {
            try
            {
               var target = new DataFlowTargetByLines { FilePath = filePath, StartLine = 49, EndLine = 52 };
               return await analyzeDataFlow.AnalyzeDataFlowAsync(target, CT);
            }
            catch (ToolException ex) when (ex.Code == ErrorCodes.AnalysisFailed)
            {
               return null; // Expected: line range may not contain analyzable statements
            }
         },
         async () =>
         {
            try
            {
               var target = new ControlFlowTargetByLines { FilePath = filePath, StartLine = 49, EndLine = 52 };
               return await analyzeControlFlow.AnalyzeControlFlowAsync(target, CT);
            }
            catch (ToolException ex) when (ex.Code == ErrorCodes.AnalysisFailed)
            {
               return null; // Expected: line range may not contain analyzable statements
            }
         },
         async () => await getComplexityMetrics.GetComplexityMetricsAsync(
            new ComplexityTargetFile { FilePath = filePath },
            cancellationToken: CT)
      };

      // Run each operation 3 times in parallel
      var tasks = operations
          .SelectMany(op => Enumerable.Range(0, 3).Select(_ => op()))
          .ToArray();

      var results = await Task.WhenAll(tasks);

      // All should complete (null indicates analysis failure which is OK for concurrency)
      Assert.Equal(12, results.Length);
   }

   #endregion
}
