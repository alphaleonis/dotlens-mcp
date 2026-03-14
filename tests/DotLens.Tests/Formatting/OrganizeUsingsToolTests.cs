using DotLens.Infrastructure;
using DotLens.Tools.Formatting;
using Xunit;

namespace DotLens.Tests.Formatting;

public class OrganizeUsingsToolTests : IsolatedProjectTestBase
{

   private OrganizeUsingsTool _tool = null!;

   // UnusedUsingsTests.cs fixture has 6 usings, but CoreTestProject has ImplicitUsings=enable
   // This makes System, System.Collections.Generic, System.Linq, System.Threading.Tasks implicit
   // So 5 usings are unused (all except CoreTestProject.Types which is a project reference)
   private const int ExpectedOriginalCount = 6;
   private const int ExpectedRemovedCount = 5;
   private const int ExpectedFinalCount = 1;

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      _tool = new OrganizeUsingsTool(WorkspaceManager);
   }

   #region File Scope Tests

   [Fact]
   public async Task OrganizeUsings_FileScope_DetectsExactUnusedUsings()
   {
      var filePath = GetCoreTestProjectPath("UnusedUsingsTests.cs");
      var result = await _tool.OrganizeUsingsAsync(scope: new FileScope { Path = filePath }, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetTypedData<OrganizeUsingsResponse>(result);

      // Verify exact counts
      Assert.Equal(1, data.FilesProcessed);
      Assert.Equal(1, data.FilesChanged);
      Assert.Equal(ExpectedRemovedCount, data.TotalUsingsRemoved);
      Assert.False(data.Applied); // Preview mode by default

      // Verify single file info
      Assert.Single(data.Files);
      var fileInfo = data.Files[0];
      Assert.EndsWith("UnusedUsingsTests.cs", fileInfo.FilePath);
      Assert.Equal("CoreTestProject", fileInfo.ProjectName);
      Assert.Equal(ExpectedOriginalCount, fileInfo.OriginalCount);
      Assert.Equal(ExpectedRemovedCount, fileInfo.RemovedCount);
      Assert.Equal(ExpectedFinalCount, fileInfo.FinalCount);
   }

   [Fact]
   public async Task OrganizeUsings_FileScope_IncludesNewTextInPreview()
   {
      var filePath = GetCoreTestProjectPath("UnusedUsingsTests.cs");
      var result = await _tool.OrganizeUsingsAsync(scope: new FileScope { Path = filePath }, preview: true, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetTypedData<OrganizeUsingsResponse>(result);

      // NewText should be included in file scope preview
      Assert.Single(data.Files);
      var newText = data.Files[0].NewText;
      Assert.NotNull(newText);

      // With ImplicitUsings=enable, only CoreTestProject.Types should remain
      Assert.Contains("using CoreTestProject.Types;", newText);
      // All others are unused (implicit or not used)
      Assert.DoesNotContain("using System;", newText);
      Assert.DoesNotContain("using System.Collections.Generic;", newText);
      Assert.DoesNotContain("System.Text.RegularExpressions", newText);
      Assert.DoesNotContain("System.Linq", newText);
      Assert.DoesNotContain("System.Threading.Tasks", newText);
   }

   [Fact]
   public async Task OrganizeUsings_FileScope_NoUsings_ReturnsNoChanges()
   {
      // NullCheckTests.cs has no explicit usings (uses implicit usings only)
      var filePath = GetCoreTestProjectPath("NullCheckTests.cs");
      var result = await _tool.OrganizeUsingsAsync(scope: new FileScope { Path = filePath }, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetTypedData<OrganizeUsingsResponse>(result);

      // File was processed but has no usings to organize
      Assert.Equal(1, data.FilesProcessed);
      Assert.Equal(0, data.FilesChanged);
      Assert.Equal(0, data.TotalUsingsRemoved);
      Assert.Empty(data.Files); // No files in result when no changes
   }

   [Fact]
   public async Task OrganizeUsings_FileScope_InvalidFile_ThrowsError()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.OrganizeUsingsAsync(scope: new FileScope { Path = "nonexistent.cs" }, cancellationToken: CT),
         "FILE_NOT_IN_SOLUTION");
   }

   #endregion

   #region Project Scope Tests

   [Fact]
   public async Task OrganizeUsings_ProjectScope_ProcessesMultipleFiles()
   {
      var result = await _tool.OrganizeUsingsAsync(
         scope: new ProjectScope { Name = "CoreTestProject" },
         preview: true,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetTypedData<OrganizeUsingsResponse>(result);

      // CoreTestProject has many files
      Assert.True(data.FilesProcessed > 5, $"Expected >5 files processed, got {data.FilesProcessed}");

      // At least UnusedUsingsTests.cs should have changes
      Assert.True(data.FilesChanged >= 1, "Expected at least 1 file with changes");
      Assert.True(data.TotalUsingsRemoved >= ExpectedRemovedCount,
         $"Expected at least {ExpectedRemovedCount} usings removed, got {data.TotalUsingsRemoved}");
   }

   [Fact]
   public async Task OrganizeUsings_ProjectScope_WithFilePattern_FiltersCorrectly()
   {
      var result = await _tool.OrganizeUsingsAsync(
         scope: new ProjectScope { Name = "CoreTestProject" },
         filePattern: "*UsingsTests.cs",
         preview: true,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetTypedData<OrganizeUsingsResponse>(result);

      // Should match exactly UnusedUsingsTests.cs
      Assert.Equal(1, data.FilesProcessed);
      Assert.Equal(1, data.FilesChanged);
      Assert.Single(data.Files);
      Assert.EndsWith("UnusedUsingsTests.cs", data.Files[0].FilePath);
   }

   [Fact]
   public async Task OrganizeUsings_ProjectScope_InvalidProject_ThrowsError()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.OrganizeUsingsAsync(scope: new ProjectScope { Name = "NonExistent" }, cancellationToken: CT),
         "PROJECT_NOT_FOUND");
   }

   [Fact]
   public async Task OrganizeUsings_ProjectScope_NoNewTextInBatch()
   {
      var result = await _tool.OrganizeUsingsAsync(
         scope: new ProjectScope { Name = "CoreTestProject" },
         preview: true,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetTypedData<OrganizeUsingsResponse>(result);

      // In batch mode, NewText should be null to save tokens
      Assert.NotEmpty(data.Files);
      foreach (var file in data.Files)
      {
         Assert.Null(file.NewText);
      }
   }

   #endregion

   #region Solution Scope Tests

   [Fact]
   public async Task OrganizeUsings_SolutionScope_ProcessesAllProjects()
   {
      var result = await _tool.OrganizeUsingsAsync(
         scope: new SolutionScope(),
         preview: true,
         cancellationToken: CT);

      AssertSuccess(result);
      var data = GetTypedData<OrganizeUsingsResponse>(result);

      // Solution has multiple projects with many files
      Assert.True(data.FilesProcessed > 10, $"Expected >10 files in solution, got {data.FilesProcessed}");
   }

   [Fact]
   public async Task OrganizeUsings_DefaultScope_IsSolution()
   {
      // When no scope is provided, should default to solution
      var result = await _tool.OrganizeUsingsAsync(preview: true, cancellationToken: CT);

      AssertSuccess(result);
      var data = GetTypedData<OrganizeUsingsResponse>(result);

      // Should process same as explicit SolutionScope
      Assert.True(data.FilesProcessed > 10, "Default scope should process entire solution");
   }

   #endregion

   #region Preview vs Apply Tests

   [Fact]
   public async Task OrganizeUsings_Preview_MatchesApplyResults_MultiFile()
   {
      // Test that preview mode produces identical results to apply mode for multi-file scenarios.
      // This verifies that cumulative solution tracking is consistent between modes.

      // Get list of files in CoreTestProject that have usings we can test
      var projectPath = Path.Combine(TestFixturesDirectory, "CoreTestProject");
      var csFiles = Directory.GetFiles(projectPath, "*.cs", SearchOption.AllDirectories)
         .Where(f => !f.Contains("obj") && !f.Contains("bin"))
         .ToList();

      // Save original contents
      var originalContents = new Dictionary<string, string>();
      foreach (var file in csFiles)
      {
         originalContents[file] = await File.ReadAllTextAsync(file);
      }

      try
      {
         // First, get preview results for the entire project
         var previewResult = await _tool.OrganizeUsingsAsync(
            scope: new ProjectScope { Name = "CoreTestProject" },
            preview: true,
            cancellationToken: CT);

         AssertSuccess(previewResult);
         var previewData = GetTypedData<OrganizeUsingsResponse>(previewResult);

         // Then, apply and get results
         var applyResult = await _tool.OrganizeUsingsAsync(
            scope: new ProjectScope { Name = "CoreTestProject" },
            preview: false,
            cancellationToken: CT);

         AssertSuccess(applyResult);
         var applyData = GetTypedData<OrganizeUsingsResponse>(applyResult);

         // Preview and apply should report identical change counts
         Assert.Equal(previewData.FilesProcessed, applyData.FilesProcessed);
         Assert.Equal(previewData.FilesChanged, applyData.FilesChanged);
         Assert.Equal(previewData.TotalUsingsRemoved, applyData.TotalUsingsRemoved);

         // The file details should match (same files, same counts)
         Assert.Equal(previewData.Files.Count, applyData.Files.Count);

         // Sort both by filepath for comparison
         var sortedPreview = previewData.Files.OrderBy(f => f.FilePath).ToList();
         var sortedApply = applyData.Files.OrderBy(f => f.FilePath).ToList();

         for (int i = 0; i < sortedPreview.Count; i++)
         {
            Assert.Equal(sortedPreview[i].FilePath, sortedApply[i].FilePath);
            Assert.Equal(sortedPreview[i].RemovedCount, sortedApply[i].RemovedCount);
            Assert.Equal(sortedPreview[i].FinalCount, sortedApply[i].FinalCount);
         }
      }
      finally
      {
         // Restore all original contents
         foreach (var (file, content) in originalContents)
         {
            await File.WriteAllTextAsync(file, content);
         }
      }
   }

   [Fact]
   public async Task OrganizeUsings_Preview_DoesNotModifyFiles()
   {
      var filePath = GetCoreTestProjectPath("UnusedUsingsTests.cs");
      var originalContent = await File.ReadAllTextAsync(filePath);

      try
      {
         var result = await _tool.OrganizeUsingsAsync(scope: new FileScope { Path = filePath }, preview: true, cancellationToken: CT);
         AssertSuccess(result);

         var data = GetTypedData<OrganizeUsingsResponse>(result);
         Assert.False(data.Applied);
         Assert.Equal(ExpectedRemovedCount, data.TotalUsingsRemoved);

         // File content should be unchanged
         var afterContent = await File.ReadAllTextAsync(filePath);
         Assert.Equal(originalContent, afterContent);
      }
      finally
      {
         await File.WriteAllTextAsync(filePath, originalContent);
      }
   }

   [Fact]
   public async Task OrganizeUsings_Apply_ModifiesFile()
   {
      var filePath = GetCoreTestProjectPath("UnusedUsingsTests.cs");
      var originalContent = await File.ReadAllTextAsync(filePath);

      try
      {
         var result = await _tool.OrganizeUsingsAsync(scope: new FileScope { Path = filePath }, preview: false, cancellationToken: CT);
         AssertSuccess(result);

         var data = GetTypedData<OrganizeUsingsResponse>(result);
         Assert.True(data.Applied);
         Assert.Equal(ExpectedRemovedCount, data.TotalUsingsRemoved);

         // File content should be modified
         var afterContent = await File.ReadAllTextAsync(filePath);
         Assert.NotEqual(originalContent, afterContent);

         // Verify unused usings were actually removed
         Assert.DoesNotContain("System.Text.RegularExpressions", afterContent);
         Assert.DoesNotContain("System.Linq", afterContent);
         Assert.DoesNotContain("System.Threading.Tasks", afterContent);
      }
      finally
      {
         // Always restore original content
         await File.WriteAllTextAsync(filePath, originalContent);
      }
   }

   #endregion
}
