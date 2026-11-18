using DotLens.Infrastructure;
using DotLens.Tools.Editing;
using DotLens.Tools.Formatting;
using DotLens.Tools.Refactoring;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace DotLens.Tests.Refactoring;

/// <summary>
/// Covers that add_using and organize_usings group and sort usings as Roslyn's Organize Imports does: global usings
/// first, then namespace, static and alias usings in turn, with aliases sorted by alias name and generic names by their
/// type arguments. Covers too that organize_usings sorts extern aliases.
/// </summary>
public class UsingToolsGroupingTests : IsolatedProjectTestBase
{
   private const string TargetPath = "Grouping/UsingTarget.cs";

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      await WriteSourceAsync("Grouping/GroupingNamespaces.cs", """
         namespace Alpha
         {
            public class AlphaType { }
            public static class AlphaStatics { public static int AlphaValue => 1; }
         }

         namespace Beta { public class BetaType { } }

         namespace Zeta
         {
            public class ZetaType { }
            public static class ZetaStatics { public static int ZetaValue => 2; }
         }

         namespace Nest
         {
            public class Outer<T>
            {
               public static class First { public static int FirstValue => 1; }
               public static class Second { public static int SecondValue => 2; }
               public static class Third { public static int ThirdValue => 3; }
               public static class Fourth { public static int FourthValue => 4; }
               public static class Fifth { public static int FifthValue => 5; }
               public static class Seventh { public static int SeventhValue => 7; }
               public static class Eighth { public static int EighthValue => 8; }
            }

            public class Outer<T1, T2>
            {
               public static class Sixth { public static int SixthValue => 6; }
            }
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Grouping_OrganizeUsings_MixedKinds_OrdersAsRoslynOrganizeImports()
   {
      await AssertOrganizeUsingsAsync(MixedKindsSource("""
         using static Zeta.ZetaStatics;
         using GroupingBuilder = System.Text.StringBuilder;
         using Zeta;
         using alpha = Zeta.ZetaType;
         using static System.Math;
         using Alpha;
         using System.Text;
         using AlphaAlias = Alpha.AlphaType;
         using static Alpha.AlphaStatics;
         """), MixedKindsSource("""
         using System.Text;
         using Alpha;
         using Zeta;
         using static System.Math;
         using static Alpha.AlphaStatics;
         using static Zeta.ZetaStatics;
         using alpha = Zeta.ZetaType;
         using AlphaAlias = Alpha.AlphaType;
         using GroupingBuilder = System.Text.StringBuilder;
         """));
   }

   /// <summary>
   /// The global:: name sorts in the same place under its name and under the word global, so Roslyn's order applies.
   /// </summary>
   [Fact]
   public async Task UsingTools_Grouping_OrganizeUsings_MixedKindsWithGlobal_OrdersAsRoslynOrganizeImports()
   {
      await AssertOrganizeUsingsAsync(MixedKindsWithGlobalSource("""
         using Zeta;
         global using static Zeta.ZetaStatics;
         using static Alpha.AlphaStatics;
         global using GroupingBuilder = System.Text.StringBuilder;
         global using Alpha;
         using GroupingZeta = Zeta.ZetaType;
         global using System.Text;
         using static System.Math;
         global using global::Beta;
         """), MixedKindsWithGlobalSource("""
         global using System.Text;
         global using Alpha;
         global using global::Beta;
         global using static Zeta.ZetaStatics;
         global using GroupingBuilder = System.Text.StringBuilder;
         using Zeta;
         using static System.Math;
         using static Alpha.AlphaStatics;
         using GroupingZeta = Zeta.ZetaType;
         """));
   }

   [Fact]
   public async Task UsingTools_Grouping_AddUsing_MixedKinds_OrdersAsRoslynOrganizeImports()
   {
      await AssertAddUsingAsync("Zeta", """
         using static System.Math;
         using GroupingBuilder = System.Text.StringBuilder;
         using Alpha;

         namespace CoreTestProject.Grouping;

         public class AddTarget
         {
         }

         """, """
         using Alpha;
         using Zeta;
         using static System.Math;
         using GroupingBuilder = System.Text.StringBuilder;

         namespace CoreTestProject.Grouping;

         public class AddTarget
         {
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Grouping_AddUsing_MixedKindsWithGlobal_OrdersAsRoslynOrganizeImports()
   {
      await AssertAddUsingAsync("System.Text", """
         global using static Zeta.ZetaStatics;
         global using Alpha;
         using GroupingBuilder = System.Text.StringBuilder;
         using static System.Math;
         global using GroupingZeta = Zeta.ZetaType;

         namespace CoreTestProject.Grouping;

         public class AddGlobalTarget
         {
         }

         """, """
         global using Alpha;
         global using static Zeta.ZetaStatics;
         global using GroupingZeta = Zeta.ZetaType;
         using System.Text;
         using static System.Math;
         using GroupingBuilder = System.Text.StringBuilder;

         namespace CoreTestProject.Grouping;

         public class AddGlobalTarget
         {
         }

         """);
   }

   /// <summary>
   /// Two aliases with one name do not compile, but a file in progress can hold them.
   /// </summary>
   [Fact]
   public async Task UsingTools_Grouping_AddUsing_SameAliasName_OrdersByTargetAsRoslynOrganizeImports()
   {
      await AssertAddUsingAsync("Beta", """
         using Same = System.Text.StringBuilder;
         using Same = Alpha.AlphaType;

         namespace CoreTestProject.Grouping;

         public class SameAliasTarget
         {
         }

         """, """
         using Beta;
         using Same = Alpha.AlphaType;
         using Same = System.Text.StringBuilder;

         namespace CoreTestProject.Grouping;

         public class SameAliasTarget
         {
         }

         """);
   }

   /// <summary>
   /// Type arguments decide between instances of one generic type: a non-name type by its first token, and ? and [] count
   /// for nothing.
   /// </summary>
   [Fact]
   public async Task UsingTools_Grouping_OrganizeUsings_StaticGenericTypes_OrdersAsRoslynOrganizeImports()
   {
      await AssertOrganizeUsingsAsync(GenericTypesSource("""
         using static Nest.Outer<string>.Second;
         using static Nest.Outer<int>.First;
         using static Nest.Outer<Alpha.AlphaType>.Third;
         using static Nest.Outer<int?[]>.Fourth;
         using static Nest.Outer<(int, int)>.Fifth;
         using static Nest.Outer<int, int>.Sixth;
         using static Nest.Outer<System.Int32>.Seventh;
         using static Nest.Outer<global::Alpha.AlphaType>.Eighth;
         """), GenericTypesSource("""
         using static Nest.Outer<(int, int)>.Fifth;
         using static Nest.Outer<Alpha.AlphaType>.Third;
         using static Nest.Outer<global::Alpha.AlphaType>.Eighth;
         using static Nest.Outer<int>.First;
         using static Nest.Outer<int?[]>.Fourth;
         using static Nest.Outer<string>.Second;
         using static Nest.Outer<System.Int32>.Seventh;
         using static Nest.Outer<int, int>.Sixth;
         """));
   }

   /// <summary>
   /// Roslyn orders a type before or after its generic namesakes depending on the input order, so only the pairs it orders
   /// the same either way are compared.
   /// </summary>
   [Fact]
   public async Task UsingTools_Grouping_AddUsing_StaticGenericAndPlainType_OrdersPairsAsRoslynOrganizeImports()
   {
      await AssertAddUsingAsync("Alpha", """
         using static Nest.G<Zeta>;
         using static Nest.G;
         using static Nest.G<int[]>;
         using static Nest.G<Alpha>;
         using static Nest.G.Inner;
         using static Nest.G<delegate*<int>>;

         namespace CoreTestProject.Grouping;

         public class PlainAndGenericTarget
         {
         }

         """, """
         using Alpha;
         using static Nest.G;
         using static Nest.G.Inner;
         using static Nest.G<Alpha>;
         using static Nest.G<delegate*<int>>;
         using static Nest.G<int[]>;
         using static Nest.G<Zeta>;

         namespace CoreTestProject.Grouping;

         public class PlainAndGenericTarget
         {
         }

         """, roslynKeepsOutput: false);
   }

   /// <summary>
   /// Roslyn compares the targets of two aliases with one name only when both are names.
   /// </summary>
   [Fact]
   public async Task UsingTools_Grouping_AddUsing_SameAliasNameTypeTarget_KeepsOrderAsRoslynOrganizeImports()
   {
      await AssertAddUsingAsync("Beta", """
         using Same = int;
         using Same = Alpha.AlphaType;

         namespace CoreTestProject.Grouping;

         public class SameAliasTypeTarget
         {
         }

         """, """
         using Beta;
         using Same = int;
         using Same = Alpha.AlphaType;

         namespace CoreTestProject.Grouping;

         public class SameAliasTypeTarget
         {
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Grouping_OrganizeUsings_ExternAliases_OrdersAsRoslynOrganizeImports()
   {
      await AssertOrganizeUsingsAsync("""
         // Header comment

         extern alias Zulu;
         extern alias system;
         extern alias Bravo;
         extern alias System;
         extern alias alpha;
         extern alias Alpha;
         using System.Text;

         namespace CoreTestProject.Grouping
         {
            extern alias Yankee;
            extern alias Xray;

            public class ExternTarget
            {
               public Encoding? Text;
            }
         }

         """, """
         // Header comment

         extern alias alpha;
         extern alias Alpha;
         extern alias Bravo;
         extern alias system;
         extern alias System;
         extern alias Zulu;
         using System.Text;

         namespace CoreTestProject.Grouping
         {
            extern alias Xray;
            extern alias Yankee;

            public class ExternTarget
            {
               public Encoding? Text;
            }
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Grouping_OrganizeUsings_ExternAliasesWithoutUsings_OrdersAsRoslynOrganizeImports()
   {
      await AssertOrganizeUsingsAsync("""
         extern alias Zulu;
         extern alias Alpha;

         namespace CoreTestProject.Grouping;

         public class ExternOnlyTarget
         {
         }

         """, """
         extern alias Alpha;
         extern alias Zulu;

         namespace CoreTestProject.Grouping;

         public class ExternOnlyTarget
         {
         }

         """);
   }

   /// <summary>
   /// Roslyn leaves a list with a directive among its directives unsorted, so only the order the tool writes is compared.
   /// </summary>
   [Fact]
   public async Task UsingTools_Grouping_OrganizeUsings_ExternAliasesAcrossDirective_SortsWithinEachRun()
   {
      await AssertOrganizeUsingsAsync("""
         // Header comment

         extern alias Zulu;
         extern alias alpha;
         #if DEBUG
         extern alias Mike;
         extern alias Kilo;
         #endif
         extern alias Bravo;
         extern alias Alpha;

         using System.Text;

         namespace CoreTestProject.Grouping;

         public class ExternDirectiveTarget
         {
            public Encoding? Text;
         }

         """, """
         // Header comment

         extern alias alpha;
         extern alias Zulu;
         #if DEBUG
         extern alias Kilo;
         extern alias Mike;
         #endif
         extern alias Alpha;
         extern alias Bravo;

         using System.Text;

         namespace CoreTestProject.Grouping;

         public class ExternDirectiveTarget
         {
            public Encoding? Text;
         }

         """, roslynSortsInput: false);
   }

   private static string GenericTypesSource(string usings) => $$"""
      {{usings}}

      namespace CoreTestProject.Grouping;

      public class GenericTypesTarget
      {
         public int Values => FirstValue + SecondValue + ThirdValue + FourthValue + FifthValue + SixthValue + SeventhValue
            + EighthValue;
      }

      """;

   private static string MixedKindsSource(string usings) => $$"""
      {{usings}}

      namespace CoreTestProject.Grouping;

      public class MixedKindsTarget
      {
         public Encoding? Text;
         public AlphaType? Alpha;
         public ZetaType? Zeta;
         public double Math => Abs(-1);
         public int AlphaStatic => AlphaValue;
         public int ZetaStatic => ZetaValue;
         public alpha? LowerAlias;
         public AlphaAlias? UpperAlias;
         public GroupingBuilder? Builder;
      }

      """;

   private static string MixedKindsWithGlobalSource(string usings) => $$"""
      {{usings}}

      namespace CoreTestProject.Grouping;

      public class MixedKindsWithGlobalTarget
      {
         public Encoding? Text;
         public AlphaType? Alpha;
         public BetaType? Beta;
         public ZetaType? Zeta;
         public double Math => Abs(-1);
         public int AlphaStatic => AlphaValue;
         public int ZetaStatic => ZetaValue;
         public GroupingZeta? ZetaAlias;
         public GroupingBuilder? Builder;
      }

      """;

   /// <param name="roslynSortsInput">Whether Roslyn's Organize Imports writes the expected text from the source.</param>
   private async Task AssertOrganizeUsingsAsync(string source, string expected, bool roslynSortsInput = true)
   {
      var path = await WriteSourceAsync(TargetPath, source);
      var document = await WorkspaceManager.GetDocumentAsync(path, CT);
      var roslynOrganized = await Formatter.OrganizeImportsAsync(document, CT);

      var result = await new OrganizeUsingsTool(WorkspaceManager).OrganizeUsingsAsync(
         new FileScope { Path = path }, preview: false, cancellationToken: CT);

      Assert.True(result.Data.Applied);
      Assert.Equal(1, result.Data.FilesChanged);
      Assert.Equal(0, result.Data.TotalUsingsRemoved);
      var written = await File.ReadAllTextAsync(path, CT);
      Assert.Equal(expected, written);
      if (roslynSortsInput)
         Assert.Equal((await roslynOrganized.GetTextAsync(CT)).ToString(), written);
      else
         Assert.Equal(source, (await roslynOrganized.GetTextAsync(CT)).ToString());

      await AssertRoslynKeepsAsync(path, written);
   }

   /// <param name="roslynKeepsOutput">
   /// Whether Roslyn's Organize Imports leaves the expected text as it is. When it does not, each pair of usings that
   /// Roslyn orders the same in either input order must keep that order.
   /// </param>
   private async Task AssertAddUsingAsync(
      string @namespace,
      string source,
      string expected,
      bool roslynKeepsOutput = true)
   {
      var path = await WriteSourceAsync(TargetPath, source);

      var result = await new AddUsingTool(WorkspaceManager).AddUsingAsync(path, @namespace, preview: false, CT);

      Assert.True(result.Data.Added);
      Assert.True(result.Data.Applied);
      var written = await File.ReadAllTextAsync(path, CT);
      Assert.Equal(expected, written);
      if (roslynKeepsOutput)
         await AssertRoslynKeepsAsync(path, written);
      else
         await AssertRoslynOrdersPairsAlikeAsync(path, written);
   }

   private async Task AssertRoslynOrdersPairsAlikeAsync(string path, string written)
   {
      var document = await WorkspaceManager.GetDocumentAsync(path, CT);
      var usings = written.Split('\n').Where(l => l.StartsWith("using ", StringComparison.Ordinal)).ToList();
      Assert.NotEmpty(usings);
      var comparedPairs = 0;
      for (var i = 0; i < usings.Count; i++)
      {
         for (var j = i + 1; j < usings.Count; j++)
         {
            var keepsOrder = await RoslynPutsFirstAsync(document, usings[i], usings[j]) == usings[i];
            var swapsOrder = await RoslynPutsFirstAsync(document, usings[j], usings[i]) == usings[i];
            if (keepsOrder == swapsOrder)
            {
               Assert.True(keepsOrder, $"Roslyn puts {usings[j]} before {usings[i]}");
               comparedPairs++;
            }
         }
      }

      Assert.NotEqual(0, comparedPairs);
   }

   private async Task<string> RoslynPutsFirstAsync(Document document, string first, string second)
   {
      var pair = document.WithText(SourceText.From(first + "\n" + second + "\n"));
      var organized = await Formatter.OrganizeImportsAsync(pair, CT);
      return (await organized.GetTextAsync(CT)).Lines[0].ToString();
   }

   private async Task AssertRoslynKeepsAsync(string path, string written)
   {
      var document = await WorkspaceManager.GetDocumentAsync(path, CT);
      var roslynOrganized = await Formatter.OrganizeImportsAsync(document.WithText(SourceText.From(written)), CT);
      Assert.Equal(written, (await roslynOrganized.GetTextAsync(CT)).ToString());
   }

   /// <summary>
   /// Writes the source and checks that it parses without diagnostics, so a diagnostic in the result comes from the tool.
   /// </summary>
   private async Task<string> WriteSourceAsync(string relativePath, string text)
   {
      Assert.Empty(CSharpSyntaxTree.ParseText(text, cancellationToken: CT).GetDiagnostics());
      var path = GetCoreTestProjectPath(relativePath);
      var written = await new WriteFileTool(WorkspaceManager).WriteFileAsync(path, text, CT);
      Assert.True(written.Data.IsNewFile);
      return path;
   }
}
