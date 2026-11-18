using System.Globalization;
using DotLens.Infrastructure;
using DotLens.Tools.Editing;
using DotLens.Tools.Formatting;
using DotLens.Tools.Refactoring;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace DotLens.Tests.Refactoring;

/// <summary>
/// Covers that add_using and organize_usings sort usings as Roslyn's Organize Imports does under any current culture,
/// that add_using refuses a malformed namespace and reads a name whose first identifier is an extern alias as a using of
/// the compilation unit reads it, and that both keep a Unicode line break.
/// </summary>
public class UsingToolsLeftoversTests : IsolatedProjectTestBase
{
   private const string TargetPath = "Leftovers/UsingTarget.cs";

   public static TheoryData<string> Cultures => ["tr-TR", "da-DK", ""];

   /// <summary>
   /// The culture-sensitive comparer puts Ib before ia under tr-TR and Aalborg last under da-DK. The invariant culture
   /// sets the order to keep.
   /// </summary>
   [Theory]
   [MemberData(nameof(Cultures))]
   public async Task UsingTools_Leftovers_OrganizeUsings_AnyCurrentCulture_SortsTheSame(string culture)
   {
      var path = await WriteSourceAsync(TargetPath, CultureSortSource("""
         using Zeta;
         using Ib;
         using ia;
         using Aalborg;
         """));

      var result = await InCultureAsync(culture, () => new OrganizeUsingsTool(WorkspaceManager).OrganizeUsingsAsync(
         new FileScope { Path = path }, preview: false, cancellationToken: CT));

      Assert.True(result.Data.Applied);
      Assert.Equal(1, result.Data.FilesChanged);
      Assert.Equal(0, result.Data.TotalUsingsRemoved);
      Assert.Equal(CultureSortSource("""
         using Aalborg;
         using ia;
         using Ib;
         using Zeta;
         """), await File.ReadAllTextAsync(path, CT));
   }

   [Theory]
   [MemberData(nameof(Cultures))]
   public async Task UsingTools_Leftovers_AddUsing_AnyCurrentCulture_SortsTheSame(string culture)
   {
      var path = await WriteSourceAsync(TargetPath, CultureSortSource("""
         using Zeta;
         using Ib;
         using Aalborg;
         """));

      var result = await InCultureAsync(culture, () =>
         new AddUsingTool(WorkspaceManager).AddUsingAsync(path, "ia", preview: false, CT));

      Assert.True(result.Data.Added);
      Assert.True(result.Data.Applied);
      Assert.Equal(CultureSortSource("""
         using Aalborg;
         using ia;
         using Ib;
         using Zeta;
         """), await File.ReadAllTextAsync(path, CT));
   }

