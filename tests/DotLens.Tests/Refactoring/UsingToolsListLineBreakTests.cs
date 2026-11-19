using DotLens.Infrastructure;
using DotLens.Tools.Editing;
using DotLens.Tools.Formatting;
using DotLens.Tools.Refactoring;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace DotLens.Tests.Refactoring;

/// <summary>
/// Covers that organize_usings and add_using write the line breaks of a using list as Roslyn's Organize Imports does,
/// in files whose usings end their lines with a line break other than the rest of the file's.
/// </summary>
/// <remarks>
/// In the cases, ~ stands for the line break of the using list and \n for the line break of the rest of the file. Each
/// test runs its case with each combination of \n and \r\n for the two, in one file that it writes again for each.
/// </remarks>
public class UsingToolsListLineBreakTests : IsolatedProjectTestBase
{
   private const string TargetPath = "ListLineBreak/ListTarget.cs";

   private static readonly (string File, string List)[] LineBreaks =
   [
      ("\n", "\n"),
      ("\r\n", "\r\n"),
      ("\r\n", "\n"),
      ("\n", "\r\n"),
   ];

   /// <summary>
   /// A source and the text that the tool writes from it.
   /// </summary>
   /// <param name="SeparateGroups">The value of dotnet_separate_import_directive_groups.</param>
   private sealed record Case(bool SeparateGroups, string Source, string Expected);

   private static readonly Dictionary<string, Case> OrganizeCases = new()
   {
      ["TriviaBelowExternAlias_Comment"] = new(
         false,
         FileSource("extern alias Ex;~// note~using Zeta;~using Alpha;\n\n"),
         FileSource("extern alias Ex;~using Alpha;\n// note~using Zeta;~\n")),
      ["TriviaBelowExternAlias_WhitespaceLine"] = new(
         false,
         FileSource("extern alias Ex;~   ~using Zeta;~using System.Text;~using Alpha;\n\n"),
         FileSource("extern alias Ex;~using System.Text;~using Alpha;\n   ~using Zeta;~\n")),
      ["DirectiveAboveFirstUsing"] = new(
         false,
         FileSource("#pragma warning disable CS0168~~using Zeta;~~using Alpha;\n\n"),
         FileSource("#pragma warning disable CS0168~~using Alpha;\nusing Zeta;~\n")),
      ["WhitespaceLineInNamespace"] = new(
         false,
         NamespaceSource("~~   using Zeta;~~   using Alpha;~   ~   using System.Text;\n\n"),
         NamespaceSource("~~   ~   using System.Text;\n   using Alpha;~   using Zeta;~\n")),
      ["GlobalThenLocal"] = new(
         false,
         FileSource("using Alpha.Inner;~global using Alpha;\n\n"),
         FileSource("global using Alpha;\nusing Alpha.Inner;~\n")),
      ["BlankLineGroups"] = new(
         false,
         FileSource("using Zeta;~~using System.Text;~using Alpha;\n\n"),
         FileSource("using System.Text;~using Alpha;\nusing Zeta;~\n")),
      ["HeaderAboveUsings"] = new(
         false,
         FileSource("// header\nusing Zeta;~using System.Text;\n\n"),
         FileSource("// header\nusing System.Text;\nusing Zeta;~\n")),
      ["TriviaBelowExternAlias_Comment_Separated"] = new(
         true,
         FileSource("extern alias Ex;~// note~using Zeta;~using Alpha;\n\n"),
         FileSource("extern alias Ex;~~using Alpha;\n// note~using Zeta;~\n")),
      ["TriviaBelowExternAlias_WhitespaceLine_Separated"] = new(
         true,
         FileSource("extern alias Ex;~   ~using Zeta;~using System.Text;~using Alpha;\n\n"),
         FileSource("extern alias Ex;~~using System.Text;~~using Alpha;\n   ~using Zeta;~\n")),
      ["DirectiveAboveFirstUsing_Separated"] = new(
         true,
         FileSource("#pragma warning disable CS0168~~using Zeta;~~using Alpha;\n\n"),
         FileSource("#pragma warning disable CS0168~~using Alpha;\n~using Zeta;~\n")),
      ["WhitespaceLineInNamespace_Separated"] = new(
         true,
         NamespaceSource("~~   using Zeta;~~   using Alpha;~   ~   using System.Text;\n\n"),
         NamespaceSource("~~   ~   using System.Text;\n~   using Alpha;~~   using Zeta;~\n")),
      ["GlobalThenLocal_Separated"] = new(
         true,
         FileSource("using Alpha.Inner;~global using Alpha;\n\n"),
         FileSource("global using Alpha;\nusing Alpha.Inner;~\n")),
      ["BlankLineGroups_Separated"] = new(
         true,
         FileSource("using Zeta;~~using System.Text;~using Alpha;\n\n"),
         FileSource("using System.Text;~~using Alpha;\n~using Zeta;~\n")),
      ["HeaderAboveUsings_Separated"] = new(
         true,
         FileSource("// header\nusing Zeta;~using System.Text;\n\n"),
         FileSource("// header\nusing System.Text;\n~using Zeta;~\n")),
      ["WhitespaceLineAtFileLevel_BlankLineFirst"] = new(
         false,
         FileSource("using Zeta;~~   ~using Alpha;\n\n"),
         FileSource("   ~using Alpha;\nusing Zeta;~\n")),
      ["WhitespaceLineAtFileLevel_WhitespaceLineFirst"] = new(
         false,
         FileSource("using Zeta;~   ~~using Alpha;\n\n"),
         FileSource("   ~~using Alpha;\nusing Zeta;~\n")),
      ["ExternAliasAboveUsings_Separated"] = new(
         true,
         FileSource("extern alias Ex;\nusing Zeta;~using Alpha;\n\n"),
         FileSource("extern alias Ex;\n\nusing Alpha;\n\nusing Zeta;~\n")),
      ["NamespaceList_Separated"] = new(
         true,
         NamespaceSource("\n   using Zeta;~   using System.Text;\n\n"),
         NamespaceSource("\n   using System.Text;\n~   using Zeta;~\n")),
   };

