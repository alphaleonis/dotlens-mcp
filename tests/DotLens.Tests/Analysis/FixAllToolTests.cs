using DotLens.Infrastructure;
using DotLens.Infrastructure.Results;
using DotLens.Tools.Analysis;
using Xunit;

namespace DotLens.Tests.Analysis;

public class FixAllToolTests : IsolatedProjectTestBase
{

   private FixAllTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      _tool = new FixAllTool(WorkspaceManager);
   }

   [Fact]
   public async Task FixAll_InvalidScope_ThrowsToolException()
   {
      var filePath = GetAnalyzerTestProjectPath("CompilerWarnings.cs");

      await AssertThrowsToolExceptionAsync(
         () => _tool.FixAllAsync("CS0168", "invalid", CT, filePath),
         "INVALID_PARAMETER");
   }

   [Fact]
   public async Task FixAll_DocumentScope_RequiresFilePath()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.FixAllAsync("CS0168", "document", CT),
         "INVALID_PARAMETER");
   }

   [Fact]
   public async Task FixAll_NonExistentDiagnostic_ReturnsNoFixes()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");

      var result = await _tool.FixAllAsync("NONEXISTENT99", "document", CT, filePath);

      var data = GetTypedData<FixAllResponse>(result);
      Assert.Equal(0, data.DiagnosticsFound);
      Assert.False(data.Applied);
   }

   [Fact]
   public async Task FixAll_ProjectScope_AcceptsProjectName()
   {
      // Test that project scope works with projectName parameter
      var result = await _tool.FixAllAsync(
         "CS9999", // Non-existent diagnostic
         "project",
         CT,
         projectName: "AnalyzerTestProject");

      var data = GetTypedData<FixAllResponse>(result);
      Assert.Equal(0, data.DiagnosticsFound);
   }

   [Fact]
   public async Task FixAll_UnknownProject_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.FixAllAsync("CS0168", "project", CT, projectName: "NonExistentProject"),
         "INVALID_PARAMETER");
   }

   [Fact]
   public async Task FixAll_PreviewMode_DoesNotModifyFile()
   {
      var filePath = GetAnalyzerTestProjectPath("CompilerWarnings.cs");
      var originalContent = await File.ReadAllTextAsync(filePath);

      try
      {
         // This may throw if no code fix provider is available, or return no diagnostics
         try
         {
            var result = await _tool.FixAllAsync("CS0168", "document", CT, filePath, preview: true);
            var data = GetTypedData<FixAllResponse>(result);

            // If there are diagnostics found, preview should be true and not applied
            if (data.DiagnosticsFound > 0)
            {
               Assert.True(data.Preview);
               Assert.False(data.Applied);
            }
         }
         catch (ToolException ex) when (ex.Code == "ANALYSIS_FAILED")
         {
            // Expected if no code fix provider is available in test environment
         }

         // Verify file wasn't modified regardless of outcome
         var currentContent = await File.ReadAllTextAsync(filePath);
         Assert.Equal(originalContent, currentContent);
      }
      finally
      {
         // Ensure file is restored if modified
         await File.WriteAllTextAsync(filePath, originalContent);
      }
   }
}
