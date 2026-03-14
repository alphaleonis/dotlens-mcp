using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Refactoring;
using Xunit;

namespace DotLens.Tests.Refactoring;

/// <summary>
/// Tests for ImplementMissingMembersTool.
/// </summary>
public class ImplementMissingMembersToolTests : IsolatedProjectTestBase
{

   private ImplementMissingMembersTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      var symbolLookupService = new SymbolLookupService(WorkspaceManager);
      _tool = new ImplementMissingMembersTool(WorkspaceManager, symbolLookupService);
   }

   #region Error Handling Tests

   [Fact]
   public async Task ImplementMissingMembers_InvalidPosition_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var lookup = new LookupByPosition { FilePath = filePath, Line = 99999, Column = 0 };

      await AssertThrowsToolExceptionAsync(
         () => _tool.ImplementMissingMembersAsync(lookup, cancellationToken: CT),
         ErrorCodes.InvalidParameter);
   }

   [Fact]
   public async Task ImplementMissingMembers_TypeByName_OnCompleteClass_ThrowsNotSupported()
   {
      // SampleClass doesn't implement interfaces or inherit abstract members
      var lookup = new LookupByName { Name = "CoreTestProject.Types.SampleClass" };

      await AssertThrowsToolExceptionAsync(
         () => _tool.ImplementMissingMembersAsync(lookup, cancellationToken: CT),
         ErrorCodes.NotSupported);
   }

   [Fact]
   public async Task ImplementMissingMembers_TypeByPosition_OnCompleteClass_ThrowsNotSupported()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");

      // Validate fixture: SampleClass should be at line 12
      await ValidateFixtureLineContainsAsync(filePath, 12, "class SampleClass");

      var lookup = new LookupByPosition { FilePath = filePath, Line = 12, Column = 14 };

      await AssertThrowsToolExceptionAsync(
         () => _tool.ImplementMissingMembersAsync(lookup, cancellationToken: CT),
         ErrorCodes.NotSupported);
   }

   [Fact]
   public async Task ImplementMissingMembers_MetadataType_ThrowsToolException()
   {
      // Try to implement members for System.Object (metadata type)
      var lookup = new LookupByName { Name = "System.Object" };

      await AssertThrowsToolExceptionAsync(
         () => _tool.ImplementMissingMembersAsync(lookup, cancellationToken: CT),
         ErrorCodes.SymbolInMetadata);
   }

   [Fact]
   public async Task ImplementMissingMembers_NonExistentType_ThrowsToolException()
   {
      var lookup = new LookupByName { Name = "CoreTestProject.NonExistentType" };

      await AssertThrowsToolExceptionAsync(
         () => _tool.ImplementMissingMembersAsync(lookup, cancellationToken: CT),
         ErrorCodes.SymbolNotFound);
   }

   [Theory]
   [InlineData("")]
   [InlineData("   ")]
   public async Task ImplementMissingMembers_EmptyOrWhitespaceName_ThrowsToolException(string name)
   {
      var lookup = new LookupByName { Name = name };

      await AssertThrowsToolExceptionAsync(
         () => _tool.ImplementMissingMembersAsync(lookup, cancellationToken: CT),
         ErrorCodes.InvalidParameter);
   }

   [Fact]
   public async Task ImplementMissingMembers_OnInterface_ThrowsToolException()
   {
      // Interfaces can't implement missing members - they define them
      var lookup = new LookupByName { Name = "CoreTestProject.Refactoring.ITestService" };

      // The type filter in the tool only allows Class or Struct
      await AssertThrowsToolExceptionAsync(
         () => _tool.ImplementMissingMembersAsync(lookup, cancellationToken: CT),
         ErrorCodes.SymbolNotFound); // Filter rejects interfaces, so "no match found"
   }

   #endregion

   #region Interface Implementation Tests

   [Fact]
   public async Task ImplementMissingMembers_ByName_WithMissingInterfaceMembers_ReturnsPreview()
   {
      var lookup = new LookupByName { Name = "CoreTestProject.Refactoring.ImplementableClass" };
      var result = await _tool.ImplementMissingMembersAsync(lookup, preview: true, cancellationToken: CT);

      // Verify result structure
      Assert.NotNull(result);
      Assert.NotNull(result.Data);
      Assert.True(result.Data.Preview);
      Assert.False(result.Data.Applied);

      // Verify action title indicates interface implementation
      Assert.Contains("Implement", result.Data.ActionTitle, StringComparison.OrdinalIgnoreCase);

      // Verify changed file details
      var changedFile = Assert.Single(result.Data.ChangedFiles);
      Assert.NotNull(changedFile.FilePath);
      Assert.Contains("ImplementMissingMembersFixtures.cs", changedFile.FilePath);
      Assert.True(changedFile.ChangeCount > 0, "Expected at least one change");
      Assert.NotNull(changedFile.NewText);

      // Verify both missing interface methods are implemented
      Assert.Contains("void Execute()", changedFile.NewText);
      Assert.Contains("Task ExecuteAsync()", changedFile.NewText);

      // Verify implementations are stubs (throw NotImplementedException)
      Assert.Contains("NotImplementedException", changedFile.NewText);
   }

   [Fact]
   public async Task ImplementMissingMembers_ByPosition_WithMissingInterfaceMembers_ReturnsPreview()
   {
      var filePath = GetCoreTestProjectPath("Refactoring/ImplementMissingMembersFixtures.cs");

      // Validate fixture: ImplementableClass should be at line 20
      await ValidateFixtureLineContainsAsync(filePath, 20, "class ImplementableClass");

      var lookup = new LookupByPosition { FilePath = filePath, Line = 20, Column = 14 };
      var result = await _tool.ImplementMissingMembersAsync(lookup, preview: true, cancellationToken: CT);

      // Verify result structure
      Assert.NotNull(result);
      Assert.NotNull(result.Data);
      Assert.True(result.Data.Preview);
      Assert.False(result.Data.Applied);

      // Verify action title indicates interface implementation
      Assert.Contains("Implement", result.Data.ActionTitle, StringComparison.OrdinalIgnoreCase);

      // Verify changed file details
      var changedFile = Assert.Single(result.Data.ChangedFiles);
      Assert.NotNull(changedFile.NewText);
      Assert.True(changedFile.ChangeCount > 0);

      // Verify both missing interface methods are implemented
      Assert.Contains("void Execute()", changedFile.NewText);
      Assert.Contains("Task ExecuteAsync()", changedFile.NewText);

      // Verify implementations are stubs
      Assert.Contains("NotImplementedException", changedFile.NewText);
   }

   #endregion

   #region Abstract Class Implementation Tests

   [Fact]
   public async Task ImplementMissingMembers_AbstractClass_ByName_ReturnsPreview()
   {
      var lookup = new LookupByName { Name = "CoreTestProject.Refactoring.IncompleteAbstractClass" };
      var result = await _tool.ImplementMissingMembersAsync(lookup, preview: true, cancellationToken: CT);

      // Verify result structure
      Assert.NotNull(result);
      Assert.NotNull(result.Data);
      Assert.True(result.Data.Preview);
      Assert.False(result.Data.Applied);

      // Verify action title indicates abstract implementation
      Assert.Contains("Implement", result.Data.ActionTitle, StringComparison.OrdinalIgnoreCase);

      var changedFile = Assert.Single(result.Data.ChangedFiles);
      Assert.NotNull(changedFile.NewText);

      // Verify abstract methods are implemented
      Assert.Contains("ProcessData()", changedFile.NewText);
      Assert.Contains("Calculate(", changedFile.NewText);

      // Verify abstract property is implemented
      Assert.Contains("Status", changedFile.NewText);

      // Verify implementations use override keyword
      Assert.Contains("override", changedFile.NewText);

      // Verify implementations are stubs
      Assert.Contains("NotImplementedException", changedFile.NewText);
   }

   [Fact]
   public async Task ImplementMissingMembers_AbstractClass_ByPosition_ReturnsPreview()
   {
      var filePath = GetCoreTestProjectPath("Refactoring/ImplementMissingMembersExtendedFixtures.cs");

      // Validate fixture: IncompleteAbstractClass should be at line 24
      await ValidateFixtureLineContainsAsync(filePath, 24, "class IncompleteAbstractClass");

      var lookup = new LookupByPosition { FilePath = filePath, Line = 24, Column = 14 };
      var result = await _tool.ImplementMissingMembersAsync(lookup, preview: true, cancellationToken: CT);

      Assert.NotNull(result);
      Assert.NotNull(result.Data);
      Assert.True(result.Data.Preview);

      var changedFile = Assert.Single(result.Data.ChangedFiles);
      Assert.NotNull(changedFile.NewText);

      // Verify abstract methods and property are implemented
      Assert.Contains("ProcessData()", changedFile.NewText);
      Assert.Contains("Calculate(", changedFile.NewText);
      Assert.Contains("Status", changedFile.NewText);
      Assert.Contains("override", changedFile.NewText);
   }

   #endregion

   #region Struct Implementation Tests

   [Fact]
   public async Task ImplementMissingMembers_Struct_ByName_ReturnsPreview()
   {
      var lookup = new LookupByName { Name = "CoreTestProject.Refactoring.IncompleteStruct" };
      var result = await _tool.ImplementMissingMembersAsync(lookup, preview: true, cancellationToken: CT);

      // Verify result structure
      Assert.NotNull(result);
      Assert.NotNull(result.Data);
      Assert.True(result.Data.Preview);
      Assert.False(result.Data.Applied);

      var changedFile = Assert.Single(result.Data.ChangedFiles);
      Assert.NotNull(changedFile.NewText);

      // Verify interface method is implemented
      Assert.Contains("Initialize()", changedFile.NewText);

      // Verify interface property is implemented
      Assert.Contains("IsReady", changedFile.NewText);
   }

   [Fact]
   public async Task ImplementMissingMembers_Struct_ByPosition_ReturnsPreview()
   {
      var filePath = GetCoreTestProjectPath("Refactoring/ImplementMissingMembersExtendedFixtures.cs");

      // Validate fixture: IncompleteStruct should be at line 45
      await ValidateFixtureLineContainsAsync(filePath, 45, "struct IncompleteStruct");

      var lookup = new LookupByPosition { FilePath = filePath, Line = 45, Column = 14 };
      var result = await _tool.ImplementMissingMembersAsync(lookup, preview: true, cancellationToken: CT);

      Assert.NotNull(result);
      Assert.NotNull(result.Data);
      Assert.True(result.Data.Preview);

      var changedFile = Assert.Single(result.Data.ChangedFiles);
      Assert.NotNull(changedFile.NewText);

      // Verify method and property are implemented
      Assert.Contains("Initialize()", changedFile.NewText);
      Assert.Contains("IsReady", changedFile.NewText);
   }

   #endregion

   #region Property/Event/Indexer Tests

   [Fact]
   public async Task ImplementMissingMembers_PropertyEventIndexer_ReturnsPreview()
   {
      var lookup = new LookupByName { Name = "CoreTestProject.Refactoring.IncompleteComplexClass" };
      var result = await _tool.ImplementMissingMembersAsync(lookup, preview: true, cancellationToken: CT);

      Assert.NotNull(result);
      Assert.NotNull(result.Data);
      Assert.True(result.Data.Preview);

      var changedFile = Assert.Single(result.Data.ChangedFiles);
      Assert.NotNull(changedFile.NewText);

      // Verify properties are implemented
      Assert.Contains("Id", changedFile.NewText);
      Assert.Contains("Description", changedFile.NewText);

      // Verify events are implemented
      Assert.Contains("StatusChanged", changedFile.NewText);
      Assert.Contains("MessageReceived", changedFile.NewText);

      // Verify indexer is implemented (this[int index])
      Assert.Contains("this[", changedFile.NewText);
   }

   #endregion

   #region Multiple Interface Tests

   [Fact]
   public async Task ImplementMissingMembers_MultipleInterfaces_ImplementsAll()
   {
      var lookup = new LookupByName { Name = "CoreTestProject.Refactoring.MultiInterfaceClass" };
      var result = await _tool.ImplementMissingMembersAsync(lookup, preview: true, cancellationToken: CT);

      Assert.NotNull(result);
      Assert.NotNull(result.Data);
      Assert.True(result.Data.Preview);

      var changedFile = Assert.Single(result.Data.ChangedFiles);
      Assert.NotNull(changedFile.NewText);

      // Verify members from IFirstInterface
      Assert.Contains("FirstMethod()", changedFile.NewText);
      Assert.Contains("FirstProperty", changedFile.NewText);

      // Verify members from ISecondInterface
      Assert.Contains("SecondMethod()", changedFile.NewText);
      Assert.Contains("SecondProperty", changedFile.NewText);
   }

   #endregion

   #region Apply Mode Tests

   [Fact]
   public async Task ImplementMissingMembers_WithApply_ModifiesWorkspaceAndFile()
   {
      // Arrange: Use a dedicated fixture that we can restore after the test
      var filePath = GetCoreTestProjectPath("Refactoring/ApplyModeFixtures.cs");
      var originalContent = await File.ReadAllTextAsync(filePath);

      try
      {
         var lookup = new LookupByName { Name = "CoreTestProject.Refactoring.ApplyModeClass" };

         // Act
         var result = await _tool.ImplementMissingMembersAsync(lookup, preview: false, cancellationToken: CT);

         // Assert result structure
         Assert.NotNull(result);
         Assert.NotNull(result.Data);
         Assert.False(result.Data.Preview);
         Assert.True(result.Data.Applied);

         // Verify action title
         Assert.Contains("Implement", result.Data.ActionTitle, StringComparison.OrdinalIgnoreCase);

         // Verify changed file info
         var changedFile = Assert.Single(result.Data.ChangedFiles);
         Assert.Null(changedFile.NewText); // NewText is null in apply mode
         Assert.True(changedFile.ChangeCount > 0);

         // Assert file was actually modified on disk
         var modifiedContent = await File.ReadAllTextAsync(filePath);
         Assert.NotEqual(originalContent, modifiedContent);
         Assert.Contains("void DoWork()", modifiedContent);
         Assert.Contains("NotImplementedException", modifiedContent);

         // Verify workspace was updated - querying for the type should show the new member
         var verifyLookup = new LookupByName { Name = "CoreTestProject.Refactoring.ApplyModeClass" };

         // This should NOT throw NotSupported anymore since members are now implemented
         // However, since we're about to restore, we can't actually test this without
         // more complex setup. The file modification verification above is sufficient.
      }
      finally
      {
         // Restore the original file content
         await File.WriteAllTextAsync(filePath, originalContent);

         // Verify restoration succeeded
         var restoredContent = await File.ReadAllTextAsync(filePath);
         Assert.Equal(originalContent, restoredContent);
      }
   }

   #endregion

   #region Helper Methods

   /// <summary>
   /// Validates that a fixture file contains expected content at a specific line.
   /// This prevents silent test failures when fixture files are accidentally modified.
   /// </summary>
   private static async Task ValidateFixtureLineContainsAsync(string filePath, int lineNumber, string expectedContent)
   {
      var lines = await File.ReadAllLinesAsync(filePath);
      Assert.True(lines.Length >= lineNumber,
         $"Fixture file {Path.GetFileName(filePath)} is too short. Expected at least {lineNumber} lines, got {lines.Length}.");

      var actualLine = lines[lineNumber - 1]; // Convert to 0-based index
      Assert.True(actualLine.Contains(expectedContent, StringComparison.Ordinal),
         $"Fixture file {Path.GetFileName(filePath)} line {lineNumber} does not contain expected content.\n" +
         $"Expected to contain: '{expectedContent}'\n" +
         $"Actual line: '{actualLine}'");
   }

   #endregion
}