   private static readonly Dictionary<string, Case> AddCases = new()
   {
      ["TriviaBelowExternAlias"] = new(
         false,
         FileSource("extern alias Ex;~// note~using Zeta;~using Alpha;\n\n"),
         FileSource("extern alias Ex;~using Alpha;\nusing Beta;~// note~using Zeta;~\n")),
      ["HeaderAboveUsings"] = new(
         false,
         FileSource("// header\nusing Zeta;~using System.Text;\n\n"),
         FileSource("// header\nusing System.Text;\nusing Beta;~using Zeta;~\n")),
      ["TriviaBelowExternAlias_Separated"] = new(
         true,
         FileSource("extern alias Ex;~// note~using Zeta;~using Alpha;\n\n"),
         FileSource("extern alias Ex;~~using Alpha;\n~using Beta;~// note~using Zeta;~\n")),
      ["HeaderAboveUsings_Separated"] = new(
         true,
         FileSource("// header\nusing Zeta;~using System.Text;\n\n"),
         FileSource("// header\nusing System.Text;\n~using Beta;~~using Zeta;~\n")),
      ["ExternAliasAboveUsings_Separated"] = new(
         true,
         FileSource("extern alias Ex;\nusing Alpha;~using Zeta;\n\n"),
         FileSource("extern alias Ex;\n\nusing Alpha;~\nusing Beta;\n\nusing Zeta;\n\n")),
      ["NamespaceList_Separated"] = new(
         true,
         NamespaceSource("\n   using Alpha;~   using Zeta;\n\n"),
         NamespaceSource("\n   using Alpha;~~   using Beta;~~   using Zeta;\n\n")),
      ["OnlyExternAlias_Separated"] = new(
         true,
         FileSource("extern alias Zulu;~"),
         FileSource("extern alias Zulu;~~using Beta;~~")),
   };

   public static TheoryData<string> OrganizeCaseNames => [.. OrganizeCases.Keys];

   public static TheoryData<string> AddCaseNames => [.. AddCases.Keys];

