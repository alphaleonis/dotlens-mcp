using DotLens.Infrastructure;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using Xunit;

namespace DotLens.Tests.Infrastructure;

/// <summary>
/// Covers which solution <see cref="MultiWorkerManager"/> makes active when loads and requests interleave.
/// </summary>
public sealed class MultiWorkerManagerActiveSolutionTests : IAsyncDisposable
{
   private static readonly string SolutionA = Path.Combine(Path.GetTempPath(), "DotLensActiveSolution", "A.slnx");
   private static readonly string SolutionB = Path.Combine(Path.GetTempPath(), "DotLensActiveSolution", "B.slnx");
   private static readonly string SolutionC = Path.Combine(Path.GetTempPath(), "DotLensActiveSolution", "C.slnx");

   private readonly ControlledWorkerLoads _loads = new();
   private readonly MultiWorkerManager _manager;

   public MultiWorkerManagerActiveSolutionTests()
   {
      _manager = new MultiWorkerManager(
         NullLogger<MultiWorkerManager>.Instance, Options.Create(new DotLensOptions()), _loads.LoadAsync);
   }

   private static CancellationToken CT => TestContext.Current.CancellationToken;

   public ValueTask DisposeAsync() => _manager.DisposeAsync();

   private async Task<LoadSolutionResult> LoadAsync(string solutionPath)
   {
      var load = _manager.LoadSolutionAsync(solutionPath, CT);
      _loads.Complete(solutionPath);
      return await load;
   }

   [Fact]
   public async Task ExplicitLoadFinishesBeforeAutoLoad_ExplicitLoadStaysActive()
   {
      var autoLoad = _manager.AutoLoadSolutionAsync(SolutionA, CT);
      var explicitLoad = _manager.LoadSolutionAsync(SolutionB, CT);

      _loads.Complete(SolutionB);
      var explicitResult = await explicitLoad;
      _loads.Complete(SolutionA);
      var autoResult = await autoLoad;

      Assert.True(explicitResult.IsActive);
      Assert.False(autoResult.IsActive);
      Assert.Equal(SolutionB, autoResult.ActiveSolutionPath);
      Assert.Equal(SolutionB, _manager.ActiveSolutionPath);
      Assert.Equivalent(new[] { SolutionA, SolutionB }, _manager.Workers.Keys);
   }

   [Fact]
   public async Task AutoLoadWithNoExplicitRequest_BecomesActive()
   {
      var autoLoad = _manager.AutoLoadSolutionAsync(SolutionA, CT);

      _loads.Complete(SolutionA);
      var result = await autoLoad;

      Assert.True(result.IsActive);
      Assert.False(result.WasAlreadyLoaded);
      Assert.Equal(SolutionA, _manager.ActiveSolutionPath);
   }

   [Fact]
   public async Task TwoExplicitLoadsFinishInReverseOrder_LaterRequestStaysActive()
   {
      var loadA = _manager.LoadSolutionAsync(SolutionA, CT);
      var loadB = _manager.LoadSolutionAsync(SolutionB, CT);

      _loads.Complete(SolutionB);
      var resultB = await loadB;
      _loads.Complete(SolutionA);
      var resultA = await loadA;

      Assert.True(resultB.IsActive);
      Assert.False(resultA.IsActive);
      Assert.Equal(SolutionB, resultA.ActiveSolutionPath);
      Assert.Equal(SolutionB, _manager.ActiveSolutionPath);
      Assert.Equivalent(new[] { SolutionA, SolutionB }, _manager.Workers.Keys);
   }

   [Fact]
   public async Task TwoExplicitLoadsFinishInOrder_LaterRequestBecomesActive()
   {
      var loadA = _manager.LoadSolutionAsync(SolutionA, CT);
      var loadB = _manager.LoadSolutionAsync(SolutionB, CT);

      _loads.Complete(SolutionA);
      var resultA = await loadA;
      _loads.Complete(SolutionB);
      var resultB = await loadB;

      Assert.True(resultA.IsActive);
      Assert.True(resultB.IsActive);
      Assert.Equal(SolutionB, _manager.ActiveSolutionPath);
   }

   [Fact]
   public async Task SetActiveSolutionWhileLoadInFlight_SetSolutionStaysActive()
   {
      await LoadAsync(SolutionC);
      await LoadAsync(SolutionB);
      var loadA = _manager.LoadSolutionAsync(SolutionA, CT);

      _manager.SetActiveSolution(SolutionC);
      _loads.Complete(SolutionA);
      var resultA = await loadA;

      Assert.False(resultA.IsActive);
      Assert.Equal(SolutionC, _manager.ActiveSolutionPath);
   }

