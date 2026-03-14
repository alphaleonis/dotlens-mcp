using DotLens.Infrastructure;
using DotLens.Tools.Refactoring;
using Xunit;

namespace DotLens.Tests.Refactoring;

[Collection("TestFixtures")]
public class GetCodeActionsToolTests : TestFixtureTestBase
{
   public GetCodeActionsToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetCodeActionsTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      _tool = new GetCodeActionsTool(Workspace);
   }

   #region Position Target Tests

   [Fact]
   public async Task GetCodeActions_PositionTarget_ReturnsRefactorings()
   {
      // Line 15: "   private int _privateField;"
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var target = new PositionTarget { FilePath = filePath, Line = 15, Column = 16 };

      var result = await _tool.GetCodeActionsAsync(target);

      AssertSuccess(result);
      Assert.NotNull(result.Data);
      Assert.NotEmpty(result.Data.Actions);

      // Should have "Encapsulate field" refactorings
      var encapsulateActions = result.Data.Actions
         .Where(a => a.Title.StartsWith("Encapsulate field", StringComparison.OrdinalIgnoreCase))
         .ToList();
      Assert.NotEmpty(encapsulateActions);

      // All encapsulate field actions should be refactorings without keys
      foreach (var action in encapsulateActions)
      {
         Assert.Equal(CodeActionKind.Refactoring, action.Kind);
         Assert.Null(action.Key); // Position targets don't have stable keys
      }

      // Verify response metadata is consistent with actions
      Assert.True(result.Data.RefactoringCount > 0);
   }

   [Fact]
   public async Task GetCodeActions_PositionTarget_WithSelection_ReturnsSelectionActions()
   {
      // Select a statement: Line 45: "      _privateField++;"
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var target = new PositionTarget
      {
         FilePath = filePath,
         Line = 45,
         Column = 7,
         EndLine = 45,
         EndColumn = 22
      };

      var result = await _tool.GetCodeActionsAsync(target);

      AssertSuccess(result);
      Assert.NotNull(result.Data);
      Assert.NotEmpty(result.Data.Actions);

      // Verify extract method refactoring is available for the selected statement
      var extractAction = result.Data.Actions.FirstOrDefault(a =>
         a.Title.StartsWith("Extract method", StringComparison.OrdinalIgnoreCase));
      Assert.NotNull(extractAction);
      Assert.Equal(CodeActionKind.Refactoring, extractAction.Kind);
      Assert.Null(extractAction.Key); // Position-based actions don't have keys
   }

   [Fact]
   public async Task GetCodeActions_PositionTarget_InvalidPosition_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var target = new PositionTarget { FilePath = filePath, Line = 99999, Column = 1 };

      await AssertThrowsToolExceptionAsync(
         () => _tool.GetCodeActionsAsync(target),
         "INVALID_PARAMETER");
   }

   [Fact]
   public async Task GetCodeActions_PositionTarget_FileNotFound_ThrowsToolException()
   {
      var target = new PositionTarget { FilePath = "NonExistent/file.cs", Line = 1, Column = 1 };

      await AssertThrowsToolExceptionAsync(
         () => _tool.GetCodeActionsAsync(target),
         ErrorCodes.FileNotInSolution);
   }

   [Fact]
   public async Task GetCodeActions_PositionTarget_TitlePattern_FiltersActions()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var target = new PositionTarget { FilePath = filePath, Line = 15, Column = 16 };

      var result = await _tool.GetCodeActionsAsync(target, titlePattern: "*Encapsulate*");

      AssertSuccess(result);
      Assert.NotNull(result.Data);
      Assert.NotEmpty(result.Data.Actions);

      // All returned actions should match the pattern
      foreach (var action in result.Data.Actions)
      {
         Assert.Contains("Encapsulate", action.Title, StringComparison.OrdinalIgnoreCase);
      }
   }

   [Fact]
   public async Task GetCodeActions_PositionTarget_TitlePattern_NoMatches_ReturnsEmptyWithMessage()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var target = new PositionTarget { FilePath = filePath, Line = 15, Column = 16 };

      var result = await _tool.GetCodeActionsAsync(target, titlePattern: "*NonexistentPattern*");

      AssertSuccess(result);
      Assert.NotNull(result.Data);
      Assert.Empty(result.Data.Actions);
      Assert.NotNull(result.Data.Message);
      Assert.Contains("NonexistentPattern", result.Data.Message);
   }

   [Fact]
   public async Task GetCodeActions_PositionTarget_ResponseStructure()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var target = new PositionTarget { FilePath = filePath, Line = 15, Column = 16 };

      var result = await _tool.GetCodeActionsAsync(target);

      AssertSuccess(result);
      var data = result.Data;
      Assert.NotNull(data);
      Assert.NotEmpty(data.Actions);

      // Verify counts match actions
      var actualFixCount = data.Actions.Count(a => a.Kind == CodeActionKind.Fix);
      var actualRefactoringCount = data.Actions.Count(a => a.Kind == CodeActionKind.Refactoring);

      Assert.Equal(actualFixCount, data.FixCount);
      Assert.Equal(actualRefactoringCount, data.RefactoringCount);

      // Verify action structure
      foreach (var action in data.Actions)
      {
         Assert.True(action.Index >= 0);
         Assert.NotEmpty(action.Title);
         Assert.True(action.Kind == CodeActionKind.Fix || action.Kind == CodeActionKind.Refactoring);
         // Count >= 1 is correct: actions are grouped by title, so Count represents
         // how many duplicate actions were merged (usually 1, but can be > 1)
         Assert.True(action.Count >= 1);
      }

      // Position targets should not have diagnostic info
      Assert.Null(data.Diagnostic);
   }

   [Fact]
   public async Task GetCodeActions_PositionTarget_ActionsAreSorted()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var target = new PositionTarget { FilePath = filePath, Line = 15, Column = 16 };

      var result = await _tool.GetCodeActionsAsync(target);

      AssertSuccess(result);
      var data = result.Data;
      Assert.NotNull(data);

      // Sorting test requires multiple actions to be meaningful
      Assert.True(data.Actions.Count > 1, "Sorting test requires at least 2 actions");

      // Verify sorting: first by kind (Fix < Refactoring), then by title
      for (int i = 1; i < data.Actions.Count; i++)
      {
         var prev = data.Actions[i - 1];
         var curr = data.Actions[i];

         if (prev.Kind == curr.Kind)
         {
            Assert.True(string.Compare(prev.Title, curr.Title, StringComparison.Ordinal) <= 0,
               $"Actions not sorted: '{prev.Title}' should come before '{curr.Title}'");
         }
         else
         {
            Assert.True(prev.Kind < curr.Kind,
               $"Actions not sorted by kind: '{prev.Kind}' should come before '{curr.Kind}'");
         }
      }
   }

   #endregion

   #region Diagnostic Target Tests

   [Fact]
   public async Task GetCodeActions_DiagnosticTarget_ReturnsFixesWithKeys()
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

      var result = await _tool.GetCodeActionsAsync(target);

      AssertSuccess(result);
      Assert.NotNull(result.Data);
      Assert.NotEmpty(result.Data.Actions);

      // All actions should be fixes with stable keys
      foreach (var action in result.Data.Actions)
      {
         Assert.Equal(CodeActionKind.Fix, action.Kind);
         Assert.NotNull(action.Key);
         Assert.Equal(8, action.Key.Length); // 8-char hex hash
         Assert.Matches("^[0-9a-f]{8}$", action.Key);
      }

      // Should have diagnostic info
      Assert.NotNull(result.Data.Diagnostic);
      Assert.Equal("CS0168", result.Data.Diagnostic.Id);
      // Use Contains for key parts rather than exact message (Roslyn wording may change)
      Assert.Contains("unusedVariable", result.Data.Diagnostic.Message);
      Assert.Contains("declared", result.Data.Diagnostic.Message);
   }

   [Fact]
   public async Task GetCodeActions_DiagnosticTarget_DiagnosticNotFound_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var target = new DiagnosticTarget
      {
         FilePath = filePath,
         Line = 12,
         Column = 14,
         DiagnosticId = "CS9999"
      };

      await AssertThrowsToolExceptionAsync(
         () => _tool.GetCodeActionsAsync(target),
         ErrorCodes.SymbolNotFound);
   }

   [Fact]
   public async Task GetCodeActions_DiagnosticTarget_KeysAreStable()
   {
      var filePath = GetCoreTestProjectPath("Analysis/FixableDiagnostics.cs");
      var target = new DiagnosticTarget
      {
         FilePath = filePath,
         Line = 27,
         Column = 10,
         DiagnosticId = "CS0168"
      };

      // Call twice and verify keys are the same
      var result1 = await _tool.GetCodeActionsAsync(target);
      var result2 = await _tool.GetCodeActionsAsync(target);

      AssertSuccess(result1);
      AssertSuccess(result2);
      Assert.NotNull(result1.Data);
      Assert.NotNull(result2.Data);
      Assert.NotEmpty(result1.Data.Actions);

      // Same keys in same order
      var keys1 = result1.Data.Actions.Select(a => a.Key).ToList();
      var keys2 = result2.Data.Actions.Select(a => a.Key).ToList();
      Assert.Equal(keys1, keys2);
   }

   [Fact]
   public async Task GetCodeActions_DiagnosticTarget_FallbackStrategy_FindsDiagnostic()
   {
      // Use line 1, column 1 (far from the actual diagnostic at line 27)
      // The tool should still find CS0168 using fallback strategy
      var filePath = GetCoreTestProjectPath("Analysis/FixableDiagnostics.cs");
      var target = new DiagnosticTarget
      {
         FilePath = filePath,
         Line = 1,
         Column = 1,
         DiagnosticId = "CS0168"
      };

      var result = await _tool.GetCodeActionsAsync(target);

      AssertSuccess(result);
      Assert.NotNull(result.Data);
      Assert.Equal("CS0168", result.Data.Diagnostic?.Id);

      // Verify actions were returned and are valid code fixes
      Assert.NotEmpty(result.Data.Actions);
      foreach (var action in result.Data.Actions)
      {
         Assert.Equal(CodeActionKind.Fix, action.Kind);
         Assert.NotNull(action.Key);
      }
   }

   [Fact]
   public async Task GetCodeActions_DiagnosticTarget_FileNotFound_ThrowsToolException()
   {
      var target = new DiagnosticTarget
      {
         FilePath = "NonExistent/file.cs",
         Line = 1,
         Column = 1,
         DiagnosticId = "CS0168"
      };

      await AssertThrowsToolExceptionAsync(
         () => _tool.GetCodeActionsAsync(target),
         ErrorCodes.FileNotInSolution);
   }

   #endregion
}
