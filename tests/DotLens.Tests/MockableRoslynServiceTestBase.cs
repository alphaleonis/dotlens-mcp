using DotLens.Infrastructure;
using FakeItEasy;
using Microsoft.CodeAnalysis;
using Xunit;
using Xunit.v3;

namespace DotLens.Tests;

/// <summary>
/// Minimal base class for unit tests using mocked dependencies.
/// Unlike RoslynServiceTestBase, this does not load a real solution.
/// Test classes should create only the specific tools they need.
/// </summary>
public abstract class MockableRoslynServiceTestBase : IDisposable
{
   private readonly AsyncLock _writeLock = new();

   protected IWorkspace FakeWorkspaceManager { get; private set; } = null!;

   protected CancellationToken CT => TestContext.Current.CancellationToken;

   protected MockableRoslynServiceTestBase()
   {
      FakeWorkspaceManager = A.Fake<IWorkspace>();
      A.CallTo(() => FakeWorkspaceManager.WriteLock).Returns(_writeLock);
   }

   public void Dispose()
   {
      _writeLock.Dispose();
      GC.SuppressFinalize(this);
   }

   /// <summary>
   /// Configures the fake workspace to indicate no solution is loaded.
   /// All methods that require a solution will throw InvalidOperationException.
   /// </summary>
   protected void SetupNoSolutionLoaded()
   {
      var noSolutionException = new InvalidOperationException("No solution is currently loaded");

      A.CallTo(() => FakeWorkspaceManager.IsSolutionLoaded).Returns(false);
      A.CallTo(() => FakeWorkspaceManager.Solution).Returns(null);
      A.CallTo(() => FakeWorkspaceManager.SolutionPath).Returns(null);
      A.CallTo(() => FakeWorkspaceManager.SolutionDirectory).Returns(null);
      A.CallTo(() => FakeWorkspaceManager.ProjectCount).Returns(0);
      A.CallTo(() => FakeWorkspaceManager.DocumentCount).Returns(0);

      // Methods that check solution loaded state
      A.CallTo(() => FakeWorkspaceManager.EnsureSolutionLoaded())
         .Throws(noSolutionException);
      A.CallTo(() => FakeWorkspaceManager.GetDocumentAsync(A<string>._, A<CancellationToken>._))
         .Throws(noSolutionException);
      A.CallTo(() => FakeWorkspaceManager.TryGetDocumentAsync(A<string>._, A<CancellationToken>._))
         .Throws(noSolutionException);
      A.CallTo(() => FakeWorkspaceManager.FindTypeByNameAsync(A<string>._, A<CancellationToken>._))
         .Throws(noSolutionException);
      A.CallTo(() => FakeWorkspaceManager.FindTypeWithCompilationAsync(A<string>._, A<bool>._, A<string?>._, A<CancellationToken>._))
         .Throws(noSolutionException);
   }

   /// <summary>
   /// Configures the fake workspace to indicate a solution is loaded.
   /// </summary>
   protected void SetupSolutionLoaded(
      string solutionPath = @"C:\Test\Test.sln",
      int projectCount = 1,
      int documentCount = 5)
   {
      A.CallTo(() => FakeWorkspaceManager.IsSolutionLoaded).Returns(true);
      A.CallTo(() => FakeWorkspaceManager.SolutionPath).Returns(solutionPath);
      A.CallTo(() => FakeWorkspaceManager.SolutionDirectory).Returns(Path.GetDirectoryName(solutionPath));
      A.CallTo(() => FakeWorkspaceManager.ProjectCount).Returns(projectCount);
      A.CallTo(() => FakeWorkspaceManager.DocumentCount).Returns(documentCount);
      A.CallTo(() => FakeWorkspaceManager.UseAbsolutePaths).Returns(false);
      A.CallTo(() => FakeWorkspaceManager.MaxDiagnostics).Returns(100);
      A.CallTo(() => FakeWorkspaceManager.TimeoutSeconds).Returns(30);
   }

   /// <summary>
   /// Configures GetDocumentAsync to throw ToolException for a given path,
   /// and TryGetDocumentAsync to return null.
   /// </summary>
   protected void SetupDocumentNotFound(string filePath)
   {
      A.CallTo(() => FakeWorkspaceManager.GetDocumentAsync(filePath, A<CancellationToken>._))
         .Throws(new ToolException(ErrorCodes.FileNotInSolution, $"File not found in solution: {filePath}"));
      A.CallTo(() => FakeWorkspaceManager.TryGetDocumentAsync(filePath, A<CancellationToken>._))
         .Returns((Document?)null);
   }

   /// <summary>
   /// Configures FormatPath to return the expected formatted path.
   /// </summary>
   protected void SetupFormatPath(string inputPath, string formattedPath)
   {
      A.CallTo(() => FakeWorkspaceManager.FormatPath(inputPath)).Returns(formattedPath);
   }

   /// <summary>
   /// Configures FormatPath to return relative paths by stripping the solution directory.
   /// </summary>
   protected void SetupRelativePathFormatting(string solutionDirectory)
   {
      A.CallTo(() => FakeWorkspaceManager.FormatPath(A<string>.That.IsNotNull()!))
         .ReturnsLazily((string? path) =>
         {
            if (path == null)
               return string.Empty;
            if (path.StartsWith(solutionDirectory, StringComparison.OrdinalIgnoreCase))
            {
               var relative = path[solutionDirectory.Length..].TrimStart(Path.DirectorySeparatorChar);
               return relative;
            }
            return path;
         });
   }

   /// <summary>
   /// Configures ApplyChanges to succeed (return true).
   /// Use this when testing non-preview operations that need to apply changes to the workspace.
   /// </summary>
   protected void SetupApplyChangesSuccess()
   {
      A.CallTo(() => FakeWorkspaceManager.ApplyChanges(A<Solution>._)).Returns(true);
   }

   /// <summary>
   /// Configures ApplyChanges to fail (return false).
   /// Use this to test error handling for APPLY_CHANGES_FAILED scenarios.
   /// </summary>
   protected void SetupApplyChangesFailure()
   {
      A.CallTo(() => FakeWorkspaceManager.ApplyChanges(A<Solution>._)).Returns(false);
   }
}