   [Fact]
   public async Task ExplicitLoadJoinsAutoLoadOfSamePath_PathBecomesActive()
   {
      var autoLoad = _manager.AutoLoadSolutionAsync(SolutionA, CT);
      await LoadAsync(SolutionB);
      var explicitLoad = _manager.LoadSolutionAsync(SolutionA, CT);

      _loads.Complete(SolutionA);
      var explicitResult = await explicitLoad;
      var autoResult = await autoLoad;

      Assert.True(explicitResult.IsActive);
      Assert.True(autoResult.IsActive);
      Assert.Same(autoResult.Instance, explicitResult.Instance);
      Assert.Equal(1, _loads.CallCount(SolutionA));
      Assert.Equal(SolutionA, _manager.ActiveSolutionPath);
   }

   [Fact]
   public async Task ExplicitCallerStopsWaitingOnJoinedAutoLoad_PathBecomesActiveWhenLoadFinishes()
   {
      var autoLoad = _manager.AutoLoadSolutionAsync(SolutionA, CT);
      await LoadAsync(SolutionB);
      using var callerTimeout = CancellationTokenSource.CreateLinkedTokenSource(CT);
      var explicitLoad = _manager.LoadSolutionAsync(SolutionA, callerTimeout.Token);

      await callerTimeout.CancelAsync();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => explicitLoad);
      _loads.Complete(SolutionA);
      var autoResult = await autoLoad;

