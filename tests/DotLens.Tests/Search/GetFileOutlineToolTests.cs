using DotLens.Infrastructure;
using DotLens.Tools.Search;
using Xunit;

namespace DotLens.Tests.Search;

[Collection("TestFixtures")]
public class GetFileOutlineToolTests : TestFixtureTestBase
{
   public GetFileOutlineToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private GetFileOutlineTool _tool = null!;

   public override async ValueTask InitializeAsync()
   {
      _tool = new GetFileOutlineTool(Workspace);
   }

   [Fact]
   public async Task GetFileOutline_ReturnsOutlineWithLineNumbers()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var result = await _tool.GetFileOutlineAsync(filePath, ct: CT);

      AssertSuccess(result);
      var outline = result.Data.Outline;

      // Should contain line numbers
      Assert.Matches(@"^\s*\d+:", outline);

      // Should contain class declarations
      Assert.Contains("class", outline);

      // Should contain member signatures
      Assert.Contains("(", outline); // method parameters
   }

   [Fact]
   public async Task GetFileOutline_ShowsHierarchy()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var result = await _tool.GetFileOutlineAsync(filePath, ct: CT);

      AssertSuccess(result);
      var outline = result.Data.Outline;

      // Should have opening and closing braces showing structure
      Assert.Contains("{", outline);
      Assert.Contains("}", outline);

      // Should show indentation (members indented within types)
      var lines = outline.Split('\n');
      var hasIndentedContent = lines.Any(l => l.Contains(":") && l.Contains("   ")); // 3-space indent
      Assert.True(hasIndentedContent, "Should have indented members within types");
   }

   [Fact]
   public async Task GetFileOutline_ExcludesPrivateWhenRequested()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");

      var withPrivate = await _tool.GetFileOutlineAsync(filePath, includePrivate: true, ct: CT);
      var withoutPrivate = await _tool.GetFileOutlineAsync(filePath, includePrivate: false, ct: CT);

      // Outline without private should be shorter or equal
      Assert.True(withoutPrivate.Data.Outline.Length <= withPrivate.Data.Outline.Length);
   }

   [Fact]
   public async Task GetFileOutline_FileNotFound_ThrowsToolException()
   {
      await AssertThrowsToolExceptionAsync(
         () => _tool.GetFileOutlineAsync("nonexistent.cs", ct: CT),
         ErrorCodes.FileNotInSolution);
   }

   [Fact]
   public async Task GetFileOutline_InterfaceFile_ShowsInterfaceDeclaration()
   {
      var filePath = GetCoreTestProjectPath("Types/Interfaces.cs");
      var result = await _tool.GetFileOutlineAsync(filePath, ct: CT);

      AssertSuccess(result);
      var outline = result.Data.Outline;

      // Should contain interface keyword
      Assert.Contains("interface", outline);
      Assert.Contains("IBasicService", outline);
   }

   [Fact]
   public async Task GetFileOutline_ShowsMethodSignatures()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var result = await _tool.GetFileOutlineAsync(filePath, ct: CT);

      AssertSuccess(result);
      var outline = result.Data.Outline;

      // Methods should end with semicolon (no body)
      // Look for pattern: method signature ending with );
      Assert.Matches(@"\w+\([^)]*\);", outline);
   }

   [Fact]
   public async Task GetFileOutline_ShowsProperties()
   {
      var filePath = GetCoreTestProjectPath("Types/Classes.cs");
      var result = await _tool.GetFileOutlineAsync(filePath, ct: CT);

      AssertSuccess(result);
      var outline = result.Data.Outline;

      // Properties should show accessors like { get; set; }
      Assert.Contains("get;", outline);
   }

   [Fact]
   public async Task GetFileOutline_ShowsNestedTypes()
   {
      // UnusedCode.cs contains nested classes
      var filePath = GetCoreTestProjectPath("Patterns/UnusedCode.cs");
      var result = await _tool.GetFileOutlineAsync(filePath, ct: CT);

      AssertSuccess(result);
      var outline = result.Data.Outline;

      // Should show nested class structure with proper indentation
      // The outline should have multiple levels of indentation
      var lines = outline.Split('\n');
      var indentLevels = lines
         .Where(l => l.Contains(":"))
         .Select(l =>
         {
            var afterColon = l.IndexOf(':') + 1;
            if (afterColon >= l.Length)
               return 0;
            var spaces = l.Skip(afterColon + 1).TakeWhile(c => c == ' ').Count();
            return spaces / 3; // 3-space indent
         })
         .Distinct()
         .Count();

      Assert.True(indentLevels >= 2, "Should have multiple indent levels for nested types");
   }

   [Fact]
   public async Task GetFileOutline_IncludeDocComments_ShowsXmlDoc()
   {
      // Use a file that has XML doc comments
      var filePath = GetCoreTestProjectPath("Types/Interfaces.cs");
      var result = await _tool.GetFileOutlineAsync(filePath, includeDocComments: true, ct: CT);

      AssertSuccess(result);
      var outline = result.Data.Outline;

      // Should contain XML doc comment content
      Assert.Contains("<summary>", outline);
   }

   [Fact]
   public async Task GetFileOutline_WithoutDocComments_DoesNotShowXmlDoc()
   {
      var filePath = GetCoreTestProjectPath("Types/Interfaces.cs");
      var result = await _tool.GetFileOutlineAsync(filePath, includeDocComments: false, ct: CT);

      AssertSuccess(result);
      var outline = result.Data.Outline;

      // Should NOT contain XML doc comment content
      Assert.DoesNotContain("<summary>", outline);
   }
}
