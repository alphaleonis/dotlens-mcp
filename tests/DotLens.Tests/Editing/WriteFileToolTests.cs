using DotLens.Infrastructure;
using DotLens.Tests.Helpers;
using DotLens.Tools.Editing;
using Xunit;

namespace DotLens.Tests.Editing;

public class WriteFileToolTests : IsolatedProjectTestBase
{
   private WriteFileTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      _tool = new WriteFileTool(WorkspaceManager);
   }

   [Fact]
   public async Task WriteValidContent_Succeeds()
   {
      var filePath = GetCoreTestProjectPath("Editing/EditTarget.cs");
      var newContent = """
         namespace CoreTestProject.Editing;

         public class EditTarget
         {
            public int Multiply(int a, int b)
            {
               return a * b;
            }
         }
         """;

      var result = await _tool.WriteFileAsync(filePath, newContent, CT);

      var data = GetData(result);
      Assert.True(data.GetBool("applied"));
      Assert.False(data.GetBool("isNewFile"));
      Assert.Equal(0, data.GetInt("errorCount"));
   }

   [Fact]
   public async Task WriteContentWithErrors_ReturnsDiagnostics()
   {
      var filePath = GetCoreTestProjectPath("Editing/EditTarget.cs");
      var badContent = """
         namespace CoreTestProject.Editing;

         public class EditTarget
         {
            public string Add(int a, int b)
            {
               return a + b;
            }
         }
         """;

      var result = await _tool.WriteFileAsync(filePath, badContent, CT);

      var data = GetData(result);
      Assert.True(data.GetBool("applied"));
      Assert.False(data.GetBool("isNewFile"));
      Assert.Equal(1, data.GetInt("errorCount"));
      var diagnostics = data.GetArray("diagnostics");
      var diagnostic = Assert.Single(diagnostics);
      Assert.Equal("CS0029", diagnostic.GetString("id"));
   }

   [Fact]
   public async Task EmptyContent_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Editing/EditTarget.cs");
      await AssertThrowsToolExceptionAsync(
         () => _tool.WriteFileAsync(filePath, "", CT),
         ErrorCodes.InvalidParameter);
   }

   [Fact]
   public async Task WhitespaceContent_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Editing/EditTarget.cs");
      await AssertThrowsToolExceptionAsync(
         () => _tool.WriteFileAsync(filePath, "   \n  ", CT),
         ErrorCodes.InvalidParameter);
   }

   [Fact]
   public async Task FileNotInAnyProject_ThrowsToolException()
   {
      // Path is within solution directory but not under any project directory
      var filePath = Path.Combine(TempProjectDir, "orphan.cs");
      await AssertThrowsToolExceptionAsync(
         () => _tool.WriteFileAsync(filePath, "class Orphan {}", CT),
         ErrorCodes.FileNotInAnyProject);
      Assert.False(File.Exists(filePath));
   }

   [Fact]
   public async Task VerifyFileOnDiskUpdated()
   {
      var filePath = GetCoreTestProjectPath("Editing/EditTarget.cs");
      var newContent = """
         namespace CoreTestProject.Editing;

         public class EditTarget
         {
            public int Subtract(int a, int b)
            {
               return a - b;
            }
         }
         """;

      await _tool.WriteFileAsync(filePath, newContent, CT);

      var diskContent = File.ReadAllText(filePath);
      Assert.Equal(newContent, diskContent);
   }

   [Fact]
   public async Task SubsequentRoslynQueriesSeeChanges()
   {
      var filePath = GetCoreTestProjectPath("Editing/EditTarget.cs");
      var newContent = """
         namespace CoreTestProject.Editing;

         public class EditTarget
         {
            public int Divide(int a, int b)
            {
               return a / b;
            }
         }
         """;

      await _tool.WriteFileAsync(filePath, newContent, CT);

      // Verify the workspace sees the new content
      var document = await WorkspaceManager.GetDocumentAsync(filePath, CT);
      var text = await document.GetTextAsync(CT);
      Assert.Equal(newContent, text.ToString());
   }

   // =============================
   // New file creation tests
   // =============================

   [Fact]
   public async Task CreateNewFile_Succeeds()
   {
      var filePath = GetCoreTestProjectPath("NewClass.cs");
      var content = """
         namespace CoreTestProject;

         public class NewClass
         {
            public int Value { get; set; }
         }
         """;

      var result = await _tool.WriteFileAsync(filePath, content, CT);

      var data = GetData(result);
      Assert.True(data.GetBool("applied"));
      Assert.True(data.GetBool("isNewFile"));
      Assert.Equal(0, data.GetInt("errorCount"));

      // Verify file exists on disk
      Assert.True(File.Exists(filePath));
      Assert.Equal(content, File.ReadAllText(filePath));
   }

   [Fact]
   public async Task CreateNewFile_ReturnsDiagnosticsForInvalidCode()
   {
      var filePath = GetCoreTestProjectPath("BadNewClass.cs");
      var content = """
         namespace CoreTestProject;

         public class BadNewClass
         {
            public int GetValue()
            {
               return "not an int";
            }
         }
         """;

      var result = await _tool.WriteFileAsync(filePath, content, CT);

      var data = GetData(result);
      Assert.True(data.GetBool("applied"));
      Assert.True(data.GetBool("isNewFile"));
      Assert.Equal(1, data.GetInt("errorCount"));
      var diagnostics = data.GetArray("diagnostics");
      var diagnostic = Assert.Single(diagnostics);
      Assert.Equal("CS0029", diagnostic.GetString("id"));
   }

   [Fact]
   public async Task CreateNewFile_SubsequentQueriesSeeChanges()
   {
      var filePath = GetCoreTestProjectPath("DiscoverableClass.cs");
      var content = """
         namespace CoreTestProject;

         public class DiscoverableClass
         {
            public void DoSomething() { }
         }
         """;

      var result = await _tool.WriteFileAsync(filePath, content, CT);
      var data = GetData(result);
      Assert.True(data.GetBool("applied"));
      Assert.True(data.GetBool("isNewFile"));

      // Verify GetDocumentAsync finds the new file
      var document = await WorkspaceManager.GetDocumentAsync(filePath, CT);
      Assert.NotNull(document);
      var text = await document.GetTextAsync(CT);
      Assert.Equal(content, text.ToString());
   }

   [Fact]
   public async Task CreateNewFile_CreatesParentDirectories()
   {
      var filePath = GetCoreTestProjectPath("Deep/Nested/Directory/NewFile.cs");
      var content = """
         namespace CoreTestProject.Deep.Nested.Directory;

         public class NewFile
         {
            public string Name => "test";
         }
         """;

      var result = await _tool.WriteFileAsync(filePath, content, CT);

      var data = GetData(result);
      Assert.True(data.GetBool("applied"));
      Assert.True(data.GetBool("isNewFile"));
      Assert.Equal(0, data.GetInt("errorCount"));
      Assert.True(File.Exists(filePath));
      Assert.Equal(content, File.ReadAllText(filePath));
   }

   [Fact]
   public async Task CreateNewFile_NotUnderAnyProject_Throws()
   {
      // Create a directory within solution dir but not under any project
      var outsideDir = Path.Combine(TempProjectDir, "NotAProject");
      Directory.CreateDirectory(outsideDir);
      var filePath = Path.Combine(outsideDir, "Orphan.cs");

      await AssertThrowsToolExceptionAsync(
         () => _tool.WriteFileAsync(filePath, "class Orphan {}", CT),
         ErrorCodes.FileNotInAnyProject);
      Assert.False(File.Exists(filePath));
   }

   [Fact]
   public async Task UpdateExistingFile_IsNewFileFalse()
   {
      var filePath = GetCoreTestProjectPath("Editing/EditTarget.cs");
      var content = """
         namespace CoreTestProject.Editing;

         public class EditTarget
         {
            public int Square(int x) => x * x;
         }
         """;

      var result = await _tool.WriteFileAsync(filePath, content, CT);

      var data = GetData(result);
      Assert.True(data.GetBool("applied"));
      Assert.False(data.GetBool("isNewFile"));
      Assert.Equal(0, data.GetInt("errorCount"));
   }
}
