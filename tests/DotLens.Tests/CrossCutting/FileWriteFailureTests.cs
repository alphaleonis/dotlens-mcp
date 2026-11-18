using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Refactoring;
using FakeItEasy;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DotLens.Tests.CrossCutting;

/// <summary>
/// Tests for file write operation failures.
///
/// Note: Many write failure scenarios require a valid Document from Roslyn,
/// which needs real compilation. These tests focus on mockable scenarios:
/// 1. Scenarios where we can short-circuit before file operations
/// 2. Verifying ApplyChanges is not called when validation fails early
///
/// Full write failure testing requires integration tests with a real solution.
/// See the integration test suite for those scenarios.
/// </summary>
public class FileWriteFailureTests : MockableRoslynServiceTestBase
{
   private const string ValidFile = @"C:\Test\Project\Valid.cs";

   // Refactoring tools
   private RenameSymbolTool _renameSymbol = null!;
   private AddUsingTool _addUsing = null!;

   public FileWriteFailureTests()
   {
      SetupSolutionLoaded(@"C:\Test\Test.sln");

      // Initialize services
      var symbolLookup = new SymbolLookupService(FakeWorkspaceManager);

      // Initialize tools
      _renameSymbol = new RenameSymbolTool(FakeWorkspaceManager, symbolLookup);
      _addUsing = new AddUsingTool(FakeWorkspaceManager);
   }

   #region Pre-write Failure Scenarios

   [Fact]
   public async Task RenameSymbol_FileNotInSolution_ThrowsToolException()
   {
      // Arrange: The file is not in solution
      SetupDocumentNotFound(ValidFile);

      // Act & Assert - SymbolLookupService throws ToolException with FILE_NOT_IN_SOLUTION
      var ex = await Assert.ThrowsAsync<ToolException>(() =>
         _renameSymbol.RenameSymbolAsync(
            new LookupByPosition { FilePath = ValidFile, Line = 1, Column = 1 },
            "NewName",
            cancellationToken: CT));
      Assert.Equal(ErrorCodes.FileNotInSolution, ex.Code);
   }

   [Fact]
   public async Task AddUsing_FileNotInSolution_ThrowsToolException()
   {
      // Arrange - WorkspaceManager now throws ToolException directly
      A.CallTo(() => FakeWorkspaceManager.GetDocumentAsync(A<string>._, A<CancellationToken>._))
         .Throws(new ToolException(ErrorCodes.FileNotInSolution, "File not found in solution: NonExistent.cs"));

      // Act & Assert
      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _addUsing.AddUsingAsync("NonExistent.cs", "System.Linq", preview: false, cancellationToken: CT));

      Assert.Equal(ErrorCodes.FileNotInSolution, ex.Code);

      // Verify ApplyChanges was never called (short-circuited before file operations)
      A.CallTo(() => FakeWorkspaceManager.ApplyChanges(A<Solution>._, A<CancellationToken>._))
         .MustNotHaveHappened();
   }

   #endregion

   #region Validation Errors

   [Fact]
   public async Task AddUsing_EmptyNamespace_ThrowsToolException()
   {
      // Act & Assert
      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _addUsing.AddUsingAsync(ValidFile, "", cancellationToken: CT));

      Assert.Equal("INVALID_PARAMETER", ex.Code);

      // ApplyChanges never called (validation failed before reaching file operations)
      A.CallTo(() => FakeWorkspaceManager.ApplyChanges(A<Solution>._, A<CancellationToken>._))
         .MustNotHaveHappened();
   }

   #endregion
}