   /// <summary>
   /// The names cover case, digits, an underscore, accents, a fullwidth letter, the System group, a name that only
   /// starts with System, and whitespace inside a name. Names equal but for accents and width keep their input order.
   /// </summary>
   [Fact]
   public async Task UsingTools_Leftovers_OrganizeUsings_MixedNames_OrdersAsRoslynOrganizeImports()
   {
      await WriteSourceAsync("Leftovers/SortNamespaces.cs", """
         namespace System.Data.SqlClient { public class SqlClientType { } }
         namespace System.Data.SQLite { public class SqliteType { } }
         namespace System.IdentityModel { public class IdentityType { } }
         namespace SystemX.Foo { public class SystemXType { } }
         namespace ia { public class LowerType { } }
         namespace Ia { public class TitleType { } }
         namespace Ib { public class UpperType { } }
         namespace A_B { public class UnderscoreType { } }
         namespace A1 { public class DigitType { } }
         namespace Aa { public class MixedType { } }
         namespace AB { public class CapitalType { } }
         namespace NUnit.Framework { public class NUnitType { } }
         namespace Newtonsoft.Json { public class NewtonsoftType { } }
         namespace resume { public class ResumeLowerType { } }
         namespace résumé { public class ResumeAccentLowerType { } }
         namespace Résumé { public class ResumeAccentTitleType { } }
         namespace Resume { public class ResumeTitleType { } }
         namespace Wide { public class WideAsciiType { } }
         namespace Ｗide { public class WideFullType { } }

         """);
      var path = await WriteSourceAsync(TargetPath, RoslynOrderSource("""
         using NUnit.Framework;
         using AB;
         using System . IO . Pipes;
         using Ib;
         using SystemX.Foo;
         using A_B;
         using System.Data.SQLite;
         using Ia;
         using Newtonsoft.Json;
         using ia;
         using System.IdentityModel;
         using Aa;
         using System.Data.SqlClient;
         using Resume;
         using résumé;
         using Ｗide;
         using Résumé;
         using Wide;
         using resume;
         using A1;
         """));
      var document = await WorkspaceManager.GetDocumentAsync(path, CT);
      var roslynOrder = await GetUsingNamesAsync(await Formatter.OrganizeImportsAsync(document, CT));

      var result = await new OrganizeUsingsTool(WorkspaceManager).OrganizeUsingsAsync(
         new FileScope { Path = path }, preview: false, cancellationToken: CT);

      Assert.Equal(1, result.Data.FilesChanged);
      Assert.Equal(0, result.Data.TotalUsingsRemoved);
      var written = await File.ReadAllTextAsync(path, CT);
      Assert.Equal(RoslynOrderSource("""
         using System.Data.SqlClient;
         using System.Data.SQLite;
         using System.IdentityModel;
         using System . IO . Pipes;
         using A_B;
         using A1;
         using Aa;
         using AB;
         using ia;
         using Ia;
         using Ib;
         using Newtonsoft.Json;
         using NUnit.Framework;
         using résumé;
         using resume;
         using Resume;
         using Résumé;
         using SystemX.Foo;
         using Ｗide;
         using Wide;
         """), written);
      Assert.Equal(roslynOrder, await GetUsingNamesAsync(document.WithText(SourceText.From(written))));
   }

   /// <summary>
   /// add_using writes global::-qualified names inside a namespace declaration whose names hide the file-level ones.
   /// </summary>
   [Fact]
   public async Task UsingTools_Leftovers_OrganizeUsings_GlobalQualifiedSystemName_SortsInSystemGroup()
   {
      await AssertOrganizeUsingsAsync(0, """
         using Alpha;
         using global::System.Text;
         using System.Collections;

         namespace CoreTestProject.Leftovers
         {
            public class GlobalQualifiedTarget
            {
               public StringBuilder? Builder;
               public ArrayList? List;
               public AlphaType? Alpha;
            }
         }

         namespace Alpha
         {
            public class AlphaType
            {
            }
         }

         """, """
         using System.Collections;
         using global::System.Text;
         using Alpha;

         namespace CoreTestProject.Leftovers
         {
            public class GlobalQualifiedTarget
            {
               public StringBuilder? Builder;
               public ArrayList? List;
               public AlphaType? Alpha;
            }
         }

         namespace Alpha
         {
            public class AlphaType
            {
            }
         }

         """);
   }

   /// <summary>
   /// The compiler reads U+FEFF and U+001A as whitespace, so the name parses, but neither belongs in the written using.
   /// </summary>
   [Theory]
   [InlineData("System.\uFEFFXml")]
   [InlineData("System.Xml\u001A")]
   public async Task UsingTools_Leftovers_AddUsing_CompilerWhitespaceInName_WritesNameWithout(string @namespace)
   {
      await AssertAddUsingAsync(@namespace, """
         using System.Text;

         namespace CoreTestProject.Leftovers;

         public class CompilerWhitespaceTarget
         {
         }

         """, """
         using System.Text;
         using System.Xml;

         namespace CoreTestProject.Leftovers;

         public class CompilerWhitespaceTarget
         {
         }

         """);
   }

