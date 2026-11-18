using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Intelligence;
using FakeItEasy;
using Xunit;

namespace DotLens.Tests.CrossCutting;

/// <summary>
/// Covers batch operations with partial failures, where some items succeed and some fail.
/// </summary>
[Collection("TestFixtures")]
public class BatchOperationTests : TestFixtureTestBase
{
   public BatchOperationTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetTypeMembersBatchTool _getTypeMembersBatch = null!;

   public override ValueTask InitializeAsync()
   {
      _getTypeMembersBatch = new GetTypeMembersBatchTool(Workspace, new SymbolLookupService(Workspace));
      return ValueTask.CompletedTask;
   }

   [Fact]
   public async Task GetTypeMembersBatch_MixedValidAndInvalid_ReturnsPartialResults()
   {
      var result = await _getTypeMembersBatch.GetTypeMembersBatchAsync(["SampleClass", "InvalidType"], cancellationToken: CT);

      Assert.Equal(2, result.Data.Requested);
      Assert.Equal(1, result.Data.Succeeded);
      Assert.Equal(1, result.Data.Failed);
      Assert.Equal(["CoreTestProject.Types.SampleClass"], result.Data.Results.Select(r => r.TypeName));
      var error = Assert.Single(result.Data.Errors);
      Assert.Equal("InvalidType", error.TypeName);
      Assert.Equal("Type not found: InvalidType", error.Error);
   }

   [Fact]
   public async Task GetTypeMembersBatch_AllInvalid_ReturnsAllErrors()
   {
      var result = await _getTypeMembersBatch.GetTypeMembersBatchAsync(
         ["NonExistent1", "NonExistent2", "NonExistent3"], cancellationToken: CT);

      Assert.Equal(3, result.Data.Requested);
      Assert.Equal(0, result.Data.Succeeded);
      Assert.Equal(3, result.Data.Failed);
      Assert.Empty(result.Data.Results);
      Assert.Equal(["NonExistent1", "NonExistent2", "NonExistent3"], result.Data.Errors.Select(e => e.TypeName));
   }

   [Fact]
   public async Task GetTypeMembersBatch_AllValid_ReturnsAllResults()
   {
      var result = await _getTypeMembersBatch.GetTypeMembersBatchAsync(["SampleClass", "AsyncService"], cancellationToken: CT);

      Assert.Equal(2, result.Data.Requested);
      Assert.Equal(2, result.Data.Succeeded);
      Assert.Equal(0, result.Data.Failed);
      Assert.Equal(
         ["CoreTestProject.Types.SampleClass", "CoreTestProject.Patterns.AsyncService"],
         result.Data.Results.Select(r => r.TypeName));
      Assert.Empty(result.Data.Errors);
   }

   [Fact]
   public async Task GetTypeMembersBatch_ErrorDetailsAreInformative()
   {
      var result = await _getTypeMembersBatch.GetTypeMembersBatchAsync(["MyMissingType"], cancellationToken: CT);

      var error = Assert.Single(result.Data.Errors);
      Assert.Equal("MyMissingType", error.TypeName);
      Assert.Equal("Type not found: MyMissingType", error.Error);
   }

   /// <summary>
   /// The token is canceled once the second lookup has started, so only a lookup that observes the token throws.
   /// </summary>
   [Fact]
   public async Task GetTypeMembersBatch_CanceledDuringLookup_ThrowsOperationCanceledException()
   {
      using var cts = new CancellationTokenSource();
      var firstEntryFormatted = false;
      var workspace = A.Fake<IReadOnlyWorkspace>(options => options.Wrapping(Workspace));
      A.CallTo(() => workspace.SolutionPath).ReturnsLazily(() =>
      {
         firstEntryFormatted = true;
         return Workspace.SolutionPath;
      });
      A.CallTo(() => workspace.EnsureSolutionLoaded()).ReturnsLazily(() =>
      {
         if (firstEntryFormatted)
            cts.Cancel();
         return Workspace.EnsureSolutionLoaded();
      });
      var batch = new GetTypeMembersBatchTool(workspace, new SymbolLookupService(workspace));

      await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
         batch.GetTypeMembersBatchAsync(["SampleClass", "AsyncService"], cancellationToken: cts.Token));
      Assert.True(cts.IsCancellationRequested);
   }
}
