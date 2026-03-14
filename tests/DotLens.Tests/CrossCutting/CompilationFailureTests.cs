using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Intelligence;
using DotLens.Tools.Search;
using FakeItEasy;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DotLens.Tests.CrossCutting;

/// <summary>
/// Tests tool behavior when compilation-related operations fail.
/// Since compilation is handled internally by Roslyn, we test the observable behavior
/// when workspace methods can't find types/symbols (which happens when compilation fails).
/// </summary>
public class CompilationFailureTests : MockableRoslynServiceTestBase
{
   private GetTypeOverviewTool _getTypeOverview = null!;
   private GetMethodSourceTool _getMethodSource = null!;

   public CompilationFailureTests()
   {
      SetupSolutionLoaded();

      var symbolLookup = new SymbolLookupService(FakeWorkspaceManager);

      _getTypeOverview = new GetTypeOverviewTool(FakeWorkspaceManager, symbolLookup);
      _getMethodSource = new GetMethodSourceTool(FakeWorkspaceManager, symbolLookup);
   }

   /// <summary>
   /// Asserts that a ToolException is thrown with the expected code.
   /// </summary>
   private static async Task AssertThrowsToolExceptionAsync(Func<Task> action, string expectedCode)
   {
      var ex = await Assert.ThrowsAsync<ToolException>(action);
      Assert.Equal(expectedCode, ex.Code);
   }

   #region FindTypeWithCompilationAsync Returns Null (simulates compilation failure)

   // Note: GetTypeMembers, GetTypeOverview, GetMethodSource, GetMethodSignature, and GetDerivedTypes
   // "type not found" behavior is tested via integration tests since they use SymbolLookupService
   // which requires a real Solution object.

   #endregion
}