   private static string CultureSortSource(string usings) => $$"""
      {{usings}}

      namespace CoreTestProject.Leftovers
      {
         public class CultureSortTarget
         {
            public AalborgType? First;
            public LowerType? Second;
            public UpperType? Third;
            public ZetaType? Fourth;
         }
      }

      namespace Aalborg { public class AalborgType { } }
      namespace ia { public class LowerType { } }
      namespace Ib { public class UpperType { } }
      namespace Zeta { public class ZetaType { } }

      """;

   private static string RoslynOrderSource(string usings) => $$"""
      {{usings}}

      namespace CoreTestProject.Leftovers;

      public class RoslynOrderTarget
      {
         public SqlClientType? SqlClient;
         public SqliteType? Sqlite;
         public IdentityType? Identity;
         public PipeStream? Pipe;
         public SystemXType? SystemX;
         public LowerType? Lower;
         public TitleType? Title;
         public UpperType? Upper;
         public UnderscoreType? Underscore;
         public DigitType? Digit;
         public MixedType? Mixed;
         public CapitalType? Capital;
         public NUnitType? NUnit;
         public NewtonsoftType? Newtonsoft;
         public ResumeLowerType? ResumeLower;
         public ResumeAccentLowerType? ResumeAccentLower;
         public ResumeAccentTitleType? ResumeAccentTitle;
         public ResumeTitleType? ResumeTitle;
         public WideAsciiType? WideAscii;
         public WideFullType? WideFull;
      }

      """;

   private static async Task<List<string>> GetUsingNamesAsync(Document document)
   {
      var root = await document.GetSyntaxRootAsync(TestContext.Current.CancellationToken) as CompilationUnitSyntax
         ?? throw new InvalidOperationException("No compilation unit");
      return [.. root.Usings.Select(u => string.Concat(u.NamespaceOrType.ToString().Where(c => !char.IsWhiteSpace(c))))];
   }

   [Theory]
   [InlineData("System.Linq;")]
   [InlineData("System..Linq")]
   [InlineData("System.")]
   [InlineData("Sys tem.Text")]
   [InlineData("System/*note*/.Text")]
   [InlineData("System.Text // note")]
   [InlineData("System.Collections.Generic.List<int>")]
   [InlineData("int")]
   [InlineData("System.class")]
   [InlineData("Foo::Bar::Baz")]
   [InlineData("global::global::System")]
   public async Task UsingTools_Leftovers_AddUsing_MalformedNamespace_ThrowsInvalidParameter(string @namespace)
   {
      var source = """
         using System.Text;

         namespace CoreTestProject.Leftovers;

         public class MalformedTarget
         {
         }

         """;
      var path = await WriteSourceAsync(TargetPath, source);

      var exception = await AssertThrowsToolExceptionAsync(
         () => new AddUsingTool(WorkspaceManager).AddUsingAsync(path, @namespace, preview: false, CT),
         ErrorCodes.InvalidParameter);

      Assert.Equal($"'{@namespace}' is not a valid namespace name", exception.Message);
      Assert.Equal(source, await File.ReadAllTextAsync(path, CT));
   }

   /// <summary>
   /// The global namespace has no Foo, so Foo in a using of the compilation unit names the extern alias.
   /// </summary>
   [Fact]
   public async Task UsingTools_Leftovers_AddUsing_ExternAliasNamedLikeFirstSegment_BindsToAliasedNamespace()
   {
      await AddAliasedReferenceAsync("namespace Bar { public class InAlias { } }");

      var path = await AssertAddUsingAsync("Foo.Bar", """
         extern alias Foo;

         namespace CoreTestProject.Leftovers
         {
            using System.Text;

            public class ExternAliasTarget
            {
            }
         }

         """, """
         extern alias Foo;

         namespace CoreTestProject.Leftovers
         {
            using System.Text;
            using Foo.Bar;

            public class ExternAliasTarget
            {
            }
         }

         """);

      await AssertBindsToAliasedBarAsync(path, "Foo.Bar");
   }

