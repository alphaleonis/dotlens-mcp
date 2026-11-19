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
/// Covers that add_using and organize_usings follow dotnet_sort_system_directives_first and
/// dotnet_separate_import_directive_groups from the .editorconfig of the file, as Roslyn's Organize Imports does.
/// </summary>
public class UsingToolsEditorConfigTests : IsolatedProjectTestBase
{
   private const string TargetPath = "EditorConfig/UsingTarget.cs";
   private const string EditorConfigPath = "EditorConfig/.editorconfig";

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      await WriteSourceAsync("EditorConfig/EditorConfigNamespaces.cs", """
         namespace Alpha
         {
            public class AlphaType { }
            public static class AlphaStatics { public static int AlphaValue => 1; }
         }

         namespace Alpha.Inner { public class InnerType { } }

         namespace Beta { public class BetaType { } }

         namespace Gamma
         {
            public class GammaType { }
            public static class GammaStatics { public static int GammaValue => 3; }
         }

         namespace Zeta
         {
            public class ZetaType { }
            public static class ZetaStatics { public static int ZetaValue => 2; }
         }

         """);
   }

   private const string MixedKinds = """
      using static Zeta.ZetaStatics;
      global using Gamma;
      using EditorBuilder = System.Text.StringBuilder;
      using Zeta;
      global using static Gamma.GammaStatics;
      using alpha = Zeta.ZetaType;
      using static System.Math;
      using Alpha.Inner;
      global using System.Globalization;
      using Alpha;
      using System.Text;
      using AlphaAlias = Alpha.AlphaType;
      global using GammaAlias = Gamma.GammaType;
      using static Alpha.AlphaStatics;
      using System.Text.RegularExpressions;
      """;

   private const string MixedKindsWithBlankLines = """
      using static Zeta.ZetaStatics;
      global using Gamma;

      using EditorBuilder = System.Text.StringBuilder;
      using Zeta;

      global using static Gamma.GammaStatics;
      using alpha = Zeta.ZetaType;

      using static System.Math;
      using Alpha.Inner;
      global using System.Globalization;

      using Alpha;
      using System.Text;
      using AlphaAlias = Alpha.AlphaType;

      global using GammaAlias = Gamma.GammaType;
      using static Alpha.AlphaStatics;

      using System.Text.RegularExpressions;
      """;

   private const string SystemFirst = """
      global using System.Globalization;
      global using Gamma;
      global using static Gamma.GammaStatics;
      global using GammaAlias = Gamma.GammaType;
      using System.Text;
      using System.Text.RegularExpressions;
      using Alpha;
      using Alpha.Inner;
      using Zeta;
      using static System.Math;
      using static Alpha.AlphaStatics;
      using static Zeta.ZetaStatics;
      using alpha = Zeta.ZetaType;
      using AlphaAlias = Alpha.AlphaType;
      using EditorBuilder = System.Text.StringBuilder;
      """;

   private const string SystemFirstSeparated = """
      global using System.Globalization;

      global using Gamma;

      global using static Gamma.GammaStatics;

      global using GammaAlias = Gamma.GammaType;

      using System.Text;
      using System.Text.RegularExpressions;

      using Alpha;
      using Alpha.Inner;

      using Zeta;

      using static System.Math;
      using static Alpha.AlphaStatics;
      using static Zeta.ZetaStatics;

      using alpha = Zeta.ZetaType;
      using AlphaAlias = Alpha.AlphaType;
      using EditorBuilder = System.Text.StringBuilder;
      """;

   private const string Alphabetical = """
      global using Gamma;
      global using System.Globalization;
      global using static Gamma.GammaStatics;
      global using GammaAlias = Gamma.GammaType;
      using Alpha;
      using Alpha.Inner;
      using System.Text;
      using System.Text.RegularExpressions;
      using Zeta;
      using static Alpha.AlphaStatics;
      using static System.Math;
      using static Zeta.ZetaStatics;
      using alpha = Zeta.ZetaType;
      using AlphaAlias = Alpha.AlphaType;
      using EditorBuilder = System.Text.StringBuilder;
      """;

   private const string AlphabeticalSeparated = """
      global using Gamma;

      global using System.Globalization;

      global using static Gamma.GammaStatics;

      global using GammaAlias = Gamma.GammaType;

      using Alpha;
      using Alpha.Inner;

      using System.Text;
      using System.Text.RegularExpressions;

      using Zeta;

      using static Alpha.AlphaStatics;
      using static System.Math;
      using static Zeta.ZetaStatics;

      using alpha = Zeta.ZetaType;
      using AlphaAlias = Alpha.AlphaType;
      using EditorBuilder = System.Text.StringBuilder;
      """;

   /// <summary>
   /// Sorted with System first, with a blank line inside the System and static groups and none between most groups.
   /// </summary>
   private const string SystemFirstPartlySeparated = """
      global using System.Globalization;
      global using Gamma;
      global using static Gamma.GammaStatics;
      global using GammaAlias = Gamma.GammaType;
      using System.Text;

      using System.Text.RegularExpressions;
      using Alpha;
      using Alpha.Inner;

      using Zeta;
      using static System.Math;
      using static Alpha.AlphaStatics;

      using static Zeta.ZetaStatics;
      using alpha = Zeta.ZetaType;
      using AlphaAlias = Alpha.AlphaType;
      using EditorBuilder = System.Text.StringBuilder;
      """;

   public static TheoryData<bool, bool, string> MixedKindsCases => new()
   {
      { true, false, SystemFirst },
      { true, true, SystemFirstSeparated },
      { false, false, Alphabetical },
      { false, true, AlphabeticalSeparated },
   };

   /// <summary>
   /// Roslyn keeps the blank lines of a list in order and adds the missing ones between groups. It drops the blank
   /// lines of a list that it sorts.
   /// </summary>
   public static TheoryData<bool, bool, string> PartlySeparatedCases => new()
   {
      { true, false, SystemFirstPartlySeparated },
      {
         true, true, """
            global using System.Globalization;

            global using Gamma;

            global using static Gamma.GammaStatics;

            global using GammaAlias = Gamma.GammaType;

            using System.Text;

            using System.Text.RegularExpressions;

            using Alpha;
            using Alpha.Inner;

            using Zeta;

            using static System.Math;
            using static Alpha.AlphaStatics;

            using static Zeta.ZetaStatics;

            using alpha = Zeta.ZetaType;
            using AlphaAlias = Alpha.AlphaType;
            using EditorBuilder = System.Text.StringBuilder;
            """
      },
      { false, false, Alphabetical },
      { false, true, AlphabeticalSeparated },
   };

   [Theory]
   [MemberData(nameof(MixedKindsCases))]
   public async Task UsingTools_EditorConfig_OrganizeUsings_MixedKinds_WritesWhatRoslynWrites(
      bool sortSystemFirst,
      bool separateGroups,
      string expectedUsings)
   {
      await UseEditorConfigAsync(sortSystemFirst, separateGroups);

      await AssertOrganizeUsingsAsync(MixedKindsSource(MixedKinds), MixedKindsSource(expectedUsings));
   }

   [Theory]
   [MemberData(nameof(MixedKindsCases))]
   public async Task UsingTools_EditorConfig_OrganizeUsings_MixedKindsWithBlankLines_WritesWhatRoslynWrites(
      bool sortSystemFirst,
      bool separateGroups,
      string expectedUsings)
   {
      await UseEditorConfigAsync(sortSystemFirst, separateGroups);

      await AssertOrganizeUsingsAsync(MixedKindsSource(MixedKindsWithBlankLines), MixedKindsSource(expectedUsings));
   }

   [Theory]
   [MemberData(nameof(PartlySeparatedCases))]
   public async Task UsingTools_EditorConfig_OrganizeUsings_SortedWithSomeBlankLines_WritesWhatRoslynWrites(
      bool sortSystemFirst,
      bool separateGroups,
      string expectedUsings)
   {
      await UseEditorConfigAsync(sortSystemFirst, separateGroups);

      await AssertOrganizeUsingsAsync(
         MixedKindsSource(SystemFirstPartlySeparated), MixedKindsSource(expectedUsings));
   }

   public static TheoryData<bool, bool, string> NamespaceCases => new()
   {
      {
         true, false, """
            using System.Text;
            using Zeta;

            namespace CoreTestProject.EditorConfig
            {
               using System.Globalization;
               using Alpha;
               using Alpha.Inner;
               using static Zeta.ZetaStatics;
               using AlphaAlias = Alpha.AlphaType;
            """
      },
      {
         true, true, """
            using System.Text;

            using Zeta;

            namespace CoreTestProject.EditorConfig
            {
               using System.Globalization;

               using Alpha;
               using Alpha.Inner;

               using static Zeta.ZetaStatics;

               using AlphaAlias = Alpha.AlphaType;
            """
      },
      {
         false, false, """
            using System.Text;
            using Zeta;

            namespace CoreTestProject.EditorConfig
            {
               using Alpha;
               using Alpha.Inner;
               using System.Globalization;
               using static Zeta.ZetaStatics;
               using AlphaAlias = Alpha.AlphaType;
            """
      },
      {
         false, true, """
            using System.Text;

            using Zeta;

            namespace CoreTestProject.EditorConfig
            {
               using Alpha;
               using Alpha.Inner;

               using System.Globalization;

               using static Zeta.ZetaStatics;

               using AlphaAlias = Alpha.AlphaType;
            """
      },
   };

   [Theory]
   [MemberData(nameof(NamespaceCases))]
   public async Task UsingTools_EditorConfig_OrganizeUsings_InsideNamespace_WritesWhatRoslynWrites(
      bool sortSystemFirst,
      bool separateGroups,
      string expectedUsings)
   {
      await UseEditorConfigAsync(sortSystemFirst, separateGroups);

      await AssertOrganizeUsingsAsync(NamespaceSource("""
         using Zeta;
         using System.Text;

         namespace CoreTestProject.EditorConfig
         {
            using static Zeta.ZetaStatics;
            using Alpha.Inner;
            using AlphaAlias = Alpha.AlphaType;
            using System.Globalization;
            using Alpha;
         """), NamespaceSource(expectedUsings));
   }

   public static TheoryData<bool, bool, string> ExternAliasCases => new()
   {
      {
         true, false, """
            extern alias Alpha;
            extern alias Zulu;
            using System.Text;
            using Zeta;
            """
      },
      {
         true, true, """
            extern alias Alpha;
            extern alias Zulu;

            using System.Text;

            using Zeta;
            """
      },
      {
         false, false, """
            extern alias Alpha;
            extern alias Zulu;
            using System.Text;
            using Zeta;
            """
      },
      {
         false, true, """
            extern alias Alpha;
            extern alias Zulu;

            using System.Text;

            using Zeta;
            """
      },
   };

   [Theory]
   [MemberData(nameof(ExternAliasCases))]
   public async Task UsingTools_EditorConfig_OrganizeUsings_ExternAliases_WritesWhatRoslynWrites(
      bool sortSystemFirst,
      bool separateGroups,
      string expectedUsings)
   {
      await UseEditorConfigAsync(sortSystemFirst, separateGroups);

      await AssertOrganizeUsingsAsync(ExternSource("""
         extern alias Zulu;
         extern alias Alpha;
         using Zeta;
         using System.Text;
         """), ExternSource(expectedUsings));
   }

   /// <summary>
   /// Pairs of a global and a non-global using of one kind, and for namespace usings of one first identifier, in order
   /// and reversed.
   /// </summary>
   public static TheoryData<bool, bool, string, string> GlobalAndLocalCases()
   {
      string[][] pairs =
      [
         ["global using Alpha;", "using Alpha.Inner;"],
         ["global using static Gamma.GammaStatics;", "using static Zeta.ZetaStatics;"],
         ["global using GammaAlias = Gamma.GammaType;", "using AlphaAlias = Alpha.AlphaType;"],
      ];
      var data = new TheoryData<bool, bool, string, string>();
      (bool, bool)[] combinations = [(true, false), (true, true), (false, false), (false, true)];
      foreach (var (sortSystemFirst, separateGroups) in combinations)
      {
         foreach (var pair in pairs)
         {
            var expected = pair[0] + "\n" + pair[1];
            data.Add(sortSystemFirst, separateGroups, expected, expected);
            data.Add(sortSystemFirst, separateGroups, pair[1] + "\n" + pair[0], expected);
         }
      }

      return data;
   }

   /// <summary>
   /// Being global or not does not split a group, so Roslyn puts no blank line between the two usings.
   /// </summary>
   [Theory]
   [MemberData(nameof(GlobalAndLocalCases))]
   public async Task UsingTools_EditorConfig_OrganizeUsings_GlobalThenLocalOfOneGroup_WritesWhatRoslynWrites(
      bool sortSystemFirst,
      bool separateGroups,
      string usings,
      string expectedUsings)
   {
      await UseEditorConfigAsync(sortSystemFirst, separateGroups);

      await AssertOrganizeUsingsAsync(GroupPairSource(usings), GroupPairSource(expectedUsings));
   }

   /// <summary>
   /// Roslyn sorts the extern aliases and usings as one list, so the blank line between them goes when either is out of
   /// order.
   /// </summary>
   [Theory]
   [MemberData(nameof(ExternAliasCases))]
   public async Task UsingTools_EditorConfig_OrganizeUsings_ExternAliasesAndBlankLine_WritesWhatRoslynWrites(
      bool sortSystemFirst,
      bool separateGroups,
      string expectedUsings)
   {
      await UseEditorConfigAsync(sortSystemFirst, separateGroups);

      await AssertOrganizeUsingsAsync(ExternSource("""
         extern alias Zulu;
         extern alias Alpha;

         using Zeta;
         using System.Text;
         """), ExternSource(expectedUsings));
   }

   public static TheoryData<bool, bool, string> SortedExternAliasCases => new()
   {
      { true, false, SortedExternAliases },
      {
         true, true, """
            extern alias Alpha;
            extern alias Zulu;

            using System.Text;

            using Zeta;
            """
      },
      { false, false, SortedExternAliases },
      {
         false, true, """
            extern alias Alpha;
            extern alias Zulu;

            using System.Text;

            using Zeta;
            """
      },
   };

   private const string SortedExternAliases = """
      extern alias Alpha;
      extern alias Zulu;

      using System.Text;
      using Zeta;
      """;

   /// <summary>
   /// A list in order keeps the blank line between its extern aliases and its usings.
   /// </summary>
   [Theory]
   [MemberData(nameof(SortedExternAliasCases))]
   public async Task UsingTools_EditorConfig_OrganizeUsings_SortedExternAliasesAndBlankLine_WritesWhatRoslynWrites(
      bool sortSystemFirst,
      bool separateGroups,
      string expectedUsings)
   {
      await UseEditorConfigAsync(sortSystemFirst, separateGroups);

      await AssertOrganizeUsingsAsync(ExternSource(SortedExternAliases), ExternSource(expectedUsings));
   }

   public static TheoryData<bool, bool, string> WhitespaceLineCases => new()
   {
      { true, false, "\n\n   \n   using System.Text;\n   using Alpha;\n   using Zeta;" },
      { true, true, "\n\n   \n   using System.Text;\n\n   using Alpha;\n\n   using Zeta;" },
      { false, false, "\n\n   using Alpha;\n   \n   using System.Text;\n   using Zeta;" },
      { false, true, "\n\n   using Alpha;\n   \n   using System.Text;\n\n   using Zeta;" },
   };

   /// <summary>
   /// Lines that hold only spaces stay when Roslyn sorts, since it removes only the line breaks that start the leading
   /// trivia of each using.
   /// </summary>
   [Theory]
   [MemberData(nameof(WhitespaceLineCases))]
   public async Task UsingTools_EditorConfig_OrganizeUsings_WhitespaceOnlyLineInNamespace_WritesWhatRoslynWrites(
      bool sortSystemFirst,
      bool separateGroups,
      string expectedUsings)
   {
      await UseEditorConfigAsync(sortSystemFirst, separateGroups);

      await AssertOrganizeUsingsAsync(
         WhitespaceLineSource("\n\n   using Zeta;\n\n   using Alpha;\n   \n   using System.Text;"),
         WhitespaceLineSource(expectedUsings));
   }

   [Theory]
   [InlineData("using Zeta;\n\n   \nusing Alpha;", "   \nusing Alpha;\nusing Zeta;")]
   [InlineData("using Zeta;\n   \n\nusing Alpha;", "   \n\nusing Alpha;\nusing Zeta;")]
   public async Task UsingTools_EditorConfig_OrganizeUsings_WhitespaceOnlyLineAtFileLevel_WritesWhatRoslynWrites(
      string usings,
      string expectedUsings)
   {
      await UseEditorConfigAsync(sortSystemFirst: true, separateGroups: false);

      await AssertOrganizeUsingsAsync(ShortSource(usings, member: ""), ShortSource(expectedUsings, member: ""));
   }

   private const string FirstIdentifiersInOrder = """
      extern alias Ex;
      using @Alpha;
      using Alpha.Inner;
      using Beta.Sub;
      using gamma.Lower;
      using Gamma;
      using global::Zeta;
      using global::Zulu;
      """;

   private const string FirstIdentifiersScrambled = """
      extern alias Ex;
      using global::Zulu;
      using Gamma;
      using @Alpha;
      using gamma.Lower;
      using Beta.Sub;
      using global::Zeta;
      using Alpha.Inner;
      """;

   private const string FirstIdentifiersSeparated = """
      extern alias Ex;

      using @Alpha;
      using Alpha.Inner;

      using Beta.Sub;

      using gamma.Lower;

      using Gamma;

      using global::Zeta;
      using global::Zulu;
      """;

   public static TheoryData<bool, string, string> FirstIdentifierCases => new()
   {
      { false, FirstIdentifiersInOrder, FirstIdentifiersInOrder },
      { false, FirstIdentifiersScrambled, FirstIdentifiersInOrder },
      { true, FirstIdentifiersInOrder, FirstIdentifiersSeparated },
      { true, FirstIdentifiersScrambled, FirstIdentifiersSeparated },
   };

   /// <summary>
   /// Pins how Roslyn compares first identifiers: @Alpha is Alpha, case counts, global:: starts with global, and the
   /// usings below extern aliases start a group.
   /// </summary>
   [Theory]
   [MemberData(nameof(FirstIdentifierCases))]
   public async Task UsingTools_EditorConfig_OrganizeUsings_FirstIdentifiers_WritesWhatRoslynWrites(
      bool separateGroups,
      string usings,
      string expectedUsings)
   {
      await WriteSourceAsync("EditorConfig/FirstIdentifierNamespaces.cs", """
         namespace gamma.Lower { public class LowerType { } }

         namespace Beta.Sub { public class SubType { } }

         namespace Zulu { public class ZuluType { } }

         """);
      await UseEditorConfigAsync(sortSystemFirst: true, separateGroups);

      await AssertOrganizeUsingsAsync(FirstIdentifierSource(usings), FirstIdentifierSource(expectedUsings));
   }

   public static TheoryData<bool, string, string> DirectiveBelowExternAliasCases => new()
   {
      {
         false,
         "extern alias Ex;\n\n#region Usings\nusing Zeta;\nusing System.Text;\n\nusing Alpha;\n#endregion",
         "extern alias Ex;\n\n#region Usings\nusing System.Text;\n\nusing Alpha;\nusing Zeta;\n#endregion"
      },
      {
         true,
         "extern alias Ex;\n\n#region Usings\nusing Zeta;\nusing System.Text;\n\nusing Alpha;\n#endregion",
         "extern alias Ex;\n\n#region Usings\nusing System.Text;\n\nusing Alpha;\n\nusing Zeta;\n#endregion"
      },
      {
         false,
         "extern alias Ex;\n\n#pragma warning disable CS0168\nusing Zeta;\nusing Alpha;",
         "extern alias Ex;\n\n#pragma warning disable CS0168\nusing Alpha;\nusing Zeta;"
      },
      {
         true,
         "extern alias Ex;\n\n#pragma warning disable CS0168\nusing Zeta;\nusing Alpha;",
         "extern alias Ex;\n\n#pragma warning disable CS0168\nusing Alpha;\n\nusing Zeta;"
      },
   };

   /// <summary>
   /// A directive above the first using below extern aliases splits the list, so Roslyn leaves it as it is and keeps
   /// its blank lines. The tool sorts within the run of usings below the directive.
   /// </summary>
   [Theory]
   [MemberData(nameof(DirectiveBelowExternAliasCases))]
   public async Task UsingTools_EditorConfig_OrganizeUsings_DirectiveBelowExternAlias_KeepsBlankLines(
      bool separateGroups,
      string usings,
      string expectedUsings)
   {
      await UseEditorConfigAsync(sortSystemFirst: true, separateGroups);

      await AssertOrganizeUsingsAsync(
         SystemTextSource(usings), SystemTextSource(expectedUsings), roslynSortsInput: false);
   }

   public static TheoryData<bool, string, string> TriviaBelowExternAliasCases => new()
   {
      {
         false,
         "extern alias Ex;\n   \nusing Zeta;\nusing System.Text;\nusing Alpha;",
         "extern alias Ex;\nusing System.Text;\nusing Alpha;\n   \nusing Zeta;"
      },
      {
         true,
         "extern alias Ex;\n   \nusing Zeta;\nusing System.Text;\nusing Alpha;",
         "extern alias Ex;\n\nusing System.Text;\n\nusing Alpha;\n   \nusing Zeta;"
      },
      {
         false,
         "extern alias Ex;\n\n// note\n\nusing Zeta;\nusing Alpha;",
         "extern alias Ex;\nusing Alpha;\n// note\n\nusing Zeta;"
      },
      {
         true,
         "extern alias Ex;\n\n// note\n\nusing Zeta;\nusing Alpha;",
         "extern alias Ex;\n\nusing Alpha;\n// note\n\nusing Zeta;"
      },
      {
         false,
         "extern alias Ex;\n// note\nusing Zeta;\nusing Alpha;",
         "extern alias Ex;\nusing Alpha;\n// note\nusing Zeta;"
      },
      {
         true,
         "extern alias Ex;\n// note\nusing Zeta;\nusing Alpha;",
         "extern alias Ex;\n\nusing Alpha;\n// note\nusing Zeta;"
      },
   };

   public static TheoryData<bool, string, string> DirectiveAboveLaterUsingCases => new()
   {
      {
         false,
         "using Zeta;\nusing Alpha;\n\n#pragma warning disable CS0168\nusing Beta;\n\nusing Gamma;",
         "using Alpha;\nusing Zeta;\n\n#pragma warning disable CS0168\nusing Beta;\n\nusing Gamma;"
      },
      {
         true,
         "using Zeta;\nusing Alpha;\n\n#pragma warning disable CS0168\nusing Beta;\n\nusing Gamma;",
         "using Alpha;\n\nusing Zeta;\n\n#pragma warning disable CS0168\nusing Beta;\n\nusing Gamma;"
      },
      {
         false,
         "extern alias Ex;\nusing Zeta;\nusing Alpha;\n\n#pragma warning disable CS0168\nusing Beta;\n\nusing Gamma;",
         "extern alias Ex;\nusing Alpha;\nusing Zeta;\n\n#pragma warning disable CS0168\nusing Beta;\n\nusing Gamma;"
      },
      {
         true,
         "extern alias Ex;\nusing Zeta;\nusing Alpha;\n\n#pragma warning disable CS0168\nusing Beta;\n\nusing Gamma;",
         "extern alias Ex;\n\nusing Alpha;\n\nusing Zeta;\n\n"
            + "#pragma warning disable CS0168\nusing Beta;\n\nusing Gamma;"
      },
   };

   /// <summary>
   /// A directive above a later using splits the list, with or without extern aliases, so Roslyn leaves it as it is and
   /// keeps its blank lines. The tool sorts within each run.
   /// </summary>
   [Theory]
   [MemberData(nameof(DirectiveAboveLaterUsingCases))]
   public async Task UsingTools_EditorConfig_OrganizeUsings_DirectiveAboveLaterUsing_KeepsBlankLines(
      bool separateGroups,
      string usings,
      string expectedUsings)
   {
      await UseEditorConfigAsync(sortSystemFirst: true, separateGroups);

      await AssertOrganizeUsingsAsync(
         FourNamespacesSource(usings), FourNamespacesSource(expectedUsings), roslynSortsInput: false);
   }

   /// <summary>
   /// Without extern aliases, a directive above the first using is part of the file header, so Roslyn sorts the list.
   /// </summary>
   [Theory]
   [InlineData(false, "#pragma warning disable CS0168\n\nusing Alpha;\nusing Zeta;")]
   [InlineData(true, "#pragma warning disable CS0168\n\nusing Alpha;\n\nusing Zeta;")]
   public async Task UsingTools_EditorConfig_OrganizeUsings_DirectiveAboveFirstUsing_WritesWhatRoslynWrites(
      bool separateGroups,
      string expectedUsings)
   {
      await UseEditorConfigAsync(sortSystemFirst: true, separateGroups);

      await AssertOrganizeUsingsAsync(
         FourNamespacesSource("#pragma warning disable CS0168\n\nusing Zeta;\n\nusing Alpha;"),
         FourNamespacesSource(expectedUsings));
   }

   /// <summary>
   /// Below extern aliases no file header exists, so Roslyn moves the trivia above the first using with it.
   /// </summary>
   [Theory]
   [MemberData(nameof(TriviaBelowExternAliasCases))]
   public async Task UsingTools_EditorConfig_OrganizeUsings_TriviaBelowExternAlias_WritesWhatRoslynWrites(
      bool separateGroups,
      string usings,
      string expectedUsings)
   {
      await UseEditorConfigAsync(sortSystemFirst: true, separateGroups);

      await AssertOrganizeUsingsAsync(SystemTextSource(usings), SystemTextSource(expectedUsings));
   }

   /// <summary>
   /// Roslyn adds no blank line above a using whose leading trivia holds a line break, such as a comment line.
   /// </summary>
   [Fact]
   public async Task UsingTools_EditorConfig_OrganizeUsings_CommentAboveUsing_WritesWhatRoslynWrites()
   {
      await UseEditorConfigAsync(sortSystemFirst: true, separateGroups: true);

      await AssertOrganizeUsingsAsync(ShortSource("""
         using Alpha;
         // Zeta comment
         using Zeta;
         using static Zeta.ZetaStatics;
         """), ShortSource("""
         using Alpha;
         // Zeta comment
         using Zeta;

         using static Zeta.ZetaStatics;
         """));
   }

   /// <summary>
   /// Each run is in order, so the tool writes what Roslyn writes, which does not sort a list that a directive splits.
   /// </summary>
   [Fact]
   public async Task UsingTools_EditorConfig_OrganizeUsings_DirectivesBetweenGroups_WritesWhatRoslynWrites()
   {
      await UseEditorConfigAsync(sortSystemFirst: true, separateGroups: true);

      await AssertOrganizeUsingsAsync(ShortSource("""
         using Alpha;
         #if DEBUG
         using Beta;
         #endif
         using Zeta;
         using static Zeta.ZetaStatics;
         """, BetaMembers), ShortSource("""
         using Alpha;

         #if DEBUG
         using Beta;

         #endif
         using Zeta;

         using static Zeta.ZetaStatics;
         """, BetaMembers));
   }

   [Fact]
   public async Task UsingTools_EditorConfig_OrganizeUsings_GroupsOnOneLine_WritesWhatRoslynWrites()
   {
      await UseEditorConfigAsync(sortSystemFirst: true, separateGroups: true);

      await AssertOrganizeUsingsAsync(
         ShortSource("using Alpha; using Zeta;\nusing static Zeta.ZetaStatics;"),
         ShortSource("using Alpha; \nusing Zeta;\n\nusing static Zeta.ZetaStatics;"));
   }

   /// <summary>
   /// A value with a severity is not a Boolean, so both options keep their defaults.
   /// </summary>
   [Fact]
   public async Task UsingTools_EditorConfig_OrganizeUsings_ValuesWithSeverity_UsesDefaults()
   {
      await UseEditorConfigAsync("false:warning", "true:warning");

      await AssertOrganizeUsingsAsync(MixedKindsSource(MixedKinds), MixedKindsSource(SystemFirst));
   }

   /// <summary>
   /// The .editorconfig is on disk before the solution loads, so MSBuildWorkspace adds it as an analyzer config
   /// document.
   /// </summary>
   [Fact]
   public async Task UsingTools_EditorConfig_OrganizeUsings_EditorConfigOnDiskAtLoad_WritesWhatRoslynWrites()
   {
      var editorConfigPath = GetCoreTestProjectPath(EditorConfigPath);
      await File.WriteAllTextAsync(editorConfigPath, EditorConfigText("false", "true"), CT);
      await WorkspaceManager.LoadSolutionAsync(TestFixturesSlnxPath, CT);
      var project = Assert.Single(WorkspaceManager.EnsureSolutionLoaded().Projects, p => p.Name == "CoreTestProject");
      Assert.Contains(editorConfigPath, project.AnalyzerConfigDocuments.Select(d => d.FilePath));

      await AssertOrganizeUsingsAsync(MixedKindsSource(MixedKinds), MixedKindsSource(AlphabeticalSeparated));
   }

   public static TheoryData<bool, bool, string> AddToMixedKindsCases => new()
   {
      { true, false, SystemFirst.Replace("using Alpha.Inner;\n", "using Alpha.Inner;\nusing Beta;\n") },
      { true, true, SystemFirstSeparated.Replace("using Alpha.Inner;\n", "using Alpha.Inner;\n\nusing Beta;\n") },
      { false, false, Alphabetical.Replace("using Alpha.Inner;\n", "using Alpha.Inner;\nusing Beta;\n") },
      { false, true, AlphabeticalSeparated.Replace("using Alpha.Inner;\n", "using Alpha.Inner;\n\nusing Beta;\n") },
   };

   [Theory]
   [MemberData(nameof(AddToMixedKindsCases))]
   public async Task UsingTools_EditorConfig_AddUsing_MixedKindsWithBlankLines_RoslynKeepsOutput(
      bool sortSystemFirst,
      bool separateGroups,
      string expectedUsings)
   {
      await UseEditorConfigAsync(sortSystemFirst, separateGroups);

      await AssertAddUsingAsync("Beta", MixedKindsSource(MixedKindsWithBlankLines), MixedKindsSource(expectedUsings));
   }

   /// <summary>
   /// Without System first the list is out of order, so its blank lines go as they do when Roslyn sorts it.
   /// </summary>
   public static TheoryData<bool, bool, string> AddToSeparatedCases => new()
   {
      { true, false, SystemFirstSeparated.Replace("using Alpha.Inner;\n", "using Alpha.Inner;\nusing Beta;\n") },
      { true, true, SystemFirstSeparated.Replace("using Alpha.Inner;\n", "using Alpha.Inner;\n\nusing Beta;\n") },
      { false, false, Alphabetical.Replace("using Alpha.Inner;\n", "using Alpha.Inner;\nusing Beta;\n") },
      { false, true, AlphabeticalSeparated.Replace("using Alpha.Inner;\n", "using Alpha.Inner;\n\nusing Beta;\n") },
   };

   [Theory]
   [MemberData(nameof(AddToSeparatedCases))]
   public async Task UsingTools_EditorConfig_AddUsing_SystemFirstSeparated_RoslynKeepsOutput(
      bool sortSystemFirst,
      bool separateGroups,
      string expectedUsings)
   {
      await UseEditorConfigAsync(sortSystemFirst, separateGroups);

      await AssertAddUsingAsync("Beta", MixedKindsSource(SystemFirstSeparated), MixedKindsSource(expectedUsings));
   }

   public static TheoryData<bool, bool, string> AddInsideNamespaceCases => new()
   {
      {
         true, false, """
            using System.Globalization;
            using Alpha;
            using Beta;
            using Zeta;
            using static Zeta.ZetaStatics;
            using AlphaAlias = Alpha.AlphaType;
            """
      },
      {
         true, true, """
            using System.Globalization;

            using Alpha;

            using Beta;

            using Zeta;

            using static Zeta.ZetaStatics;

            using AlphaAlias = Alpha.AlphaType;
            """
      },
      {
         false, false, """
            using Alpha;
            using Beta;
            using System.Globalization;
            using Zeta;
            using static Zeta.ZetaStatics;
            using AlphaAlias = Alpha.AlphaType;
            """
      },
      {
         false, true, """
            using Alpha;

            using Beta;

            using System.Globalization;

            using Zeta;

            using static Zeta.ZetaStatics;

            using AlphaAlias = Alpha.AlphaType;
            """
      },
   };

   [Theory]
   [MemberData(nameof(AddInsideNamespaceCases))]
   public async Task UsingTools_EditorConfig_AddUsing_InsideNamespace_RoslynKeepsOutput(
      bool sortSystemFirst,
      bool separateGroups,
      string expectedUsings)
   {
      await UseEditorConfigAsync(sortSystemFirst, separateGroups);

      await AssertAddUsingAsync("Alpha", AddInsideNamespaceSource("""
         using static Zeta.ZetaStatics;
         using Zeta;
         using AlphaAlias = Alpha.AlphaType;
         using System.Globalization;
         using Beta;
         """), AddInsideNamespaceSource(expectedUsings));
   }

   /// <summary>
   /// An alias-qualified name starts with its alias, so Ex::Bar and Ex.Baz are one group. An unresolved extern alias
   /// makes organize_usings remove both, so add_using pins the case.
   /// </summary>
   [Fact]
   public async Task UsingTools_EditorConfig_AddUsing_AliasQualifiedName_RoslynKeepsOutput()
   {
      await UseEditorConfigAsync(sortSystemFirst: true, separateGroups: true);

      await AssertAddUsingAsync("Alpha", """
         extern alias Ex;
         using Ex::Bar;
         using Ex.Baz;

         namespace CoreTestProject.EditorConfig;

         public class AliasQualifiedTarget
         {
         }

         """, """
         extern alias Ex;

         using Alpha;

         using Ex::Bar;
         using Ex.Baz;

         namespace CoreTestProject.EditorConfig;

         public class AliasQualifiedTarget
         {
         }

         """);
   }

   [Fact]
   public async Task UsingTools_EditorConfig_AddUsing_FileWithOnlyExternAlias_RoslynKeepsOutput()
   {
      await UseEditorConfigAsync(sortSystemFirst: true, separateGroups: true);

      await AssertAddUsingAsync("Beta", """
         extern alias Zulu;
         namespace CoreTestProject.EditorConfig;

         public class ExternOnlyTarget
         {
         }

         """, """
         extern alias Zulu;

         using Beta;

         namespace CoreTestProject.EditorConfig;

         public class ExternOnlyTarget
         {
         }

         """);
   }

   private static string MixedKindsSource(string usings) => $$"""
      {{usings}}

      namespace CoreTestProject.EditorConfig;

      public class MixedKindsTarget
      {
         public CultureInfo? Culture;
         public GammaType? GammaPlain;
         public int GammaStatic => GammaValue;
         public GammaAlias? GammaAliased;
         public Encoding? Text;
         public Regex? Pattern;
         public AlphaType? AlphaPlain;
         public InnerType? Inner;
         public ZetaType? ZetaPlain;
         public double Math => Abs(-1);
         public int AlphaStatic => AlphaValue;
         public int ZetaStatic => ZetaValue;
         public alpha? LowerAlias;
         public AlphaAlias? UpperAlias;
         public EditorBuilder? Builder;
      }

      """;

   private static string NamespaceSource(string usings) => $$"""
      {{usings}}

         public class NamespaceTarget
         {
            public StringBuilder? Builder;
            public ZetaType? ZetaPlain;
            public int ZetaStatic => ZetaValue;
            public InnerType? Inner;
            public AlphaAlias? Aliased;
            public CultureInfo? Culture;
            public AlphaType? AlphaPlain;
         }
      }

      """;

   private static string ExternSource(string usings) => $$"""
      {{usings}}

      namespace CoreTestProject.EditorConfig;

      public class ExternTarget
      {
         public StringBuilder? Builder;
         public ZetaType? ZetaPlain;
      }

      """;

   private const string BetaMembers = "public BetaType? BetaPlain; public int ZetaStatic => ZetaValue;";

   private static string ShortSource(string usings, string member = "public int ZetaStatic => ZetaValue;") => $$"""
      {{usings}}

      namespace CoreTestProject.EditorConfig;

      public class ShortTarget
      {
         public AlphaType? AlphaPlain;
         public ZetaType? ZetaPlain;
         {{member}}
      }

      """;

   private static string WhitespaceLineSource(string usings) =>
      "namespace CoreTestProject.EditorConfig\n{" + usings + """


            public class WhitespaceLineTarget
            {
               public StringBuilder? Builder;
               public AlphaType? AlphaPlain;
               public ZetaType? ZetaPlain;
            }
         }

         """;

   private static string FirstIdentifierSource(string usings) => $$"""
      {{usings}}

      namespace CoreTestProject.EditorConfig;

      public class FirstIdentifierTarget
      {
         public AlphaType? AlphaPlain;
         public InnerType? Inner;
         public SubType? Sub;
         public LowerType? Lower;
         public GammaType? GammaPlain;
         public ZetaType? ZetaPlain;
         public ZuluType? ZuluPlain;
      }

      """;

   private static string SystemTextSource(string usings) => $$"""
      {{usings}}

      namespace CoreTestProject.EditorConfig;

      public class SystemTextTarget
      {
         public StringBuilder? Builder;
         public AlphaType? AlphaPlain;
         public ZetaType? ZetaPlain;
      }

      """;

   private static string FourNamespacesSource(string usings) => $$"""
      {{usings}}

      namespace CoreTestProject.EditorConfig;

      public class FourNamespacesTarget
      {
         public AlphaType? AlphaPlain;
         public BetaType? BetaPlain;
         public GammaType? GammaPlain;
         public ZetaType? ZetaPlain;
      }

      """;

   private static string GroupPairSource(string usings) => $$"""
      {{usings}}

      namespace CoreTestProject.EditorConfig;

      public class GroupPairTarget
      {
         public AlphaType? AlphaPlain;
         public InnerType? Inner;
         public int GammaStatic => GammaValue;
         public int ZetaStatic => ZetaValue;
         public GammaAlias? GammaAliased;
         public AlphaAlias? AlphaAliased;
      }

      """;

   private static string AddInsideNamespaceSource(string usings) => $$"""
      namespace CoreTestProject.EditorConfig
      {
      {{Indent(usings)}}

         public class AddInsideNamespaceTarget
         {
         }
      }

      """;

   private static string Indent(string lines) =>
      string.Join("\n", lines.Split('\n').Select(l => l.Length == 0 ? l : "   " + l));

   private static string EditorConfigText(string sortSystemFirst, string separateGroups) => $"""
      root = true

      [*.cs]
      dotnet_sort_system_directives_first = {sortSystemFirst}
      dotnet_separate_import_directive_groups = {separateGroups}

      """;

   private Task UseEditorConfigAsync(bool sortSystemFirst, bool separateGroups) =>
      UseEditorConfigAsync(sortSystemFirst ? "true" : "false", separateGroups ? "true" : "false");

   /// <summary>
   /// Writes the .editorconfig and adds it to CoreTestProject, as a load of the solution would.
   /// </summary>
   private async Task UseEditorConfigAsync(string sortSystemFirst, string separateGroups)
   {
      var path = GetCoreTestProjectPath(EditorConfigPath);
      var text = EditorConfigText(sortSystemFirst, separateGroups);
      await File.WriteAllTextAsync(path, text, CT);
      using var writeLock = await WorkspaceManager.WriteLock.AcquireAsync(CT);
      var project = Assert.Single(WorkspaceManager.EnsureSolutionLoaded().Projects, p => p.Name == "CoreTestProject");
      Assert.DoesNotContain(path, project.AnalyzerConfigDocuments.Select(d => d.FilePath));
      WorkspaceManager.UpdateSolution(
         project.AddAnalyzerConfigDocument(".editorconfig", SourceText.From(text), filePath: path).Project.Solution);
   }

   /// <param name="roslynSortsInput">
   /// Whether Roslyn's Organize Imports writes the expected text. When it does not, it must keep the order of the
   /// usings of the source, and leave the expected text as it is.
   /// </param>
   private async Task AssertOrganizeUsingsAsync(string source, string expected, bool roslynSortsInput = true)
   {
      var path = await WriteSourceAsync(TargetPath, source);
      var document = await WorkspaceManager.GetDocumentAsync(path, CT);
      var roslynOrganized = await Formatter.OrganizeImportsAsync(document, CT);

      var result = await new OrganizeUsingsTool(WorkspaceManager).OrganizeUsingsAsync(
         new FileScope { Path = path }, preview: false, cancellationToken: CT);

      Assert.True(result.Data.Applied);
      Assert.Equal(expected == source ? 0 : 1, result.Data.FilesChanged);
      Assert.Equal(0, result.Data.TotalUsingsRemoved);
      var written = await File.ReadAllTextAsync(path, CT);
      Assert.Equal(expected, written);
      Assert.Empty(ParseDiagnostics(written));
      if (roslynSortsInput)
      {
         Assert.Equal((await roslynOrganized.GetTextAsync(CT)).ToString(), written);
         return;
      }

      Assert.Equal(UsingLines(source), UsingLines((await roslynOrganized.GetTextAsync(CT)).ToString()));
      var reorganized = await Formatter.OrganizeImportsAsync(document.WithText(SourceText.From(written)), CT);
      Assert.Equal(written, (await reorganized.GetTextAsync(CT)).ToString());
   }

   private async Task AssertAddUsingAsync(string @namespace, string source, string expected)
   {
      var path = await WriteSourceAsync(TargetPath, source);

      var result = await new AddUsingTool(WorkspaceManager).AddUsingAsync(path, @namespace, preview: false, CT);

      Assert.True(result.Data.Added);
      Assert.True(result.Data.Applied);
      var written = await File.ReadAllTextAsync(path, CT);
      Assert.Equal(expected, written);
      Assert.Empty(ParseDiagnostics(written));
      var document = await WorkspaceManager.GetDocumentAsync(path, CT);
      var roslynOrganized = await Formatter.OrganizeImportsAsync(document.WithText(SourceText.From(written)), CT);
      Assert.Equal(written, (await roslynOrganized.GetTextAsync(CT)).ToString());
   }

   private static List<string> UsingLines(string text) =>
      [.. text.Split('\n').Where(l => l.StartsWith("using ", StringComparison.Ordinal))];

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
