using DotLens.Tools.Refactoring;
using Xunit;

namespace DotLens.Tests.Refactoring;

public class AddUsingToolTests : IsolatedProjectTestBase
{

   private AddUsingTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      _tool = new AddUsingTool(WorkspaceManager);
   }

   [Fact]
   public async Task AddUsing_PreviewMode_ReturnsNewContent()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var result = await _tool.AddUsingAsync(
          filePath,
          "System.Text.RegularExpressions",
          preview: true,
          cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.True(data.GetBoolOrNull("preview"));
      Assert.False(data.GetBoolOrNull("added"));
      Assert.Contains("System.Text.RegularExpressions", data.GetStringOrNull("newFileContent"));
   }

   [Fact]
   public async Task AddUsing_ExistingNamespace_ReportsAlreadyExists()
   {
      // Classes.cs has explicit "using System.Collections.Generic;" at file level
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var result = await _tool.AddUsingAsync(
          filePath,
          "System.Collections.Generic",
          preview: true,
          cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      Assert.True(data.GetBoolOrNull("alreadyExists"));
   }

   [Fact]
   public async Task AddUsing_EmptyNamespace_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      await AssertThrowsToolExceptionAsync(
         () => _tool.AddUsingAsync(filePath, "", cancellationToken: CT),
         "INVALID_PARAMETER");
   }

   [Fact]
   public async Task AddUsing_PreviewMode_OutputHasProperSpacing()
   {
      // Regression test for bug: "usingSystem.Diagnostics;" instead of "using System.Diagnostics;"
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var result = await _tool.AddUsingAsync(
          filePath,
          "System.Diagnostics",
          preview: true,
          cancellationToken: CT);

      AssertSuccess(result);
      var data = GetData(result);
      var newContent = data.GetStringOrNull("newFileContent");

      Assert.NotNull(newContent);
      // Must have proper spacing: "using System.Diagnostics" not "usingSystem.Diagnostics"
      Assert.Contains("using System.Diagnostics;", newContent);
      Assert.DoesNotContain("usingSystem.Diagnostics;", newContent);
   }
}
