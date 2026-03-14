using DotLens.Tools.Refactoring;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace DotLens.Tests.Refactoring;

public class RefactoringHelpersTests
{
   #region ToCamelCase Tests

   [Theory]
   [InlineData("Name", "name")]
   [InlineData("MyProperty", "myProperty")]
   [InlineData("X", "x")]
   [InlineData("ABC", "aBC")]
   [InlineData("", "")]
   public void ToCamelCase_ConvertsFirstCharToLower(string input, string expected)
   {
      var result = RefactoringHelpers.ToCamelCase(input);
      Assert.Equal(expected, result);
   }

   [Fact]
   public void ToCamelCase_WithNull_ReturnsNull()
   {
      var result = RefactoringHelpers.ToCamelCase(null!);
      Assert.Null(result);
   }

   #endregion

   #region GetBuiltInRefactoringProviders Tests

   [Fact]
   public void GetBuiltInRefactoringProviders_ReturnsListWithoutThrowingException()
   {
      // The method may return an empty list depending on Roslyn version/assembly
      // The important thing is it doesn't throw
      var providers = RefactoringHelpers.GetBuiltInRefactoringProviders();

      Assert.NotNull(providers);
      // Verify it's a valid list (may be empty if no providers with parameterless constructors)
      Assert.IsType<List<Microsoft.CodeAnalysis.CodeRefactorings.CodeRefactoringProvider>>(providers);
   }

   [Fact]
   public void GetBuiltInRefactoringProviders_AllReturnedProvidersAreValid()
   {
      var providers = RefactoringHelpers.GetBuiltInRefactoringProviders();

      // If any providers are returned, they should all be non-null
      foreach (var provider in providers)
      {
         Assert.NotNull(provider);
      }
   }

   [Fact]
   public void GetBuiltInRefactoringProviders_FindsKnownRefactoringProviders()
   {
      var providers = RefactoringHelpers.GetBuiltInRefactoringProviders();

      // Verify we find a non-trivial number of providers from the Features assembly
      // Common refactoring providers include: Inline, Extract Method, Encapsulate Field, etc.
      Assert.True(providers.Count > 10,
         $"Expected more than 10 refactoring providers, but found {providers.Count}. " +
         "This suggests the Features assemblies are not being loaded correctly.");

      // Verify we find some well-known provider types by checking type names
      var providerNames = providers.Select(p => p.GetType().Name).ToList();

      // At least one of these common refactorings should be present
      var expectedProviders = new[]
      {
         "InlineTemporary", "ExtractMethod", "EncapsulateField",
         "IntroduceVariable", "MoveType"
      };

      var foundAny = expectedProviders.Any(expected =>
         providerNames.Any(name => name.Contains(expected, StringComparison.OrdinalIgnoreCase)));

      Assert.True(foundAny,
         $"Expected to find at least one of [{string.Join(", ", expectedProviders)}] " +
         $"in provider names, but found: [{string.Join(", ", providerNames.Take(20))}...]");
   }

   #endregion

   #region GetChangedFilesAsync Tests

   [Fact]
   public async Task GetChangedFilesAsync_WithNoChanges_ReturnsEmptyList()
   {
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(TestHelpers.SampleCode.SimpleClass);
      try
      {
         var oldSolution = workspace.CurrentSolution;
         var newSolution = workspace.CurrentSolution; // Same solution

         var changedFiles = await RefactoringHelpers.GetChangedFilesAsync(
            oldSolution, newSolution, includeText: true, CancellationToken.None);

         Assert.Empty(changedFiles);
      }
      finally
      {
         workspace.Dispose();
      }
   }

   [Fact]
   public async Task GetChangedFilesAsync_WithSingleFileChange_ReturnsChangedFile()
   {
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(TestHelpers.SampleCode.SimpleClass);
      try
      {
         var oldSolution = workspace.CurrentSolution;

         // Make a change to the document
         var oldText = await document.GetTextAsync();
         var newText = oldText.Replace(new TextSpan(0, 0), "// Added comment\n");
         var newSolution = oldSolution.WithDocumentText(document.Id, newText);

         var changedFiles = await RefactoringHelpers.GetChangedFilesAsync(
            oldSolution, newSolution, includeText: true, CancellationToken.None);

         Assert.Single(changedFiles);
         Assert.Equal(document.FilePath, changedFiles[0].FilePath);
         Assert.True(changedFiles[0].ChangeCount > 0);
         Assert.NotNull(changedFiles[0].NewText);
         Assert.Contains("// Added comment", changedFiles[0].NewText);
      }
      finally
      {
         workspace.Dispose();
      }
   }

   [Fact]
   public async Task GetChangedFilesAsync_WithIncludeTextFalse_OmitsNewText()
   {
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(TestHelpers.SampleCode.SimpleClass);
      try
      {
         var oldSolution = workspace.CurrentSolution;

         // Make a change
         var oldText = await document.GetTextAsync();
         var newText = oldText.Replace(new TextSpan(0, 0), "// Comment\n");
         var newSolution = oldSolution.WithDocumentText(document.Id, newText);

         var changedFiles = await RefactoringHelpers.GetChangedFilesAsync(
            oldSolution, newSolution, includeText: false, CancellationToken.None);

         Assert.Single(changedFiles);
         Assert.Null(changedFiles[0].NewText);
      }
      finally
      {
         workspace.Dispose();
      }
   }

   [Fact]
   public async Task GetChangedFilesAsync_WithMultipleFileChanges_ReturnsAllChangedFiles()
   {
      var (workspace, documents) = TestHelpers.CreateWorkspaceWithMultipleDocuments(
         (TestHelpers.SampleCode.SimpleClass, "File1.cs"),
         (TestHelpers.SampleCode.InterfaceImplementation, "File2.cs"));
      try
      {
         var oldSolution = workspace.CurrentSolution;
         var newSolution = oldSolution;

         // Change both documents
         foreach (var doc in documents)
         {
            var oldText = await doc.GetTextAsync();
            var newText = oldText.Replace(new TextSpan(0, 0), $"// Modified {doc.Name}\n");
            newSolution = newSolution.WithDocumentText(doc.Id, newText);
         }

         var changedFiles = await RefactoringHelpers.GetChangedFilesAsync(
            oldSolution, newSolution, includeText: true, CancellationToken.None);

         Assert.Equal(2, changedFiles.Count);
      }
      finally
      {
         workspace.Dispose();
      }
   }

   [Fact]
   public async Task GetChangedFilesAsync_SupportsCancellation()
   {
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(TestHelpers.SampleCode.SimpleClass);
      try
      {
         var oldSolution = workspace.CurrentSolution;
         var oldText = await document.GetTextAsync();
         var newText = oldText.Replace(new TextSpan(0, 0), "// Comment\n");
         var newSolution = oldSolution.WithDocumentText(document.Id, newText);

         using var cts = new CancellationTokenSource();
         cts.Cancel();

         await Assert.ThrowsAsync<OperationCanceledException>(() =>
            RefactoringHelpers.GetChangedFilesAsync(oldSolution, newSolution, true, cts.Token));
      }
      finally
      {
         workspace.Dispose();
      }
   }

   #endregion

   #region ChangedFile Tests

   [Fact]
   public void ChangedFile_Properties_CanBeSetAndRetrieved()
   {
      var changedFile = new ChangedFile
      {
         FilePath = "/path/to/file.cs",
         ChangeCount = 5,
         NewText = "new content"
      };

      Assert.Equal("/path/to/file.cs", changedFile.FilePath);
      Assert.Equal(5, changedFile.ChangeCount);
      Assert.Equal("new content", changedFile.NewText);
   }

   #endregion
}
