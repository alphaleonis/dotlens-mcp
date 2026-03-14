using DotLens.Infrastructure;
using DotLens.Tools.Formatting;
using Xunit;

namespace DotLens.Tests.Formatting;

public class FormatDocumentBatchToolTests : IsolatedProjectTestBase
{

   private FormatDocumentBatchTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      _tool = new FormatDocumentBatchTool(WorkspaceManager);
   }

   [Fact]
   public async Task FormatDocumentBatch_Project_ReturnsResults()
   {
      var result = await _tool.FormatDocumentBatchAsync(
         scope: new ProjectScope { Name = "CoreTestProject" },
         preview: true,
         cancellationToken: CT);

      AssertSuccess(result);
      Assert.NotNull(result.Data);
      Assert.False(result.Data.Applied);
      Assert.True(result.Data.FilesScanned > 0, "Should have scanned at least one file");
      Assert.True(result.Data.FilesScanned >= result.Data.FilesFormatted,
         "Formatted files cannot exceed scanned files");
   }

   [Fact]
   public async Task FormatDocumentBatch_Solution_ScansAllProjects()
   {
      var result = await _tool.FormatDocumentBatchAsync(
         scope: new SolutionScope(),
         preview: true,
         cancellationToken: CT);

      AssertSuccess(result);
      Assert.NotNull(result.Data);
      Assert.True(result.Data.FilesScanned > 0, "Should have scanned files across solution");
      Assert.True(result.Data.FilesScanned >= result.Data.FilesFormatted,
         "Formatted files cannot exceed scanned files");
   }

   [Fact]
   public async Task FormatDocumentBatch_FilePattern_FiltersFiles()
   {
      var result = await _tool.FormatDocumentBatchAsync(
         scope: new ProjectScope { Name = "CoreTestProject" },
         filePattern: "*Service*.cs",
         preview: true,
         cancellationToken: CT);

      AssertSuccess(result);
      Assert.NotNull(result.Data);

      // Verify the pattern actually matched files (guards against false-positive tests)
      Assert.True(result.Data.FilesScanned > 0,
         "Pattern '*Service*.cs' should match at least one file - verify TestService.cs exists in fixtures");

      // Files only contains files that NEEDED formatting changes
      // If any files needed formatting, verify they match the pattern
      if (result.Data.FilesFormatted > 0)
      {
         Assert.NotNull(result.Data.Files);
         Assert.NotEmpty(result.Data.Files);
         foreach (var file in result.Data.Files)
         {
            Assert.Contains("Service", file.FilePath, StringComparison.OrdinalIgnoreCase);
         }
      }
   }

   [Fact]
   public async Task FormatDocumentBatch_VerbositySummary_NoFileDetails()
   {
      var result = await _tool.FormatDocumentBatchAsync(
         scope: new ProjectScope { Name = "CoreTestProject" },
         preview: true,
         verbosity: ToolResultVerbosity.Summary,
         cancellationToken: CT);

      AssertSuccess(result);
      Assert.NotNull(result.Data);
      Assert.Null(result.Data.Files); // Summary verbosity excludes file details
      // Verify counts are meaningful even in summary mode
      Assert.True(result.Data.FilesScanned > 0, "Summary mode should still scan files");
      Assert.True(result.Data.FilesScanned >= result.Data.FilesFormatted,
         "Formatted files cannot exceed scanned files");
   }

   [Fact]
   public async Task FormatDocumentBatch_VerbosityFull_IncludesFormattedContent()
   {
      var result = await _tool.FormatDocumentBatchAsync(
         scope: new ProjectScope { Name = "CoreTestProject" },
         preview: true,
         verbosity: ToolResultVerbosity.Full,
         cancellationToken: CT);

      AssertSuccess(result);
      Assert.NotNull(result.Data);
      Assert.NotNull(result.Data.Files);

      // If any files need formatting, verify NewText is populated
      if (result.Data.FilesFormatted > 0)
      {
         Assert.Contains(result.Data.Files, f => f.NewText != null);
      }
   }

   [Fact]
   public async Task FormatDocumentBatch_InvalidProject_ThrowsToolException()
   {
      var ex = await Assert.ThrowsAsync<ToolException>(() =>
         _tool.FormatDocumentBatchAsync(
            scope: new ProjectScope { Name = "NonExistentProject" },
            preview: true,
            cancellationToken: CT));

      Assert.Equal(ErrorCodes.ProjectNotFound, ex.Code);
   }

   [Fact]
   public async Task FormatDocumentBatch_SingleFile_FormatsSpecificFile()
   {
      // Use absolute path for file scope (required by GetDocumentAsync)
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var result = await _tool.FormatDocumentBatchAsync(
         scope: new FileScope { Path = filePath },
         preview: true,
         cancellationToken: CT);

      AssertSuccess(result);
      Assert.NotNull(result.Data);
      // Should scan exactly one file
      Assert.Equal(1, result.Data.FilesScanned);
   }
}
