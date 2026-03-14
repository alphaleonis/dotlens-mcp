using DotLens.Tools.Worker;
using Xunit;

namespace DotLens.Tests.Worker;

public class SyncDocumentsToolTests : IsolatedProjectTestBase
{

   private SyncDocumentsTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      _tool = new SyncDocumentsTool(WorkspaceManager);
   }

   [Fact]
   public async Task SyncDocuments_WithNoFiles_SyncsAllDocuments()
   {
      var result = await _tool.SyncDocumentsAsync(null, CT);

      AssertSuccess(result);
      var data = GetData(result);

      // Verify structure exists
      Assert.NotNull(data["updated"]);
      Assert.NotNull(data["added"]);
      Assert.NotNull(data["removed"]);
      Assert.NotNull(data["totalSynced"]);

      // totalSynced should match the document count (syncing all files)
      var totalSynced = data.GetInt("totalSynced");
      Assert.True(totalSynced >= 1, $"Expected at least 1 synced document, got {totalSynced}");
   }

   [Fact]
   public async Task SyncDocuments_WithSpecificFile_SyncsOnlyThatFile()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var result = await _tool.SyncDocumentsAsync(new List<string> { filePath }, CT);

      AssertSuccess(result);
      var data = GetData(result);
      var updatedFiles = data.GetArrayOrNull("updatedFiles");
      Assert.NotNull(updatedFiles);
      Assert.Equal(1, data.GetIntOrNull("totalSynced"));
   }

   [Fact]
   public async Task SyncDocuments_WithNonExistentFile_HandlesGracefully()
   {
      var result = await _tool.SyncDocumentsAsync(new List<string> { "NonExistent.cs" }, CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal(0, data.GetIntOrNull("totalSynced"));
   }

   [Fact]
   public async Task SyncDocuments_WithPathTraversal_ReturnsSecurityError()
   {
      var result = await _tool.SyncDocumentsAsync(
          new List<string> { "../../../../../../etc/passwd" }, CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.Equal(0, data.GetIntOrNull("totalSynced"));
      var errors = data.GetArrayOrNull("errors");
      Assert.NotNull(errors);
      Assert.Single(errors!);
      Assert.Contains("traversal", errors[0].GetStringOrNull("error"));
   }
}
