using System.Text.Json;
using System.Text.Json.Nodes;
using DotLens.Infrastructure;
using DotLens.Tools.Search;
using Xunit;

namespace DotLens.Tests.Infrastructure;

/// <summary>
/// Tests for WorkspaceManager functionality including solution loading,
/// health checks, document syncing, and path handling.
/// </summary>
public class WorkspaceManagerTests : IsolatedProjectTestBase
{

   private FindSymbolsTool _findSymbols = null!;

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      _findSymbols = new FindSymbolsTool(WorkspaceManager);
   }

   #region Health Check

   [Fact]
   public async Task GetHealthCheck_ReturnsSolutionInfo()
   {
      // Act
      var result = await WorkspaceManager.GetHealthCheckAsync();

      // Assert - health check has different structure (status, solution.loaded, etc.)
      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal("Ready", data.GetStringOrNull("status"));
      Assert.True(data.GetObject("solution").GetBoolOrNull("loaded"));
      var solutionPath = data.GetObject("solution").GetStringOrNull("path");
      Assert.True(solutionPath!.EndsWith("TestFixtures.sln") || solutionPath.EndsWith("TestFixtures.slnx"),
          "Solution path should end with TestFixtures.sln or TestFixtures.slnx");
   }

   #endregion

   #region Load Solution

   [Fact]
   public async Task LoadSolution_WithValidPath_Succeeds()
   {
      var result = await WorkspaceManager.LoadSolutionAsync(TestFixturesSolutionPath, CT);

      var solution = WorkspaceManager.EnsureSolutionLoaded();
      Assert.Equal(TestFixturesSolutionPath, solution.FilePath);
      Assert.Equivalent(
         new[] { "AnalyzerTestProject", "InheritanceTestProject", "CoreTestProject", "ConflictingTypesProject" },
         solution.Projects.Select(p => p.Name));
      Assert.Equal(TestFixturesSolutionPath, result.Data.SolutionPath);
      Assert.Equal(4, result.Data.ProjectCount);
      Assert.Equal(solution.Projects.Sum(p => p.DocumentIds.Count), result.Data.DocumentCount);
      Assert.Empty(result.Data.Warnings);
   }

   [Fact]
   public async Task LoadSolution_WithInvalidPath_ThrowsToolException()
   {
      // Arrange
      var invalidPath = @"C:\NonExistent\Fake.sln";

      // Act & Assert
      await AssertThrowsToolExceptionAsync(
         () => WorkspaceManager.LoadSolutionAsync(invalidPath),
         "FILE_NOT_FOUND");
   }

   #endregion

   #region Sync Documents

   [Fact]
   public async Task SyncDocuments_WithNoFiles_SyncsAllDocuments()
   {
      // Act - sync all documents (no specific files)
      var result = await WorkspaceManager.SyncDocumentsAsync(null);

      // Assert
      AssertSuccess(result);
      var data = GetData(result);
      Assert.True(data.GetIntOrNull("totalSynced") >= 0);
      Assert.NotNull(data["updated"]);
      Assert.NotNull(data["added"]);
      Assert.NotNull(data["removed"]);
   }

   [Fact]
   public async Task SyncDocuments_WithSpecificFile_SyncsOnlyThatFile()
   {
      // Act - sync a specific file from TestFixtures
      var result = await WorkspaceManager.SyncDocumentsAsync(new List<string> { "CoreTestProject/Types/Classes.cs" });

      // Assert
      AssertSuccess(result);
      var data = GetData(result);
      // File exists, should be in updated list
      var updatedFiles = data.GetArrayOrNull("updatedFiles");
      Assert.NotNull(updatedFiles);

      // Should have synced exactly 1 file
      Assert.Equal(1, data.GetIntOrNull("totalSynced"));
   }

   [Fact]
   public async Task SyncDocuments_WithNonExistentFile_HandlesGracefully()
   {
      // Act - sync a file that doesn't exist and isn't in solution
      var result = await WorkspaceManager.SyncDocumentsAsync(new List<string> { "src/NonExistent.cs" });

      // Assert
      AssertSuccess(result);
      var data = GetData(result);
      // Nothing to sync for non-existent file not in solution
      Assert.Equal(0, data.GetIntOrNull("totalSynced"));
   }

   [Fact]
   public async Task SyncDocuments_WithPathTraversal_ReturnsSecurityError()
   {
      // Act - attempt path traversal attack
      var result = await WorkspaceManager.SyncDocumentsAsync(
          new List<string> { "../../../../../../etc/passwd" });

      // Assert - should succeed overall but report error for the malicious path
      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal(0, data.GetIntOrNull("totalSynced"));
      var errors = data.GetArrayOrNull("errors");
      Assert.NotNull(errors);
      Assert.Single(errors!);
      Assert.Contains("traversal", errors[0].GetStringOrNull("error"));
   }

   [Fact]
   public async Task SyncDocuments_WithAllDocuments_DoesNotThrowOnDuplicatePaths()
   {
      // A NuGet package can add a source file to several projects (xunit.v3.core's DefaultRunnerReporters.cs).
      // TestFixtures has no such path; SyncDocumentsLinkedCopiesTests covers files that several projects compile.

      // Act - sync all documents (the code path that builds the path dictionary)
      var result = await WorkspaceManager.SyncDocumentsAsync(null);

      // Assert - should complete without throwing
      AssertSuccess(result);
      var data = GetData(result);
      Assert.NotNull(data["totalSynced"]);
   }

   #endregion

   #region Path Handling

   [Fact]
   public async Task Paths_AreRelativeByDefault()
   {
      // Act - search for a symbol to get file paths
      var result = await _findSymbols.FindSymbolsAsync(name: "SampleClass", kinds: [SymbolKindFilter.Class], maxResults: 1, cancellationToken: CT);

      // Assert
      AssertSuccess(result);
      var data = GetData(result);
      var symbols = data.GetArrayOrNull("items");
      Assert.NotNull(symbols);
      Assert.True(symbols!.Count > 0);

      // Path is inside location object
      var location = symbols[0].GetObjectOrNull("location");
      Assert.NotNull(location);
      var filePath = location!.GetStringOrNull("filePath");
      Assert.NotNull(filePath);
      Assert.False(filePath!.StartsWith("C:"));
      Assert.False(filePath.StartsWith("/"));
      // Should use forward slashes for cross-platform consistency
      Assert.DoesNotContain("\\", filePath);
      // Should be a relative path like "CoreTestProject/Types/Classes.cs"
      Assert.StartsWith("CoreTestProject/", filePath);
   }

   [Fact]
   public async Task GetDocumentAsync_WithRelativePath_ResolvesAgainstSolutionDirectory()
   {
      // Arrange - use a relative path (relative to solution directory)
      var relativePath = "CoreTestProject/Types/Classes.cs";
      var expectedAbsolutePath = GetCoreTestProjectPath("Types/Classes.cs");

      // Act - should resolve relative to solution directory, not CWD
      var document = await WorkspaceManager.GetDocumentAsync(relativePath, CT);

      // Assert - document should be found and resolved to correct absolute path
      Assert.NotNull(document);
      Assert.Equal(expectedAbsolutePath, document.FilePath);
   }

   // Note: GetDocumentAsync and TryGetDocumentAsync have different contracts (throw vs return null),
   // so we test both to ensure relative path resolution works correctly in each variant.
   [Fact]
   public async Task TryGetDocumentAsync_WithRelativePath_ResolvesAgainstSolutionDirectory()
   {
      // Arrange - use a relative path (relative to solution directory)
      var relativePath = "CoreTestProject/Types/Classes.cs";
      var expectedAbsolutePath = GetCoreTestProjectPath("Types/Classes.cs");

      // Act - should resolve relative to solution directory, not CWD
      var document = await WorkspaceManager.TryGetDocumentAsync(relativePath, CT);

      // Assert - document should be found and resolved to correct absolute path
      Assert.NotNull(document);
      Assert.Equal(expectedAbsolutePath, document!.FilePath);
   }

   [Fact]
   public async Task GetDocumentAsync_WithAbsolutePath_WorksAsExpected()
   {
      // Arrange - use an absolute path
      var absolutePath = GetCoreTestProjectPath("Types/Classes.cs");

      // Act
      var document = await WorkspaceManager.GetDocumentAsync(absolutePath, CT);

      // Assert - document should be found at the exact path provided
      Assert.NotNull(document);
      Assert.Equal(absolutePath, document.FilePath);
   }

   [Fact]
   public async Task TryGetDocumentAsync_WithNonExistentRelativePath_ReturnsNull()
   {
      // Arrange - use a relative path that doesn't exist
      var relativePath = "NonExistent/DoesNotExist.cs";

      // Act
      var document = await WorkspaceManager.TryGetDocumentAsync(relativePath, CT);

      // Assert - should return null for non-existent path
      Assert.Null(document);
   }

   [Fact]
   public async Task GetDocumentAsync_WithNonExistentRelativePath_ThrowsToolException()
   {
      // Arrange - use a relative path that doesn't exist
      var relativePath = "NonExistent/DoesNotExist.cs";

      // Act & Assert - GetDocumentAsync throws ToolException (unlike TryGetDocumentAsync which returns null)
      var ex = await Assert.ThrowsAsync<ToolException>(
         () => WorkspaceManager.GetDocumentAsync(relativePath, CT));
      Assert.Equal(ErrorCodes.FileNotInSolution, ex.Code);
   }

   [Fact]
   public async Task TryGetDocumentAsync_WithPathTraversal_ThrowsSecurityException()
   {
      // Arrange - attempt path traversal attack
      var maliciousPath = "../../../../../../etc/passwd";

      // Act & Assert - should throw SecurityException, not silently return null
      await Assert.ThrowsAsync<System.Security.SecurityException>(
         () => WorkspaceManager.TryGetDocumentAsync(maliciousPath, CT));
   }

   [Fact]
   public async Task GetDocumentAsync_WithPathTraversal_ThrowsToolException()
   {
      // Arrange - attempt path traversal attack
      var maliciousPath = "../../../../../../etc/passwd";

      // Act & Assert - should throw ToolException with SecurityError code
      var ex = await Assert.ThrowsAsync<ToolException>(
         () => WorkspaceManager.GetDocumentAsync(maliciousPath, CT));
      Assert.Equal(ErrorCodes.SecurityError, ex.Code);
   }

   #endregion

   #region FindTypeWithCompilationAsync

   [Fact]
   public async Task FindTypeWithCompilationAsync_WithProjectName_FindsTypeInSpecificProject()
   {
      // Act - search for Animal type specifically in InheritanceTestProject
      var result = await WorkspaceManager.FindTypeWithCompilationAsync(
         "InheritanceTestProject.Animal",
         requireFullyQualifiedName: true,
         projectName: "InheritanceTestProject");

      // Assert
      Assert.NotNull(result);
      Assert.Equal("InheritanceTestProject.Animal", result.Type.ToDisplayString());
      Assert.Equal("InheritanceTestProject", result.Project.Name);
   }

   [Fact]
   public async Task FindTypeWithCompilationAsync_WithProjectName_ReturnsNullWhenTypeNotInProject()
   {
      // Act - search for Animal type in CoreTestProject (it's actually in InheritanceTestProject)
      var result = await WorkspaceManager.FindTypeWithCompilationAsync(
         "InheritanceTestProject.Animal",
         requireFullyQualifiedName: true,
         projectName: "CoreTestProject");

      // Assert - should return null because Animal is not in CoreTestProject
      Assert.Null(result);
   }

   [Fact]
   public async Task FindTypeWithCompilationAsync_WithProjectName_ReturnsNullForNonExistentProject()
   {
      // Act - search in a project that doesn't exist
      var result = await WorkspaceManager.FindTypeWithCompilationAsync(
         "System.String",
         requireFullyQualifiedName: true,
         projectName: "NonExistentProject");

      // Assert - should return null because project doesn't exist
      Assert.Null(result);
   }

   [Fact]
   public async Task FindTypeWithCompilationAsync_WithProjectName_IsCaseInsensitive()
   {
      // Act - search with different case project name
      var result = await WorkspaceManager.FindTypeWithCompilationAsync(
         "InheritanceTestProject.Animal",
         requireFullyQualifiedName: true,
         projectName: "inheritancetestproject");  // lowercase

      // Assert
      Assert.NotNull(result);
      Assert.Equal("InheritanceTestProject", result.Project.Name);
   }

   [Fact]
   public async Task FindTypeWithCompilationAsync_WithoutProjectName_SearchesAllProjects()
   {
      // Act - search without projectName (should search all projects)
      var result = await WorkspaceManager.FindTypeWithCompilationAsync(
         "InheritanceTestProject.Animal",
         requireFullyQualifiedName: true);

      // Assert
      Assert.NotNull(result);
      Assert.Equal("InheritanceTestProject.Animal", result.Type.ToDisplayString());
   }

   #endregion
}
