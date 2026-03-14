using DotLens.Infrastructure;
using DotLens.Tools.Refactoring;
using Xunit;

namespace DotLens.Tests.Refactoring;

/// <summary>
/// Tests for ApplyCodeActionTool with preview=true (no file modifications).
/// For apply mode tests (preview=false), see <see cref="ApplyCodeActionApplyModeTests"/>.
/// </summary>
public class ApplyCodeActionToolTests : IsolatedProjectTestBase
{

   private ApplyCodeActionTool _tool = null!;
   private GetCodeActionsTool _discoveryTool = null!;

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      _tool = new ApplyCodeActionTool(WorkspaceManager);
      _discoveryTool = new GetCodeActionsTool(WorkspaceManager);
   }

   #region Position Target + Title Selection Tests

   [Fact]
   public async Task ApplyCodeAction_PositionTarget_TitleSelection_Preview_ReturnsChanges()
   {
      // Line 15: private field - has "Encapsulate field" action
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var target = new PositionTarget { FilePath = filePath, Line = 15, Column = 16 };

      // First discover to get exact action title (partial match "Encapsulate field" is ambiguous)
      var discoveryResult = await _discoveryTool.GetCodeActionsAsync(target);
      AssertSuccess(discoveryResult);
      Assert.NotNull(discoveryResult.Data);
      var encapsulateAction = discoveryResult.Data.Actions
         .FirstOrDefault(a => a.Title.StartsWith("Encapsulate field", StringComparison.OrdinalIgnoreCase));
      Assert.NotNull(encapsulateAction);

      var selection = new TitleSelection { Title = encapsulateAction.Title };
      var result = await _tool.ApplyCodeActionAsync(target, selection, preview: true);

      AssertSuccess(result);
      Assert.NotNull(result.Data);
      Assert.False(result.Data.Applied);
      Assert.True(result.Data.Preview);
      Assert.StartsWith("Encapsulate field", result.Data.ActionTitle, StringComparison.OrdinalIgnoreCase);
      Assert.Equal(CodeActionKind.Refactoring, result.Data.ActionKind);
      Assert.NotEmpty(result.Data.ChangedFiles);

      // Preview mode should include NewText with expected changes
      foreach (var file in result.Data.ChangedFiles)
      {
         Assert.NotNull(file.NewText);
      }

      // Verify the encapsulation produces a property (not just that content exists)
      var mainFile = result.Data.ChangedFiles.First(f => f.FilePath.EndsWith("Classes.cs"));
      Assert.Contains("get;", mainFile.NewText); // Property accessor
   }

   [Fact]
   public async Task ApplyCodeAction_PositionTarget_TitleSelection_AmbiguousPartialMatch_ThrowsToolException()
   {
      // Ambiguous partial title match should throw
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var target = new PositionTarget { FilePath = filePath, Line = 15, Column = 16 };
      var selection = new TitleSelection { Title = "Encapsulate" }; // Matches multiple actions

      var ex = await AssertThrowsToolExceptionAsync(
         () => _tool.ApplyCodeActionAsync(target, selection, preview: true),
         ErrorCodes.AmbiguousSymbol);

      Assert.Contains("matches 2 actions", ex.Message);
   }

   [Fact]
   public async Task ApplyCodeAction_PositionTarget_TitleSelection_CaseInsensitive()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var target = new PositionTarget { FilePath = filePath, Line = 15, Column = 16 };

      // First discover to get exact action title, then test case insensitivity
      var discoveryResult = await _discoveryTool.GetCodeActionsAsync(target);
      AssertSuccess(discoveryResult);
      Assert.NotNull(discoveryResult.Data);
      var encapsulateAction = discoveryResult.Data.Actions
         .FirstOrDefault(a => a.Title.StartsWith("Encapsulate field", StringComparison.OrdinalIgnoreCase));
      Assert.NotNull(encapsulateAction);

      // Use the title in different case
      var selection = new TitleSelection { Title = encapsulateAction.Title.ToUpperInvariant() };
      var result = await _tool.ApplyCodeActionAsync(target, selection, preview: true);

      AssertSuccess(result);
      Assert.NotNull(result.Data);
      Assert.StartsWith("Encapsulate field", result.Data.ActionTitle, StringComparison.OrdinalIgnoreCase);
   }

   [Fact]
   public async Task ApplyCodeAction_PositionTarget_TitleSelection_NotFound_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var target = new PositionTarget { FilePath = filePath, Line = 15, Column = 16 };
      var selection = new TitleSelection { Title = "Nonexistent Action Title" };

      await AssertThrowsToolExceptionAsync(
         () => _tool.ApplyCodeActionAsync(target, selection, preview: true),
         ErrorCodes.SymbolNotFound);
   }

   [Fact]
   public async Task ApplyCodeAction_PositionTarget_KeySelection_ThrowsToolException()
   {
      // Key selection doesn't work with position targets
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var target = new PositionTarget { FilePath = filePath, Line = 15, Column = 16 };
      var selection = new KeySelection { Key = "12345678" };

      var ex = await AssertThrowsToolExceptionAsync(
         () => _tool.ApplyCodeActionAsync(target, selection, preview: true),
         ErrorCodes.InvalidParameter);

      Assert.Contains("Key selection only works with diagnostic targets", ex.Message);
   }

   [Fact]
   public async Task ApplyCodeAction_PositionTarget_InvalidPosition_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var target = new PositionTarget { FilePath = filePath, Line = 99999, Column = 1 };
      var selection = new TitleSelection { Title = "Some Action" };

      await AssertThrowsToolExceptionAsync(
         () => _tool.ApplyCodeActionAsync(target, selection, preview: true),
         "INVALID_PARAMETER");
   }

   [Fact]
   public async Task ApplyCodeAction_PositionTarget_FileNotFound_ThrowsToolException()
   {
      var target = new PositionTarget { FilePath = "NonExistent/file.cs", Line = 1, Column = 1 };
      var selection = new TitleSelection { Title = "Some Action" };

      await AssertThrowsToolExceptionAsync(
         () => _tool.ApplyCodeActionAsync(target, selection, preview: true),
         ErrorCodes.FileNotInSolution);
   }

   #endregion

   #region Diagnostic Target + Title Selection Tests

   [Fact]
   public async Task ApplyCodeAction_DiagnosticTarget_TitleSelection_Preview_ReturnsChanges()
   {
      // FixableDiagnostics.cs has CS0168 (unused variable) at line 27
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
      AssertSuccess(discoveryResult);
      Assert.NotNull(discoveryResult.Data);
      Assert.NotEmpty(discoveryResult.Data.Actions);
      var actionTitle = discoveryResult.Data.Actions[0].Title;

      var selection = new TitleSelection { Title = actionTitle };
      var result = await _tool.ApplyCodeActionAsync(target, selection, preview: true);

      AssertSuccess(result);
      Assert.NotNull(result.Data);
      Assert.False(result.Data.Applied);
      Assert.True(result.Data.Preview);
      Assert.Equal(actionTitle, result.Data.ActionTitle);
      Assert.Equal(CodeActionKind.Fix, result.Data.ActionKind);
      Assert.Equal("CS0168", result.Data.DiagnosticId);
      Assert.NotNull(result.Data.ActionKey); // Diagnostic target should have key
      Assert.NotEmpty(result.Data.ChangedFiles);
   }

   #endregion

   #region Diagnostic Target + Key Selection Tests

   [Fact]
   public async Task ApplyCodeAction_DiagnosticTarget_KeySelection_Preview_ReturnsChanges()
   {
      // First discover the actions to get a valid key
      var filePath = GetCoreTestProjectPath("Analysis/FixableDiagnostics.cs");
      var target = new DiagnosticTarget
      {
         FilePath = filePath,
         Line = 27,
         Column = 10,
         DiagnosticId = "CS0168"
      };

      var discoveryResult = await _discoveryTool.GetCodeActionsAsync(target);
      AssertSuccess(discoveryResult);
      Assert.NotNull(discoveryResult.Data);
      Assert.NotEmpty(discoveryResult.Data.Actions);
      var actionKey = discoveryResult.Data.Actions[0].Key;
      Assert.NotNull(actionKey);

      var selection = new KeySelection { Key = actionKey };
      var result = await _tool.ApplyCodeActionAsync(target, selection, preview: true);

      AssertSuccess(result);
      Assert.NotNull(result.Data);
      Assert.False(result.Data.Applied);
      Assert.True(result.Data.Preview);
      Assert.Equal(CodeActionKind.Fix, result.Data.ActionKind);
      Assert.Equal(actionKey, result.Data.ActionKey);
      Assert.NotEmpty(result.Data.ChangedFiles);
   }

   [Fact]
   public async Task ApplyCodeAction_DiagnosticTarget_KeySelection_InvalidKey_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Analysis/FixableDiagnostics.cs");
      var target = new DiagnosticTarget
      {
         FilePath = filePath,
         Line = 27,
         Column = 10,
         DiagnosticId = "CS0168"
      };
      var selection = new KeySelection { Key = "00000000" }; // Invalid key

      var ex = await AssertThrowsToolExceptionAsync(
         () => _tool.ApplyCodeActionAsync(target, selection, preview: true),
         ErrorCodes.SymbolNotFound);

      Assert.Contains("00000000", ex.Message);
   }

   [Fact]
   public async Task ApplyCodeAction_DiagnosticTarget_DiagnosticNotFound_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var target = new DiagnosticTarget
      {
         FilePath = filePath,
         Line = 12,
         Column = 14,
         DiagnosticId = "CS9999"
      };
      var selection = new TitleSelection { Title = "Any" };

      await AssertThrowsToolExceptionAsync(
         () => _tool.ApplyCodeActionAsync(target, selection, preview: true),
         ErrorCodes.SymbolNotFound);
   }

   #endregion

   #region Response Structure Tests

   [Fact]
   public async Task ApplyCodeAction_Preview_ResponseStructure()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var target = new PositionTarget { FilePath = filePath, Line = 15, Column = 16 };

      // First discover to get exact action title (partial match "Encapsulate field" is ambiguous)
      var discoveryResult = await _discoveryTool.GetCodeActionsAsync(target);
      AssertSuccess(discoveryResult);
      Assert.NotNull(discoveryResult.Data);
      var encapsulateAction = discoveryResult.Data.Actions
         .FirstOrDefault(a => a.Title.StartsWith("Encapsulate field", StringComparison.OrdinalIgnoreCase));
      Assert.NotNull(encapsulateAction);

      var selection = new TitleSelection { Title = encapsulateAction.Title };
      var result = await _tool.ApplyCodeActionAsync(target, selection, preview: true);

      AssertSuccess(result);
      var data = result.Data;
      Assert.NotNull(data);

      // Check all expected properties
      Assert.False(data.Applied);
      Assert.True(data.Preview);
      Assert.NotEmpty(data.ActionTitle);
      Assert.True(data.ActionKind == CodeActionKind.Fix || data.ActionKind == CodeActionKind.Refactoring);
      Assert.NotEmpty(data.ChangedFiles);

      // Preview mode should have NewText
      foreach (var file in data.ChangedFiles)
      {
         Assert.NotEmpty(file.FilePath);
         Assert.True(file.ChangeCount > 0);
         Assert.NotNull(file.NewText);
      }
   }

   #endregion
}