   public static TheoryData<int> LineBreakIndexes => [.. Enumerable.Range(0, LineBreaks.Length)];

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      await WriteSourceAsync("ListLineBreak/ListLineBreakNamespaces.cs", """
         namespace Alpha { public class AlphaType { } }

         namespace Alpha.Inner { public class InnerType { } }

         namespace Beta { public class BetaType { } }

         namespace Zeta { public class ZetaType { } }

         """);
   }

   [Theory]
   [MemberData(nameof(OrganizeCaseNames))]
   public async Task UsingTools_ListLineBreak_OrganizeUsings_WritesWhatRoslynWrites(string name)
   {
      var testCase = OrganizeCases[name];
      await UseEditorConfigAsync(testCase.SeparateGroups);

      for (var i = 0; i < LineBreaks.Length; i++)
      {
         var (fileBreak, listBreak) = LineBreaks[i];
         var source = Expand(testCase.Source, fileBreak, listBreak);
         var path = await WriteSourceAsync(TargetPath, source, isNewFile: i == 0);
         var document = await WorkspaceManager.GetDocumentAsync(path, CT);
         var roslynOrganized = await Formatter.OrganizeImportsAsync(document, CT);

         var result = await new OrganizeUsingsTool(WorkspaceManager).OrganizeUsingsAsync(
            new FileScope { Path = path }, preview: false, cancellationToken: CT);

         Assert.True(result.Data.Applied);
         Assert.Equal(1, result.Data.FilesChanged);
         Assert.Equal(0, result.Data.TotalUsingsRemoved);
         var written = await File.ReadAllTextAsync(path, CT);
         Assert.Equal(Expand(testCase.Expected, fileBreak, listBreak), written);
         Assert.Equal((await roslynOrganized.GetTextAsync(CT)).ToString(), written);
      }
   }

   [Theory]
   [MemberData(nameof(AddCaseNames))]
   public async Task UsingTools_ListLineBreak_AddUsing_RoslynKeepsOutput(string name)
   {
      var testCase = AddCases[name];
      await UseEditorConfigAsync(testCase.SeparateGroups);

      for (var i = 0; i < LineBreaks.Length; i++)
      {
         var (fileBreak, listBreak) = LineBreaks[i];
         var source = Expand(testCase.Source, fileBreak, listBreak);
         var path = await WriteSourceAsync(TargetPath, source, isNewFile: i == 0);

         var result = await new AddUsingTool(WorkspaceManager).AddUsingAsync(path, "Beta", preview: false, CT);

         Assert.True(result.Data.Added);
         Assert.True(result.Data.Applied);
         var written = await File.ReadAllTextAsync(path, CT);
         Assert.Equal(Expand(testCase.Expected, fileBreak, listBreak), written);
         var document = await WorkspaceManager.GetDocumentAsync(path, CT);
         var roslynOrganized = await Formatter.OrganizeImportsAsync(document, CT);
         Assert.Equal(written, (await roslynOrganized.GetTextAsync(CT)).ToString());
      }
   }

   /// <summary>
   /// The using that ends the first line of the list goes, so the line breaks that the tool adds are the ones of the
   /// usings that remain, as when Roslyn organizes them.
   /// </summary>
   [Theory]
   [MemberData(nameof(LineBreakIndexes))]
   public async Task UsingTools_ListLineBreak_OrganizeUsings_RemovesFirstUsing_AddsLineBreakOfRemainingUsings(int index)
   {
      var (fileBreak, listBreak) = LineBreaks[index];
      await UseEditorConfigAsync(separateGroups: true);
      var path = await WriteSourceAsync(
         TargetPath,
         Expand(FileSource("using System.Linq;\nusing Zeta;~using System.Text;~\n"), fileBreak, listBreak),
         isNewFile: true);

      var result = await new OrganizeUsingsTool(WorkspaceManager).OrganizeUsingsAsync(
         new FileScope { Path = path }, preview: false, cancellationToken: CT);

      Assert.True(result.Data.Applied);
      Assert.Equal(1, result.Data.FilesChanged);
      Assert.Equal(1, result.Data.TotalUsingsRemoved);
      Assert.Equal(
         Expand(FileSource("using System.Text;~~using Zeta;~\n"), fileBreak, listBreak),
         await File.ReadAllTextAsync(path, CT));
   }

   private static string Expand(string template, string fileBreak, string listBreak) =>
      string.Concat(template.Select(c => c switch
      {
         '\n' => fileBreak,
         '~' => listBreak,
         _ => c.ToString(),
      }));

   private static string FileSource(string usings) => usings + """
      namespace CoreTestProject.ListLineBreak;

      public class ListTarget
      {
         public StringBuilder? Builder;
         public AlphaType? AlphaPlain;
         public InnerType? Inner;
         public ZetaType? ZetaPlain;
      }

      """.ReplaceLineEndings("\n");

   private static string NamespaceSource(string usings) => "namespace CoreTestProject.ListLineBreak\n{" + usings + """
         public class ListTarget
         {
            public StringBuilder? Builder;
            public AlphaType? AlphaPlain;
            public InnerType? Inner;
            public ZetaType? ZetaPlain;
         }
      }

      """.ReplaceLineEndings("\n");

   /// <summary>
   /// Writes the .editorconfig and adds it to CoreTestProject, as a load of the solution would.
   /// </summary>
   private async Task UseEditorConfigAsync(bool separateGroups)
   {
      var path = GetCoreTestProjectPath("ListLineBreak/.editorconfig");
      var text = $"""
         root = true

         [*.cs]
         dotnet_sort_system_directives_first = true
         dotnet_separate_import_directive_groups = {(separateGroups ? "true" : "false")}

         """;
      await File.WriteAllTextAsync(path, text, CT);
      using var writeLock = await WorkspaceManager.WriteLock.AcquireAsync(CT);
      var project = Assert.Single(WorkspaceManager.EnsureSolutionLoaded().Projects, p => p.Name == "CoreTestProject");
      WorkspaceManager.UpdateSolution(
         project.AddAnalyzerConfigDocument(".editorconfig", SourceText.From(text), filePath: path).Project.Solution);
   }

   /// <summary>
   /// Writes the source and checks that it parses without diagnostics, so a diagnostic in the result comes from the tool.
   /// </summary>
   private async Task<string> WriteSourceAsync(string relativePath, string text, bool isNewFile = true)
   {
      Assert.Empty(CSharpSyntaxTree.ParseText(text, cancellationToken: CT).GetDiagnostics());
      var path = GetCoreTestProjectPath(relativePath);
      var written = await new WriteFileTool(WorkspaceManager).WriteFileAsync(path, text, CT);
      Assert.Equal(isNewFile, written.Data.IsNewFile);
      return path;
   }
}
