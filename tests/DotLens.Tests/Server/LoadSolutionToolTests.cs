using DotLens.Infrastructure;
using DotLens.Tools.Server;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.v3;

namespace DotLens.Tests.Server;

public class LoadSolutionToolTests
{
   protected CancellationToken CT => TestContext.Current.CancellationToken;

   private static IOptions<DotLensOptions> DefaultOptions() =>
      Options.Create(new DotLensOptions());

   [Fact]
   public async Task LoadSolution_FileNotExists_ThrowsToolException()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      var tool = new LoadSolutionTool(workerManager, DefaultOptions(), NullLogger<LoadSolutionTool>.Instance);

      var ex = await Assert.ThrowsAsync<ToolException>(
         () => tool.LoadSolutionAsync(@"C:\nonexistent\solution.sln", cancellationToken: CT));

      Assert.Equal("FILE_NOT_FOUND", ex.Code);
      A.CallTo(() => workerManager.LoadSolutionAsync(A<string>._, A<CancellationToken>._))
         .MustNotHaveHappened();
   }

   [Fact]
   public async Task LoadSolution_AlreadyLoaded_SwitchesActive()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      var solutionPath = Path.GetTempFileName() + ".sln";
      File.WriteAllText(solutionPath, ""); // Create temp file

      try
      {
         var instance = new WorkerInstance
         {
            SolutionPath = solutionPath,
            Client = null!,
            LoadedAt = DateTime.UtcNow.AddMinutes(-5), // Loaded 5 mins ago
            ProjectCount = 3,
            DocumentCount = 10
         };
         A.CallTo(() => workerManager.LoadSolutionAsync(solutionPath, A<CancellationToken>._))
            .Returns(new LoadSolutionResult(instance, WasAlreadyLoaded: true));
         A.CallTo(() => workerManager.WorkerCount).Returns(1);

         var tool = new LoadSolutionTool(workerManager, DefaultOptions(), NullLogger<LoadSolutionTool>.Instance);

         var result = await tool.LoadSolutionAsync(solutionPath, cancellationToken: CT);

         Assert.NotNull(result.Data);
         Assert.Equal("already_loaded", result.Data.Status);
         Assert.Equal(solutionPath, result.Data.SolutionPath);
         Assert.Equal(1, result.Data.TotalSolutions);
         Assert.Equal(3, result.Data.ProjectCount);
         Assert.Equal(10, result.Data.DocumentCount);
      }
      finally
      {
         File.Delete(solutionPath);
      }
   }

   [Fact]
   public async Task LoadSolution_NewSolution_LoadsSuccessfully()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      var solutionPath = Path.GetTempFileName() + ".sln";
      File.WriteAllText(solutionPath, ""); // Create temp file

      try
      {
         var instance = new WorkerInstance
         {
            SolutionPath = solutionPath,
            Client = null!,
            LoadedAt = DateTime.UtcNow, // Just loaded
            ProjectCount = 3,
            DocumentCount = 10
         };
         A.CallTo(() => workerManager.LoadSolutionAsync(solutionPath, A<CancellationToken>._))
            .Returns(new LoadSolutionResult(instance, WasAlreadyLoaded: false));
         A.CallTo(() => workerManager.WorkerCount).Returns(1);

         var tool = new LoadSolutionTool(workerManager, DefaultOptions(), NullLogger<LoadSolutionTool>.Instance);

         var result = await tool.LoadSolutionAsync(solutionPath, cancellationToken: CT);

         Assert.NotNull(result.Data);
         Assert.Equal("loaded", result.Data.Status);
         Assert.Equal(solutionPath, result.Data.SolutionPath);
         Assert.Equal(1, result.Data.TotalSolutions);
         Assert.Equal(3, result.Data.ProjectCount);
         Assert.Equal(10, result.Data.DocumentCount);
      }
      finally
      {
         File.Delete(solutionPath);
      }
   }

   [Theory]
   [InlineData(false, true)]
   [InlineData(true, true)]
   [InlineData(false, false)]
   public async Task LoadSolution_UnresolvedFrameworkReferences_ReturnsTheWorkerLoadWarnings(
      bool wasAlreadyLoaded, bool hasWarning)
   {
      const string warning = "Project App resolved no framework references.";
      var workerManager = A.Fake<IMultiWorkerManager>();
      var solutionPath = Path.GetTempFileName() + ".sln";
      File.WriteAllText(solutionPath, "");

      try
      {
         var instance = new WorkerInstance
         {
            SolutionPath = solutionPath,
            Client = null!,
            LoadedAt = DateTime.UtcNow,
            LoadWarnings = hasWarning ? [warning] : [],
         };
         A.CallTo(() => workerManager.LoadSolutionAsync(solutionPath, A<CancellationToken>._))
            .Returns(new LoadSolutionResult(instance, wasAlreadyLoaded));
         var tool = new LoadSolutionTool(workerManager, DefaultOptions(), NullLogger<LoadSolutionTool>.Instance);

         var result = await tool.LoadSolutionAsync(solutionPath, cancellationToken: CT);

         Assert.Equal(wasAlreadyLoaded ? "already_loaded" : "loaded", result.Data.Status);
         Assert.Equal(hasWarning ? new[] { warning } : null, result.Data.Warnings);
      }
      finally
      {
         File.Delete(solutionPath);
      }
   }

   [Fact]
   public async Task LoadSolution_LoadFails_ThrowsToolException()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      var solutionPath = Path.GetTempFileName() + ".sln";
      File.WriteAllText(solutionPath, ""); // Create temp file

      try
      {
         A.CallTo(() => workerManager.LoadSolutionAsync(solutionPath, A<CancellationToken>._))
            .ThrowsAsync(new Exception("Worker process failed to start"));

         var tool = new LoadSolutionTool(workerManager, DefaultOptions(), NullLogger<LoadSolutionTool>.Instance);

         var ex = await Assert.ThrowsAsync<ToolException>(
            () => tool.LoadSolutionAsync(solutionPath, cancellationToken: CT));

         Assert.Equal("ANALYSIS_FAILED", ex.Code);
      }
      finally
      {
         File.Delete(solutionPath);
      }
   }

   [Fact]
   public async Task LoadSolution_LoadingInProgress_ReturnsLoadingStatus()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      var solutionPath = Path.GetTempFileName() + ".sln";
      File.WriteAllText(solutionPath, ""); // Create temp file

      try
      {
         // Simulate a load that's in progress and takes longer than the wait timeout
         A.CallTo(() => workerManager.IsLoading(solutionPath)).Returns(true);
         A.CallTo(() => workerManager.LoadSolutionAsync(solutionPath, A<CancellationToken>._))
            .ReturnsLazily(call => WaitForCancellation(call.GetArgument<CancellationToken>(1)));
         A.CallTo(() => workerManager.WorkerCount).Returns(0);

         var options = Options.Create(new DotLensOptions { LoadWaitTimeoutSeconds = 1 });
         var tool = new LoadSolutionTool(workerManager, options, NullLogger<LoadSolutionTool>.Instance);

         var result = await tool.LoadSolutionAsync(solutionPath, cancellationToken: CT);

         Assert.NotNull(result.Data);
         Assert.Equal("loading", result.Data.Status);
         Assert.Equal(solutionPath, result.Data.SolutionPath);
         Assert.Equal(0, result.Data.TotalSolutions);
         Assert.Equal("Solution is loading in the background. Waited 1s. Try again in a few seconds.", result.Data.Message);
         Assert.Equal("Call load_solution again or use health_check to monitor progress.", result.Data.Hint);
      }
      finally
      {
         File.Delete(solutionPath);
      }
   }

   /// <summary>
   /// Returns a task that waits until canceled, simulating a long-running load.
   /// </summary>
   private static async Task<LoadSolutionResult> WaitForCancellation(CancellationToken cancellationToken)
   {
      await Task.Delay(Timeout.Infinite, cancellationToken);
      throw new InvalidOperationException("unreachable");
   }

   [Fact]
   public async Task LoadSolution_LoadingCompletes_ReturnsLoaded()
   {
      var workerManager = A.Fake<IMultiWorkerManager>();
      var solutionPath = Path.GetTempFileName() + ".sln";
      File.WriteAllText(solutionPath, ""); // Create temp file

      try
      {
         // First call: IsLoading returns true (join existing load)
         // But LoadSolutionAsync completes fast enough within the wait timeout
         var instance = new WorkerInstance
         {
            SolutionPath = solutionPath,
            Client = null!,
            LoadedAt = DateTime.UtcNow,
            ProjectCount = 5,
            DocumentCount = 20
         };
         A.CallTo(() => workerManager.IsLoading(solutionPath)).Returns(true);
         A.CallTo(() => workerManager.LoadSolutionAsync(solutionPath, A<CancellationToken>._))
            .Returns(new LoadSolutionResult(instance, WasAlreadyLoaded: false));
         A.CallTo(() => workerManager.WorkerCount).Returns(1);

         var options = Options.Create(new DotLensOptions { LoadWaitTimeoutSeconds = 5 });
         var tool = new LoadSolutionTool(workerManager, options, NullLogger<LoadSolutionTool>.Instance);

         var result = await tool.LoadSolutionAsync(solutionPath, cancellationToken: CT);

         Assert.NotNull(result.Data);
         Assert.Equal("loaded", result.Data.Status);
         Assert.Equal(solutionPath, result.Data.SolutionPath);
         Assert.Equal(1, result.Data.TotalSolutions);
         Assert.Equal(5, result.Data.ProjectCount);
         Assert.Equal(20, result.Data.DocumentCount);
      }
      finally
      {
         File.Delete(solutionPath);
      }
   }
}
