using DotLens.Infrastructure;
using DotLens.Tests.Helpers;
using DotLens.Tools.Editing;
using Xunit;

namespace DotLens.Tests.Editing;

public class EditFileToolTests : IsolatedProjectTestBase
{
   private EditFileTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      _tool = new EditFileTool(WorkspaceManager);
   }

   [Fact]
   public async Task SingleEdit_Succeeds()
   {
      var filePath = GetCoreTestProjectPath("Editing/EditTarget.cs");
      var result = await _tool.EditFileAsync(
         filePath,
         [new EditOperation { Find = "public int Add(int a, int b)", Replace = "public int Sum(int a, int b)" }],
         CT);

      var data = GetData(result);
      Assert.Equal(1, data.GetInt("editsApplied"));
      Assert.True(data.GetBool("applied"));
      Assert.Equal(0, data.GetInt("errorCount"));
   }

   [Fact]
   public async Task MultipleEdits_Succeeds()
   {
      var filePath = GetCoreTestProjectPath("Editing/EditTarget.cs");
      var result = await _tool.EditFileAsync(
         filePath,
         [
            new EditOperation { Find = "public int Add(int a, int b)", Replace = "public int Sum(int a, int b)" },
            new EditOperation { Find = "public string Greet(string name)", Replace = "public string SayHello(string name)" }
         ],
         CT);

      var data = GetData(result);
      Assert.Equal(2, data.GetInt("editsApplied"));
      Assert.True(data.GetBool("applied"));
      Assert.Equal(0, data.GetInt("errorCount"));
   }

   [Fact]
   public async Task EditIntroducesError_ReturnsDiagnostics()
   {
      var filePath = GetCoreTestProjectPath("Editing/EditTarget.cs");
      // Change return type to string but keep returning int expression
      var result = await _tool.EditFileAsync(
         filePath,
         [new EditOperation { Find = "public int Add(int a, int b)", Replace = "public string Add(int a, int b)" }],
         CT);

      var data = GetData(result);
      Assert.True(data.GetBool("applied"));
      Assert.Equal(1, data.GetInt("errorCount"));
      var diagnostics = data.GetArray("diagnostics");
      var diagnostic = Assert.Single(diagnostics);
      Assert.Equal("CS0029", diagnostic.GetString("id"));
   }

   [Fact]
   public async Task EditValidCode_ZeroDiagnostics()
   {
      var filePath = GetCoreTestProjectPath("Editing/EditTarget.cs");
      // Rename method - still valid code
      var result = await _tool.EditFileAsync(
         filePath,
         [new EditOperation { Find = "public int Add(int a, int b)", Replace = "public int Sum(int a, int b)" }],
         CT);

      var data = GetData(result);
      Assert.True(data.GetBool("applied"));
      Assert.Equal(0, data.GetInt("errorCount"));
   }

   [Fact]
   public async Task FindTextNotFound_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Editing/EditTarget.cs");
      await AssertThrowsToolExceptionAsync(
         () => _tool.EditFileAsync(
            filePath,
            [new EditOperation { Find = "nonexistent text", Replace = "something" }],
            CT),
         ErrorCodes.InvalidParameter);
   }

   [Fact]
   public async Task FindTextAppearsTwice_ThrowsWithLineNumbers()
   {
      var filePath = GetCoreTestProjectPath("Editing/EditTarget.cs");
      // "public" appears multiple times
      var ex = await AssertThrowsToolExceptionAsync(
         () => _tool.EditFileAsync(
            filePath,
            [new EditOperation { Find = "public", Replace = "private" }],
            CT),
         ErrorCodes.InvalidParameter);

      Assert.Equal("Edit [0]: find text appears multiple times (lines 3 and 5)", ex.Message);
   }

   [Fact]
   public async Task OverlappingEdits_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Editing/EditTarget.cs");
      // Read file to get actual line endings (avoids hardcoding \r\n)
      var fileContent = File.ReadAllText(filePath);
      var methodStart = fileContent.IndexOf("public int Add(int a, int b)");
      var methodEnd = fileContent.IndexOf("}", methodStart + 1) + 1;
      var methodBlock = fileContent[methodStart..methodEnd];

      // These overlap because the second find string is contained in the first's region
      await AssertThrowsToolExceptionAsync(
         () => _tool.EditFileAsync(
            filePath,
            [
               new EditOperation { Find = methodBlock, Replace = "public int Sum(int x, int y) => x + y;" },
               new EditOperation { Find = "return a + b;", Replace = "return a - b;" }
            ],
            CT),
         ErrorCodes.InvalidParameter);
   }

   [Fact]
   public async Task EmptyFindString_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Editing/EditTarget.cs");
      await AssertThrowsToolExceptionAsync(
         () => _tool.EditFileAsync(
            filePath,
            [new EditOperation { Find = "", Replace = "something" }],
            CT),
         ErrorCodes.InvalidParameter);
   }

   [Fact]
   public async Task EmptyEditsArray_ThrowsToolException()
   {
      var filePath = GetCoreTestProjectPath("Editing/EditTarget.cs");
      await AssertThrowsToolExceptionAsync(
         () => _tool.EditFileAsync(filePath, [], CT),
         ErrorCodes.InvalidParameter);
   }

   [Fact]
   public async Task FileNotInSolution_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.EditFileAsync(
            "nonexistent/file.cs",
            [new EditOperation { Find = "a", Replace = "b" }],
            CT),
         ErrorCodes.FileNotInSolution);
   }

   [Fact]
   public async Task VerifyFileOnDiskUpdated()
   {
      var filePath = GetCoreTestProjectPath("Editing/EditTarget.cs");
      var originalContent = File.ReadAllText(filePath);

      await _tool.EditFileAsync(
         filePath,
         [new EditOperation { Find = "public int Add(int a, int b)", Replace = "public int Sum(int a, int b)" }],
         CT);

      var diskContent = File.ReadAllText(filePath);
      var expectedContent = originalContent.Replace("public int Add(int a, int b)", "public int Sum(int a, int b)");
      Assert.Equal(expectedContent, diskContent);
   }

   [Fact]
   public async Task SubsequentRoslynQueriesSeeChanges()
   {
      var filePath = GetCoreTestProjectPath("Editing/EditTarget.cs");

      await _tool.EditFileAsync(
         filePath,
         [new EditOperation { Find = "public int Add(int a, int b)", Replace = "public int Sum(int a, int b)" }],
         CT);

      var document = await WorkspaceManager.GetDocumentAsync(filePath, CT);
      var text = await document.GetTextAsync(CT);
      Assert.Contains("Sum", text.ToString());
      Assert.DoesNotContain("Add(int a, int b)", text.ToString());
   }
}