   /// <summary>
   /// The nested namespace hides the extern alias inside the namespace declaration.
   /// </summary>
   [Fact]
   public async Task UsingTools_Leftovers_AddUsing_ExternAliasHiddenInNamespace_WritesAliasQualifiedName()
   {
      await AddAliasedReferenceAsync("namespace Bar { public class InAlias { } }");

      var path = await AssertAddUsingAsync("Foo.Bar", """
         extern alias Foo;

         namespace CoreTestProject.Leftovers
         {
            using System.Text;

            namespace Foo
            {
               public class Hider
               {
               }
            }

            public class HiddenAliasTarget
            {
            }
         }

         """, """
         extern alias Foo;

         namespace CoreTestProject.Leftovers
         {
            using System.Text;
            using Foo::Bar;

            namespace Foo
            {
               public class Hider
               {
               }
            }

            public class HiddenAliasTarget
            {
            }
         }

         """);

      await AssertBindsToAliasedBarAsync(path, "Foo::Bar");
   }

   /// <summary>
   /// No name can reach the extern alias alone past the nested namespace that hides it.
   /// </summary>
   [Fact]
   public async Task UsingTools_Leftovers_AddUsing_HiddenExternAliasAlone_ThrowsNotSupported()
   {
      await AddAliasedReferenceAsync("namespace Bar { public class InAlias { } }");
      var source = """
         extern alias Foo;

         namespace CoreTestProject.Leftovers
         {
            using System.Text;

            namespace Foo
            {
               public class Hider
               {
               }
            }

            public class HiddenAliasAloneTarget
            {
            }
         }

         """;
      var path = await WriteSourceAsync(TargetPath, source);

      var exception = await AssertThrowsToolExceptionAsync(
         () => new AddUsingTool(WorkspaceManager).AddUsingAsync(path, "Foo", preview: false, CT),
         ErrorCodes.NotSupported);

      Assert.Equal("Cannot add the using: a name in the namespace declaration hides the extern alias 'Foo'", exception.Message);
      Assert.Equal(source, await File.ReadAllTextAsync(path, CT));
   }

   [Fact]
   public async Task UsingTools_Leftovers_AddUsing_AliasQualifiedName_AddsUsing()
   {
      await AddAliasedReferenceAsync("namespace Bar { public class InAlias { } }");

      var path = await AssertAddUsingAsync("Foo::Bar", """
         extern alias Foo;

         namespace CoreTestProject.Leftovers
         {
            using System.Text;

            public class AliasQualifiedTarget
            {
            }
         }

         """, """
         extern alias Foo;

         namespace CoreTestProject.Leftovers
         {
            using System.Text;
            using Foo::Bar;

            public class AliasQualifiedTarget
            {
            }
         }

         """);

      await AssertBindsToAliasedBarAsync(path, "Foo::Bar");
   }

   [Fact]
   public async Task UsingTools_Leftovers_AddUsing_ExternAliasUsingPresent_ReportsPresent()
   {
      await AddAliasedReferenceAsync("namespace Bar { public class InAlias { } }");
      var source = """
         extern alias Foo;

         namespace CoreTestProject.Leftovers
         {
            using Foo.Bar;

            public class AliasPresentTarget
            {
            }
         }

         """;
      var path = await WriteSourceAsync(TargetPath, source);

      var result = await new AddUsingTool(WorkspaceManager).AddUsingAsync(path, "Foo.Bar", preview: false, CT);

      Assert.False(result.Data.Added);
      Assert.True(result.Data.AlreadyExists);
      Assert.Equal("Using directive 'Foo.Bar' already exists", result.Data.Message);
      Assert.Equal(source, await File.ReadAllTextAsync(path, CT));
   }

   [Fact]
   public async Task UsingTools_Leftovers_AddUsing_TypeThroughExternAlias_ThrowsInvalidParameter()
   {
      await AddAliasedReferenceAsync("namespace Bar { public class Widget { } }");
      var source = """
         extern alias Foo;

         namespace CoreTestProject.Leftovers
         {
            using System.Text;

            public class AliasTypeTarget
            {
            }
         }

         """;
      var path = await WriteSourceAsync(TargetPath, source);

      var exception = await AssertThrowsToolExceptionAsync(
         () => new AddUsingTool(WorkspaceManager).AddUsingAsync(path, "Foo.Bar.Widget", preview: false, CT),
         ErrorCodes.InvalidParameter);

      Assert.Equal("'Foo.Bar.Widget' is a type, not a namespace", exception.Message);
      Assert.Equal(source, await File.ReadAllTextAsync(path, CT));
   }

