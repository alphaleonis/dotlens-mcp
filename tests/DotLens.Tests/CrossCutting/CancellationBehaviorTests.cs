using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools;
using DotLens.Tools.Analysis;
using DotLens.Tools.Intelligence;
using DotLens.Tools.Navigation;
using DotLens.Tools.Refactoring;
using DotLens.Tools.Search;
using FakeItEasy;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DotLens.Tests.CrossCutting;

/// <summary>
/// Tests that cancellation tokens are properly respected throughout async operation chains.
/// </summary>
public class CancellationBehaviorTests : MockableRoslynServiceTestBase
{
   // Navigation tools
   private GetSymbolInfoTool _getSymbolInfo = null!;
   private FindReferencesTool _findReferences = null!;

   // Search tools
   private GetTypeMembersTool _getTypeMembers = null!;

   // Analysis tools
   private GetComplexityMetricsTool _getComplexityMetrics = null!;

   // Refactoring tools
   private AddUsingTool _addUsing = null!;

   // Intelligence tools
   private GetTypeOverviewTool _getTypeOverview = null!;
   private GetTypeMembersBatchTool _getTypeMembersBatch = null!;

   public CancellationBehaviorTests()
   {
      SetupSolutionLoaded();

      // Initialize services
      var symbolLookup = new SymbolLookupService(FakeWorkspaceManager);

      // Initialize tools
      _getSymbolInfo = new GetSymbolInfoTool(FakeWorkspaceManager, symbolLookup);
      _findReferences = new FindReferencesTool(FakeWorkspaceManager, symbolLookup);
      _getTypeMembers = new GetTypeMembersTool(FakeWorkspaceManager, symbolLookup);
      _getComplexityMetrics = new GetComplexityMetricsTool(FakeWorkspaceManager, symbolLookup);
      _addUsing = new AddUsingTool(FakeWorkspaceManager);
      _getTypeOverview = new GetTypeOverviewTool(FakeWorkspaceManager, symbolLookup);
      _getTypeMembersBatch = new GetTypeMembersBatchTool(FakeWorkspaceManager);
   }

   #region Cancellation During Type Lookup

   // Note: GetTypeMembers, GetTypeOverview, and GetDerivedTypes cancellation are tested via integration tests
   // since they now use SymbolLookupService which requires a real Solution object

   #endregion

   #region Cancellation During Document Access

   [Fact]
   public async Task GetSymbolInfo_Cancelled_ThrowsOperationCanceledException()
   {
      // Arrange
      using var cts = new CancellationTokenSource();
      cts.Cancel();

      A.CallTo(() => FakeWorkspaceManager.GetDocumentAsync(A<string>._, A<CancellationToken>._))
         .ThrowsAsync(new OperationCanceledException());

      // Act & Assert
      await Assert.ThrowsAsync<OperationCanceledException>(() =>
         _getSymbolInfo.GetSymbolInfoAsync(
            new LookupByPosition { FilePath = @"C:\Test\File.cs", Line = 1, Column = 1 },
            cancellationToken: cts.Token));
   }

   [Fact]
   public async Task FindReferences_Cancelled_ThrowsOperationCanceledException()
   {
      // Arrange
      using var cts = new CancellationTokenSource();
      cts.Cancel();

      A.CallTo(() => FakeWorkspaceManager.GetDocumentAsync(A<string>._, A<CancellationToken>._))
         .ThrowsAsync(new OperationCanceledException());

      // Act & Assert
      await Assert.ThrowsAsync<OperationCanceledException>(() =>
         _findReferences.FindReferencesAsync(
            new LookupByPosition { FilePath = @"C:\Test\File.cs", Line = 1, Column = 1 },
            cancellationToken: cts.Token));
   }

   [Fact]
   public async Task GetComplexityMetrics_Cancelled_ThrowsOperationCanceledException()
   {
      // Arrange
      using var cts = new CancellationTokenSource();
      cts.Cancel();

      A.CallTo(() => FakeWorkspaceManager.GetDocumentAsync(A<string>._, A<CancellationToken>._))
         .ThrowsAsync(new OperationCanceledException());

      // Act & Assert
      await Assert.ThrowsAsync<OperationCanceledException>(() =>
         _getComplexityMetrics.GetComplexityMetricsAsync(
            new ComplexityTargetFile { FilePath = @"C:\Test\File.cs" },
            cancellationToken: cts.Token));
   }

   [Fact]
   public async Task AddUsing_Cancelled_ThrowsOperationCanceledException()
   {
      // Arrange
      using var cts = new CancellationTokenSource();
      cts.Cancel();

      // Act & Assert — cancellation occurs at WriteLock.AcquireAsync before any workspace call.
      // ThrowsAnyAsync because SemaphoreSlim throws TaskCanceledException (subclass of OperationCanceledException).
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
         _addUsing.AddUsingAsync(@"C:\Test\File.cs", "System.Linq", cancellationToken: cts.Token));
   }

   #endregion

   #region Cancellation Token Propagation

   [Fact]
   public async Task GetTypeMembersBatch_PropagatesCancellationToken()
   {
      // Arrange
      using var cts = new CancellationTokenSource();
      var capturedTokens = new List<CancellationToken>();

      A.CallTo(() => FakeWorkspaceManager.FindTypeWithCompilationAsync(A<string>._, A<bool>._, A<string?>._, A<CancellationToken>._))
         .Invokes((string _, bool _, string? _, CancellationToken ct) => capturedTokens.Add(ct))
         .Returns(Task.FromResult<TypeLookupResult?>(null));

      // Act
      await _getTypeMembersBatch.GetTypeMembersBatchAsync(["Type1", "Type2"], cancellationToken: cts.Token);

      // Assert - verify cancellation token was passed through
      Assert.Equal(2, capturedTokens.Count);
      foreach (var token in capturedTokens)
      {
         Assert.Equal(cts.Token, token);
      }
   }

   #endregion

   // Note: Early cancellation detection for GetTypeMembers is covered by integration tests
   // since it now uses SymbolLookupService which requires a real Solution object
}
