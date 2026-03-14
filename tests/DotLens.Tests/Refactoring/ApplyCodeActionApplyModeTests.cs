using DotLens.Tools.Refactoring;
using Xunit;

namespace DotLens.Tests.Refactoring;

/// <summary>
/// Tests for ApplyCodeActionTool with preview=false (actual file modifications).
/// Uses IsolatedProjectTestBase to work on a temp copy of test fixtures.
/// </summary>
public class ApplyCodeActionApplyModeTests : IsolatedProjectTestBase
{
   private ApplyCodeActionTool _tool = null!;
   private GetCodeActionsTool _discoveryTool = null!;

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      _tool = new ApplyCodeActionTool(WorkspaceManager);
      _discoveryTool = new GetCodeActionsTool(WorkspaceManager);
   }

   [Fact]
   public async Task ApplyCodeAction_ApplyMode_ModifiesFile()
   {
      // Arrange: Find a code fix for CS0168 (unused variable)
      var filePath = GetCoreTestProjectPath("Analysis/FixableDiagnostics.cs");
      var target = new DiagnosticTarget
      {
         FilePath = filePath,
         Line = 27,
         Column = 10,
         DiagnosticId = "CS0168"
      };

      // First discover the actions to get a valid title
      var discoveryResult = await _discoveryTool.GetCodeActionsAsync(target);
      Assert.NotNull(discoveryResult.Data);
      Assert.NotEmpty(discoveryResult.Data.Actions);
      var actionTitle = discoveryResult.Data.Actions[0].Title;

      // Read original file content
      var originalContent = await File.ReadAllTextAsync(filePath, CT);
      Assert.Contains("int unusedVariable;", originalContent);

      // Act: Apply the fix with preview=false
      var selection = new TitleSelection { Title = actionTitle };
      var result = await _tool.ApplyCodeActionAsync(target, selection, preview: false);

      // Assert: Verify the response indicates applied
      Assert.NotNull(result.Data);
      Assert.True(result.Data.Applied);
      Assert.False(result.Data.Preview);
      Assert.NotEmpty(result.Data.ChangedFiles);

      // Apply mode should NOT include NewText (it's already written)
      foreach (var file in result.Data.ChangedFiles)
      {
         Assert.Null(file.NewText);
      }

      // Assert: Verify file was actually modified on disk
      var modifiedContent = await File.ReadAllTextAsync(filePath, CT);
      Assert.DoesNotContain("int unusedVariable;", modifiedContent);
   }

   [Fact]
   public async Task ApplyCodeAction_ApplyMode_PositionTarget_ModifiesFile()
   {
      // Arrange: Use "Encapsulate field" refactoring on a field
      // Line 15: private field in Classes.cs
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var target = new PositionTarget { FilePath = filePath, Line = 15, Column = 16 };

      // First discover the available actions to get an exact title
      // (partial match "Encapsulate field" would be ambiguous)
      var discoveryResult = await _discoveryTool.GetCodeActionsAsync(target);
      Assert.NotNull(discoveryResult.Data);
      var encapsulateAction = discoveryResult.Data.Actions
         .FirstOrDefault(a => a.Title.StartsWith("Encapsulate field", StringComparison.OrdinalIgnoreCase));
      Assert.NotNull(encapsulateAction);

      var selection = new TitleSelection { Title = encapsulateAction.Title };

      // Read original file content
      var originalContent = await File.ReadAllTextAsync(filePath, CT);

      // Act: Apply the refactoring with preview=false
      var result = await _tool.ApplyCodeActionAsync(target, selection, preview: false);

      // Assert: Verify the response indicates applied
      Assert.NotNull(result.Data);
      Assert.True(result.Data.Applied);
      Assert.False(result.Data.Preview);
      Assert.NotEmpty(result.Data.ChangedFiles);

      // Assert: Verify file was modified on disk
      var modifiedContent = await File.ReadAllTextAsync(filePath, CT);
      Assert.NotEqual(originalContent, modifiedContent);
      // The encapsulate field refactoring should add a property
      Assert.Contains("get;", modifiedContent);
   }
}