   public static TheoryData<string> UnicodeNewLines => ["\u2028", "\u0085", "\u2029"];

   [Theory]
   [MemberData(nameof(UnicodeNewLines))]
   public async Task UsingTools_Leftovers_AddUsing_UnicodeLineBreak_KeepsLineBreak(string newLine)
   {
      await AssertAddUsingAsync("System.Xml", """
         using System.Text;

         namespace CoreTestProject.Leftovers;

         public class UnicodeAddTarget
         {
         }

         """.ReplaceLineEndings(newLine), """
         using System.Text;
         using System.Xml;

         namespace CoreTestProject.Leftovers;

         public class UnicodeAddTarget
         {
         }

         """.ReplaceLineEndings(newLine));
   }

   [Theory]
   [MemberData(nameof(UnicodeNewLines))]
   public async Task UsingTools_Leftovers_AddUsing_UnicodeLineBreakNoUsings_KeepsLineBreak(string newLine)
   {
      await AssertAddUsingAsync("System.Text", """
         // File header

         namespace CoreTestProject.Leftovers;

         public class UnicodeEmptyTarget
         {
         }

         """.ReplaceLineEndings(newLine), """
         // File header

         using System.Text;

         namespace CoreTestProject.Leftovers;

         public class UnicodeEmptyTarget
         {
         }

         """.ReplaceLineEndings(newLine));
   }

   /// <summary>
   /// The using of System.Text shares its line with the next using, so removing that one ends the line, and the usings
   /// change order, so each one gets a new follower.
   /// </summary>
   [Theory]
   [MemberData(nameof(UnicodeNewLines))]
   public async Task UsingTools_Leftovers_OrganizeUsings_UnicodeLineBreak_KeepsLineBreak(string newLine)
   {
      await AssertOrganizeUsingsAsync(1, """
         #region Usings
         using System.Text; using System.Globalization;
         using CoreTestProject.Types;
         using System.Collections.Concurrent;
         #endregion

         namespace CoreTestProject.Leftovers;

         public class UnicodeOrganizeTarget
         {
            public SampleClass? Sample;
            public StringBuilder? Builder;
            public ConcurrentBag<int>? Bag;
         }

         """.ReplaceLineEndings(newLine), """
         #region Usings
         using System.Collections.Concurrent;
         using System.Text;
         using CoreTestProject.Types;
         #endregion

         namespace CoreTestProject.Leftovers;

         public class UnicodeOrganizeTarget
         {
            public SampleClass? Sample;
            public StringBuilder? Builder;
            public ConcurrentBag<int>? Bag;
         }

         """.ReplaceLineEndings(newLine));
   }

   /// <summary>
   /// Only another file uses the global using, so it is used only when the whole compilation counts.
   /// </summary>
   [Fact]
   public async Task UsingTools_Leftovers_OrganizeUsings_GlobalUsingUsedByOtherFile_KeepsGlobalUsing()
   {
      await WriteSourceAsync("Leftovers/NumericsUser.cs", """
         namespace CoreTestProject.Leftovers;

         public class NumericsUser
         {
            public BigInteger Value;
         }

         """);

      await AssertOrganizeUsingsAsync(1, """
         global using System.Numerics;
         using System.Globalization;

         namespace CoreTestProject.Leftovers;

         public class GlobalUsingTarget
         {
         }

         """, """
         global using System.Numerics;

         namespace CoreTestProject.Leftovers;

         public class GlobalUsingTarget
         {
         }

         """);
   }

   private static async Task<T> InCultureAsync<T>(string culture, Func<Task<T>> action)
   {
      var originalCulture = CultureInfo.CurrentCulture;
      var originalUICulture = CultureInfo.CurrentUICulture;
      try
      {
         CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
         CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
         return await action();
      }
      finally
      {
         CultureInfo.CurrentCulture = originalCulture;
         CultureInfo.CurrentUICulture = originalUICulture;
      }
   }