      Assert.True(autoResult.IsActive);
      Assert.Equal(SolutionA, _manager.ActiveSolutionPath);
   }

   [Fact]
   public async Task ExplicitLoadFails_PreviousActiveSolutionStays()
   {
      await LoadAsync(SolutionC);
      var loadB = _manager.LoadSolutionAsync(SolutionB, CT);

      _loads.Fail(SolutionB, new InvalidOperationException("worker_load_solution failed"));
      var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => loadB);

      Assert.Equal("worker_load_solution failed", ex.Message);
      Assert.Equal(SolutionC, _manager.ActiveSolutionPath);
      Assert.Equivalent(new[] { SolutionC }, _manager.Workers.Keys);
      Assert.Equivalent(new Dictionary<string, string> { [SolutionB] = "worker_load_solution failed" }, _manager.FailedLoads);
   }

   /// <summary>
   /// A request whose load fails does not count, so the latest request that succeeds names the active solution.
   /// </summary>
   [Fact]
   public async Task LatestExplicitLoadFails_EarlierLoadInFlightBecomesActive()
   {
      await LoadAsync(SolutionC);
      var loadA = _manager.LoadSolutionAsync(SolutionA, CT);
      var loadB = _manager.LoadSolutionAsync(SolutionB, CT);

      _loads.Fail(SolutionB, new InvalidOperationException("worker_load_solution failed"));
      await Assert.ThrowsAsync<InvalidOperationException>(() => loadB);
      _loads.Complete(SolutionA);
      var resultA = await loadA;

      Assert.True(resultA.IsActive);
      Assert.Equal(SolutionA, _manager.ActiveSolutionPath);
   }

   [Fact]
   public async Task UnloadActiveSolutionWhileEarlierLoadInFlight_EarlierLoadFinishesAfterUnload_BecomesActive()
   {
      var loadA = _manager.LoadSolutionAsync(SolutionA, CT);
      await LoadAsync(SolutionB);

      await _manager.UnloadSolutionAsync(SolutionB, CT);
      var activeAfterUnload = _manager.ActiveSolutionPath;
      _loads.Complete(SolutionA);
      var resultA = await loadA;

      Assert.Null(activeAfterUnload);
      Assert.True(resultA.IsActive);
      Assert.Equal(SolutionA, _manager.ActiveSolutionPath);
      Assert.Equivalent(new[] { SolutionA }, _manager.Workers.Keys);
   }

   [Fact]
   public async Task UnloadActiveSolutionAfterEarlierLoadFinished_EarlierLoadBecomesActive()
   {
      var loadA = _manager.LoadSolutionAsync(SolutionA, CT);
      await LoadAsync(SolutionB);

      _loads.Complete(SolutionA);
      var resultA = await loadA;
      await _manager.UnloadSolutionAsync(SolutionB, CT);

      Assert.False(resultA.IsActive);
      Assert.Equal(SolutionB, resultA.ActiveSolutionPath);
      Assert.Equal(SolutionA, _manager.ActiveSolutionPath);
      Assert.Equivalent(new[] { SolutionA }, _manager.Workers.Keys);
   }

   /// <summary>
   /// C and A start in that order while B is active, and both finish after B is unloaded, C first.
   /// </summary>
   [Fact]
   public async Task TwoEarlierLoadsFinishAfterActiveSolutionUnloaded_LaterRequestBecomesActive()
   {
      var loadC = _manager.LoadSolutionAsync(SolutionC, CT);
      var loadA = _manager.LoadSolutionAsync(SolutionA, CT);
      await LoadAsync(SolutionB);

      await _manager.UnloadSolutionAsync(SolutionB, CT);
      _loads.Complete(SolutionC);
      var resultC = await loadC;
      _loads.Complete(SolutionA);
      var resultA = await loadA;

      Assert.True(resultC.IsActive);
      Assert.True(resultA.IsActive);
      Assert.Equal(SolutionA, _manager.ActiveSolutionPath);
   }

   [Fact]
   public async Task AutoLoadJoinedByExplicitRequest_FinishesAfterActiveSolutionUnloaded_BecomesActive()
   {
      var autoLoad = _manager.AutoLoadSolutionAsync(SolutionA, CT);
      var explicitLoad = _manager.LoadSolutionAsync(SolutionA, CT);
      await LoadAsync(SolutionB);

      await _manager.UnloadSolutionAsync(SolutionB, CT);
      _loads.Complete(SolutionA);
      var explicitResult = await explicitLoad;
      await autoLoad;

      Assert.True(explicitResult.IsActive);
      Assert.Equal(SolutionA, _manager.ActiveSolutionPath);
   }

   [Fact]
   public async Task AutoLoadJoinedByExplicitRequest_LaterRequestActive_LaterRequestStaysActive()
   {
      var autoLoad = _manager.AutoLoadSolutionAsync(SolutionA, CT);
      var explicitLoad = _manager.LoadSolutionAsync(SolutionA, CT);
      await LoadAsync(SolutionB);

      _loads.Complete(SolutionA);
      var explicitResult = await explicitLoad;
      var autoResult = await autoLoad;

      Assert.False(explicitResult.IsActive);
      Assert.False(autoResult.IsActive);
      Assert.Equal(SolutionB, _manager.ActiveSolutionPath);
   }

   [Fact]
   public async Task AutoLoadFinishesAfterExplicitSolutionWasUnloaded_BecomesActive()
   {
      var autoLoad = _manager.AutoLoadSolutionAsync(SolutionA, CT);
      await LoadAsync(SolutionB);

      await _manager.UnloadSolutionAsync(SolutionB, CT);
      _loads.Complete(SolutionA);
      var autoResult = await autoLoad;

      Assert.True(autoResult.IsActive);
      Assert.Equal(SolutionA, _manager.ActiveSolutionPath);
      Assert.Equivalent(new[] { SolutionA }, _manager.Workers.Keys);
   }

   [Fact]
   public async Task AutoLoadFinishesBeforeExplicitSolutionIsUnloaded_BecomesActiveOnUnload()
   {
      var autoLoad = _manager.AutoLoadSolutionAsync(SolutionA, CT);
      await LoadAsync(SolutionB);

      _loads.Complete(SolutionA);
      var autoResult = await autoLoad;
      await _manager.UnloadSolutionAsync(SolutionB, CT);

      Assert.False(autoResult.IsActive);
      Assert.Equal(SolutionB, autoResult.ActiveSolutionPath);
      Assert.Equal(SolutionA, _manager.ActiveSolutionPath);
   }

   /// <summary>
   /// C is loaded by request 1, A is pending with request 2, and B is active with request 3 when B is unloaded.
   /// </summary>
   [Fact]
   public async Task NewerLoadFinishesAfterUnloadWithOlderSolutionLoaded_NewerLoadBecomesActive()
   {
      await LoadAsync(SolutionC);
      var loadA = _manager.LoadSolutionAsync(SolutionA, CT);
      await LoadAsync(SolutionB);

      await _manager.UnloadSolutionAsync(SolutionB, CT);
      var activeAfterUnload = _manager.ActiveSolutionPath;
      _loads.Complete(SolutionA);
      var resultA = await loadA;

      Assert.Equal(SolutionC, activeAfterUnload);
      Assert.True(resultA.IsActive);
      Assert.Equal(SolutionA, _manager.ActiveSolutionPath);
   }

   /// <summary>
   /// The same requests as <see cref="NewerLoadFinishesAfterUnloadWithOlderSolutionLoaded_NewerLoadBecomesActive"/>, with A
   /// finishing before the unload.
   /// </summary>
   [Fact]
   public async Task NewerLoadFinishesBeforeUnloadWithOlderSolutionLoaded_NewerLoadBecomesActiveOnUnload()
   {
      await LoadAsync(SolutionC);
      var loadA = _manager.LoadSolutionAsync(SolutionA, CT);
      await LoadAsync(SolutionB);

      _loads.Complete(SolutionA);
      var resultA = await loadA;
      await _manager.UnloadSolutionAsync(SolutionB, CT);

      Assert.False(resultA.IsActive);
      Assert.Equal(SolutionB, resultA.ActiveSolutionPath);
      Assert.Equal(SolutionA, _manager.ActiveSolutionPath);
   }

   [Fact]
   public async Task UnloadActiveSolution_TwoRemain_LatestNamedSolutionBecomesActive()
   {
      await LoadAsync(SolutionA);
      await LoadAsync(SolutionC);
      await LoadAsync(SolutionB);

      await _manager.UnloadSolutionAsync(SolutionB, CT);

      Assert.Equal(SolutionC, _manager.ActiveSolutionPath);
      Assert.Equivalent(new[] { SolutionA, SolutionC }, _manager.Workers.Keys);
   }

   /// <summary>
   /// A's load is requested before B's but finishes after it, so neither its load number nor the load order picks A.
   /// </summary>
   [Fact]
   public async Task UnloadActiveSolution_SetActiveNamedOlderSolutionLater_ThatSolutionBecomesActive()
   {
      var loadA = _manager.LoadSolutionAsync(SolutionA, CT);
      await LoadAsync(SolutionB);
      _loads.Complete(SolutionA);
      await loadA;
      await LoadAsync(SolutionC);

      _manager.SetActiveSolution(SolutionA);
      _manager.SetActiveSolution(SolutionC);
      await _manager.UnloadSolutionAsync(SolutionC, CT);

      Assert.Equal(SolutionA, _manager.ActiveSolutionPath);
   }

   /// <summary>
   /// A's load is requested before B's but finishes after it, so neither its load number nor the load order picks A.
   /// </summary>
   [Fact]
   public async Task UnloadActiveSolution_LoadNamedAlreadyLoadedSolutionLater_ThatSolutionBecomesActive()
   {
      var loadA = _manager.LoadSolutionAsync(SolutionA, CT);
      await LoadAsync(SolutionB);
      _loads.Complete(SolutionA);
      await loadA;
      await LoadAsync(SolutionC);

      await _manager.LoadSolutionAsync(SolutionA, CT);
      _manager.SetActiveSolution(SolutionC);
      await _manager.UnloadSolutionAsync(SolutionC, CT);

      Assert.Equal(SolutionA, _manager.ActiveSolutionPath);
   }

   [Fact]
   public async Task UnloadActiveSolution_OneRemains_ItBecomesActive()
   {
      await LoadAsync(SolutionA);
      await LoadAsync(SolutionB);

      await _manager.UnloadSolutionAsync(SolutionB, CT);

      Assert.Equal(SolutionA, _manager.ActiveSolutionPath);
   }

   [Fact]
   public async Task UnloadActiveSolution_NoneRemain_NoSolutionActive()
   {
      await LoadAsync(SolutionA);

      await _manager.UnloadSolutionAsync(SolutionA, CT);

      Assert.Null(_manager.ActiveSolutionPath);
      Assert.Empty(_manager.Workers);
   }

   [Fact]
   public async Task UnloadInactiveSolution_ActiveSolutionStays()
   {
      await LoadAsync(SolutionA);
      await LoadAsync(SolutionB);
      await LoadAsync(SolutionC);

      await _manager.UnloadSolutionAsync(SolutionB, CT);

      Assert.Equal(SolutionC, _manager.ActiveSolutionPath);
      Assert.Equivalent(new[] { SolutionA, SolutionC }, _manager.Workers.Keys);
   }

   /// <summary>
   /// After the unload, A is active with its own request number, so the older load of C does not replace it.
   /// </summary>
   [Fact]
   public async Task UnloadActiveSolution_ReplacementNewerThanLoadInFlight_ReplacementStaysActive()
   {
      var loadC = _manager.LoadSolutionAsync(SolutionC, CT);
      await LoadAsync(SolutionA);
      await LoadAsync(SolutionB);

      await _manager.UnloadSolutionAsync(SolutionB, CT);
      _loads.Complete(SolutionC);
      var resultC = await loadC;

      Assert.False(resultC.IsActive);
      Assert.Equal(SolutionA, resultC.ActiveSolutionPath);
      Assert.Equal(SolutionA, _manager.ActiveSolutionPath);
   }

   /// <summary>
   /// Unloads and loads B again the way reload_solution does, while an older load of A is in flight.
   /// </summary>
   [Fact]
   public async Task ReloadActiveSolutionWhileOlderLoadInFlight_OlderLoadFinishesFirst_ReloadedSolutionActive()
   {
      var loadA = _manager.LoadSolutionAsync(SolutionA, CT);
      await LoadAsync(SolutionB);

      await _manager.UnloadSolutionAsync(SolutionB, CT);
      var reloadB = _manager.LoadSolutionAsync(SolutionB, CT);
      _loads.Complete(SolutionA);
      var resultA = await loadA;
      _loads.Complete(SolutionB, attempt: 1);
      var resultB = await reloadB;

      Assert.True(resultA.IsActive);
      Assert.True(resultB.IsActive);
      Assert.Equal(SolutionB, _manager.ActiveSolutionPath);
   }

   [Fact]
   public async Task ReloadActiveSolutionWhileOlderLoadInFlight_ReloadFinishesFirst_ReloadedSolutionStaysActive()
   {
      var loadA = _manager.LoadSolutionAsync(SolutionA, CT);
      await LoadAsync(SolutionB);

      await _manager.UnloadSolutionAsync(SolutionB, CT);
      var reloadB = _manager.LoadSolutionAsync(SolutionB, CT);
      _loads.Complete(SolutionB, attempt: 1);
      var resultB = await reloadB;
      _loads.Complete(SolutionA);
      var resultA = await loadA;

      Assert.True(resultB.IsActive);
      Assert.False(resultA.IsActive);
      Assert.Equal(SolutionB, _manager.ActiveSolutionPath);
   }

   [Fact]
   public async Task LoadOfAlreadyLoadedSolution_BecomesActive()
   {
      await LoadAsync(SolutionA);
      await LoadAsync(SolutionB);

      var result = await _manager.LoadSolutionAsync(SolutionA, CT);

      Assert.True(result.WasAlreadyLoaded);
      Assert.True(result.IsActive);
      Assert.Equal(SolutionA, _manager.ActiveSolutionPath);
   }

   [Fact]
   public async Task AutoLoadOfSolutionLoadedByExplicitRequest_ExplicitSolutionStaysActive()
   {
      await LoadAsync(SolutionA);
      await LoadAsync(SolutionB);

      var result = await _manager.AutoLoadSolutionAsync(SolutionA, CT);

      Assert.True(result.WasAlreadyLoaded);
      Assert.False(result.IsActive);
      Assert.Equal(SolutionB, _manager.ActiveSolutionPath);
   }

   /// <summary>
   /// Stands in for the worker processes: each load of a path waits until the test completes or fails it.
   /// </summary>
   private sealed class ControlledWorkerLoads
   {
      private readonly Lock _lock = new();
      private readonly Dictionary<(string Path, int Attempt), TaskCompletionSource<WorkerInstance>> _completions = [];
      private readonly Dictionary<string, int> _callCounts = [];

      public Task<WorkerInstance> LoadAsync(string solutionPath, CancellationToken cancellationToken)
      {
         int attempt;
         lock (_lock)
         {
            attempt = _callCounts.GetValueOrDefault(solutionPath);
            _callCounts[solutionPath] = attempt + 1;
         }
         return Completion(solutionPath, attempt).Task.WaitAsync(cancellationToken);
      }

      public int CallCount(string solutionPath)
      {
         lock (_lock)
         {
            return _callCounts.GetValueOrDefault(solutionPath);
         }
      }

      /// <summary>
      /// Finishes a load of the path, where the attempt counts the loads of that path from 0.
      /// </summary>
      public void Complete(string solutionPath, int attempt = 0) =>
         Completion(solutionPath, attempt).SetResult(new WorkerInstance
         {
            SolutionPath = solutionPath,
            Client = A.Fake<McpClient>(),
            LoadedAt = DateTime.UtcNow,
         });

      public void Fail(string solutionPath, Exception exception) =>
         Completion(solutionPath, attempt: 0).SetException(exception);

      private TaskCompletionSource<WorkerInstance> Completion(string solutionPath, int attempt)
      {
         lock (_lock)
         {
            if (!_completions.TryGetValue((solutionPath, attempt), out var completion))
            {
               completion = new TaskCompletionSource<WorkerInstance>(TaskCreationOptions.RunContinuationsAsynchronously);
               _completions[(solutionPath, attempt)] = completion;
            }
            return completion;
         }
      }
   }
}