   /// <summary>
   /// Adds to CoreTestProject a reference with the extern alias Foo to an assembly compiled from the source.
   /// </summary>
   private async Task AddAliasedReferenceAsync(string source)
   {
      using (await WorkspaceManager.WriteLock.AcquireAsync(CT))
      {
         var project = WorkspaceManager.EnsureSolutionLoaded().Projects.Single(p => p.Name == "CoreTestProject");
         var library = CSharpCompilation.Create(
            "AliasedLibrary",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: CT)],
            project.MetadataReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
         using var image = new MemoryStream();
         var emitted = library.Emit(image, cancellationToken: CT);
         Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));

         var reference = MetadataReference.CreateFromImage(
            image.ToArray(), new MetadataReferenceProperties(aliases: ["Foo"]));
         WorkspaceManager.UpdateSolution(project.AddMetadataReference(reference).Solution);
      }
   }

   private async Task AssertBindsToAliasedBarAsync(string path, string usingName)
   {
      var document = await WorkspaceManager.GetDocumentAsync(path, CT);
      var root = await document.GetSyntaxRootAsync(CT) ?? throw new InvalidOperationException("No syntax root");
      var model = await document.GetSemanticModelAsync(CT) ?? throw new InvalidOperationException("No semantic model");
      var added = root.DescendantNodes().OfType<UsingDirectiveSyntax>().Single(u => u.Name?.ToString() == usingName);

      var imported = Assert.IsAssignableFrom<INamespaceSymbol>(model.GetSymbolInfo(added.NamespaceOrType, CT).Symbol);
      Assert.Equal("Bar", imported.ToDisplayString());
      Assert.Equal("AliasedLibrary", imported.ContainingAssembly.Name);
      Assert.Empty(model.GetDiagnostics(cancellationToken: CT).Where(d => d.Severity == DiagnosticSeverity.Error));
   }

   private async Task<string> AssertAddUsingAsync(string @namespace, string source, string expected)
   {
      var path = await WriteSourceAsync(TargetPath, source);

      var result = await new AddUsingTool(WorkspaceManager).AddUsingAsync(path, @namespace, preview: false, CT);

      Assert.True(result.Data.Added);
      Assert.True(result.Data.Applied);
      var written = await File.ReadAllTextAsync(path, CT);
      Assert.Equal(expected, written);
      Assert.Empty(ParseDiagnostics(written));
      return path;
   }

   private async Task AssertOrganizeUsingsAsync(int expectedRemoved, string source, string expected)
   {
      var path = await WriteSourceAsync(TargetPath, source);

      var result = await new OrganizeUsingsTool(WorkspaceManager).OrganizeUsingsAsync(
         new FileScope { Path = path }, preview: false, cancellationToken: CT);

      Assert.True(result.Data.Applied);
      Assert.Equal(1, result.Data.FilesChanged);
      Assert.Equal(expectedRemoved, result.Data.TotalUsingsRemoved);
      var written = await File.ReadAllTextAsync(path, CT);
      Assert.Equal(expected, written);
      Assert.Empty(ParseDiagnostics(written));
   }

   private static List<string> ParseDiagnostics(string text) =>
      [.. CSharpSyntaxTree.ParseText(text, cancellationToken: TestContext.Current.CancellationToken)
         .GetDiagnostics()
         .Select(d => d.ToString())];

   /// <summary>
   /// Writes the source and checks that it parses without diagnostics, so a diagnostic in the result comes from the tool.
   /// </summary>
   private async Task<string> WriteSourceAsync(string relativePath, string text)
   {
      Assert.Empty(ParseDiagnostics(text));
      var path = GetCoreTestProjectPath(relativePath);
      var written = await new WriteFileTool(WorkspaceManager).WriteFileAsync(path, text, CT);
      Assert.True(written.Data.IsNewFile);
      return path;
   }
}
