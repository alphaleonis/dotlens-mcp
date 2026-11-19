using System.Text;
using DotLens.Infrastructure;
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
/// Covers that organize_usings and add_using keep the order of a using list that a preprocessor directive splits, as
/// Roslyn's Organize Imports does, under each combination of the two .editorconfig using options.
/// </summary>
/// <remarks>
/// Each test writes its source once for each combination, in a folder whose .editorconfig sets it, and compares one
/// report of all combinations, so a failure shows every combination that differs. The tests share one loaded workspace.
/// </remarks>
public class UsingToolsDirectiveSplitTests : IClassFixture<DirectiveSplitWorkspace>
{
   private const string Members = "MEMBERS";

   private static readonly (string Folder, bool SortSystemFirst, bool SeparateGroups)[] Combinations =
      DirectiveSplitWorkspace.Combinations;

   private readonly DirectiveSplitWorkspace _fixture;

   public UsingToolsDirectiveSplitTests(DirectiveSplitWorkspace fixture)
   {
      _fixture = fixture;
   }

   private static CancellationToken CT => TestContext.Current.CancellationToken;

   private WorkspaceManager WorkspaceManager => _fixture.Workspace;

   private static readonly CSharpParseOptions DebugParseOptions = new(preprocessorSymbols: ["DEBUG"]);

   /// <summary>
   /// The member of the target class that uses each using of the shapes, by the using's name.
   /// </summary>
   private static readonly Dictionary<string, string> MemberOf = new()
   {
      ["Alpha"] = "public AlphaType? AlphaPlain;",
      ["Beta"] = "public BetaType? BetaPlain;",
      ["Gamma"] = "public GammaType? GammaPlain;",
      ["Juliet"] = "public JulietType? JulietPlain;",
      ["Kilo"] = "public KiloType? KiloPlain;",
      ["Mike"] = "public MikeType? MikePlain;",
      ["Zeta"] = "public ZetaType? ZetaPlain;",
      ["System.Text"] = "public StringBuilder? Builder;",
      ["System.Globalization"] = "public CultureInfo? Culture;",
      ["static Alpha.AlphaStatics"] = "public int AlphaStatic => AlphaValue;",
      ["static Zeta.ZetaStatics"] = "public int ZetaStatic => ZetaValue;",
      ["AlphaAlias = Alpha.AlphaType"] = "public AlphaAlias? AlphaAliased;",
      ["ZetaAlias = Zeta.ZetaType"] = "public ZetaAlias? ZetaAliased;",
   };

   /// <summary>
   /// A source for organize_usings, and the directives the tool writes from it.
   /// </summary>
   /// <param name="Source">The source, with MEMBERS where the members that use its usings go.</param>
   /// <param name="Unused">The lines of the usings that nothing uses.</param>
   /// <param name="Written">The directives in the order the tool writes them, or null when it keeps their order.</param>
   /// <param name="Crlf">Whether the shape runs with CRLF line breaks too.</param>
   private sealed record OrganizeShape(string Source, string[] Unused, string[]? Written = null, bool Crlf = false);

   /// <summary>
   /// A source for add_using, and the place the tool picks for the new using.
   /// </summary>
   /// <param name="Source">The source, with MEMBERS in place of the members, which add_using does not need.</param>
   /// <param name="Namespace">The namespace to add.</param>
   /// <param name="Placed">The source with the new using at the place the tool picks.</param>
   /// <param name="PlacedSystemFirst">
   /// The source with the new using at the place the tool picks when System sorts first, or null when it is
   /// <paramref name="Placed"/>.
   /// </param>
   /// <param name="Crlf">Whether the shape runs with CRLF line breaks too.</param>
   private sealed record AddShape(
      string Source,
      string Namespace,
      string Placed,
      string? PlacedSystemFirst = null,
      bool Crlf = false);

   private static readonly Dictionary<string, OrganizeShape> OrganizeShapes = new()
   {
      ["IfBetweenUnsortedRuns"] = new(
         FileSource("using Zeta;\nusing Alpha;\n#if DEBUG\nusing Mike;\nusing Kilo;\n#endif\nusing Beta;\nusing System.Text;\n"),
         [],
         Crlf: true),
      ["RegionBetweenUnsortedRuns"] = new(
         FileSource("using Zeta;\nusing Alpha;\n#region Usings\nusing Mike;\nusing Kilo;\n#endregion\nusing Beta;\n"),
         []),
      ["PragmaBetweenUnsortedRuns"] = new(
         FileSource("using Zeta;\nusing Alpha;\n#pragma warning disable CS0168\nusing Mike;\nusing Kilo;\n"),
         []),
      ["IfBetweenExternAliases"] = new(
         FileSource("// Header comment\n\nextern alias Zulu;\nextern alias xray;\n#if DEBUG\nextern alias Yankee;\n"
            + "extern alias Whiskey;\n#endif\nextern alias Victor;\nextern alias Xray;\n\nusing System.Text;\n"),
         []),
      ["RegionAboveFirstUsingBelowExternAlias"] = new(
         FileSource("extern alias Ex;\n\n#region Usings\nusing Zeta;\nusing System.Text;\n\nusing Alpha;\n#endregion\n"),
         []),
      ["PragmaAboveFirstUsingBelowExternAlias"] = new(
         FileSource("extern alias Ex;\n\n#pragma warning disable CS0168\nusing Zeta;\nusing Alpha;\n"),
         []),
      ["IfAboveFirstUsingBelowExternAlias"] = new(
         FileSource("extern alias Ex;\n#if DEBUG\nusing Zeta;\nusing Alpha;\n#endif\n"),
         []),
      ["PragmaAboveFirstUsing"] = new(
         FileSource("#pragma warning disable CS0168\n\nusing Zeta;\n\nusing Alpha;\n"),
         [],
         Written: ["using Alpha;", "using Zeta;"]),
      ["IfAroundAllUsings"] = new(
         FileSource("#if DEBUG\nusing Zeta;\nusing Alpha;\n#endif\n"),
         [],
         Written: ["using Alpha;", "using Zeta;"]),
      ["IfBetweenGroups"] = new(
         FileSource("using Alpha;\n#if DEBUG\nusing Beta;\n#endif\nusing Zeta;\nusing static Zeta.ZetaStatics;\n"),
         []),
      ["RemovalEmptiesPragmaRunBelow"] = new(
         FileSource("using Zeta;\n\nusing Alpha;\n#pragma warning disable CS0168\nusing Spare;\n"),
         ["using Spare;"],
         Written: ["using Alpha;", "using Zeta;"]),
      ["RemovalEmptiesIfRunAbove"] = new(
         FileSource("#if DEBUG\nusing Spare;\n#endif\nusing Zeta;\n\nusing Alpha;\n"),
         ["using Spare;"],
         Written: ["using Alpha;", "using Zeta;"]),
      ["RemovalEmptiesRegionRunBelow"] = new(
         FileSource("using Zeta;\nusing Alpha;\n#region Usings\nusing Spare;\n#endregion\n"),
         ["using Spare;"],
         Written: ["using Alpha;", "using Zeta;"]),
      ["RemovalEmptiesIfRunBelow"] = new(
         FileSource("using Zeta;\nusing Alpha;\n#if DEBUG\nusing Spare;\n#endif\n"),
         ["using Spare;"],
         Written: ["using Alpha;", "using Zeta;"]),
      ["RemovalEmptiesMiddleRun"] = new(
         FileSource("using Zeta;\nusing Alpha;\n#if DEBUG\nusing Spare;\n#endif\nusing Kilo;\nusing Beta;\n"),
         ["using Spare;"],
         Crlf: true),
      ["RemovalEmptiesMiddleRunBetweenBlankLines"] = new(
         FileSource("using Zeta;\nusing Alpha;\n\n#if DEBUG\nusing Spare;\n#endif\n\nusing Kilo;\nusing Beta;\n"),
         ["using Spare;"]),
      ["RemovalOfFirstUsingOfRun"] = new(
         FileSource("using Zeta;\n#if DEBUG\nusing Spare;\nusing Mike;\nusing Kilo;\n#endif\nusing Alpha;\n"),
         ["using Spare;"]),
      ["RemovalAboveDirective"] = new(
         FileSource("using Zeta;\nusing Spare;\nusing Alpha;\n#if DEBUG\nusing Mike;\n#endif\n"),
         ["using Spare;"]),
      ["RemovalEmptiesFirstRunBelowExternAlias"] = new(
         FileSource("extern alias Ex;\n#if DEBUG\nusing Spare;\n#endif\nusing Zeta;\nusing Alpha;\n"),
         ["using Spare;"]),
      ["RemovalOfEveryUsing"] = new(
         FileSource("using Spare;\n#if DEBUG\nusing Extra;\n#endif\n"),
         ["using Spare;", "using Extra;"]),
      ["RemovalEmptiesFirstRunBelowHeader"] = new(
         FileSource("// Header comment\n\nusing Spare;\n#if DEBUG\nusing Mike;\n#endif\nusing Zeta;\nusing Alpha;\n"),
         ["using Spare;"]),
      ["RemovalEmptiesFirstRunAbovePragma"] = new(
         FileSource("// Header comment\n\nusing Spare;\n#pragma warning disable CS0168\nusing Zeta;\n\nusing Alpha;\n"),
         ["using Spare;"],
         Written: ["using Alpha;", "using Zeta;"],
         Crlf: true),
      ["RemovalEmptiesRunBetweenDirectives"] = new(
         FileSource("using Zeta;\n#if DEBUG\nusing Spare;\nusing Extra;\n#endif\n#pragma warning disable CS0168\nusing Alpha;\n"),
         ["using Spare;", "using Extra;"]),
      ["RemovalAtGroupBoundary"] = new(
         FileSource("using System.Text;\n\nusing Alpha;\nusing Spare;\n#if DEBUG\nusing Beta;\n#endif\nusing Zeta;\n"),
         ["using Spare;"]),
      ["RemovalBelowSplitExternAliases"] = new(
         FileSource("extern alias Zulu;\n#if DEBUG\nextern alias Yankee;\n#endif\nusing Zeta;\nusing Spare;\nusing Alpha;\n"),
         ["using Spare;"]),
      ["BlockNamespace"] = new(
         "namespace CoreTestProject.DirectiveSplit\n{\n   using Zeta;\n   using Alpha;\n#if DEBUG\n   using Mike;\n"
            + "   using Kilo;\n#endif\n   using Beta;\n\n   public class SplitTarget\n   {\n      MEMBERS\n   }\n}\n",
         [],
         Crlf: true),
      ["NestedNamespaces"] = new(
         "using Zeta;\nusing Alpha;\n\nnamespace CoreTestProject\n{\n   using Juliet;\n   using Gamma;\n"
            + "#pragma warning disable CS0168\n   using Mike;\n   using Kilo;\n\n   namespace DirectiveSplit\n   {\n"
            + "      using Beta;\n      using Spare;\n#region Usings\n      using System.Text;\n"
            + "      using static Zeta.ZetaStatics;\n#endregion\n\n      public class SplitTarget\n      {\n"
            + "         MEMBERS\n      }\n   }\n}\n",
         ["using Spare;"],
         Written:
         [
            "using Alpha;", "using Zeta;", "using Juliet;", "using Gamma;", "using Mike;", "using Kilo;", "using Beta;",
            "using System.Text;", "using static Zeta.ZetaStatics;",
         ]),
      ["FileScopedNamespace"] = new(
         "namespace CoreTestProject.DirectiveSplit;\n\nusing Zeta;\nusing Alpha;\n#if DEBUG\nusing Mike;\nusing Kilo;\n"
            + "#endif\n\npublic class SplitTarget\n{\n   MEMBERS\n}\n",
         []),
      ["GlobalUsings"] = new(
         FileSource("global using Zeta;\nglobal using Alpha;\n#if DEBUG\nglobal using Mike;\n#endif\nusing Gamma;\nusing Beta;\n"),
         []),
      ["BlankLinesAndComments"] = new(
         FileSource("// Header comment\n\nusing Zeta;\n\n// Alpha comment\nusing Alpha;\n#if DEBUG\n\nusing Beta;\n\n"
            + "// Gamma comment\nusing Gamma;\n\n#endif\n\nusing Kilo;\n"),
         [],
         Crlf: true),
      ["BlankLinesAroundDirectives"] = new(
         FileSource("using Zeta;\n\n\n#if DEBUG\n\nusing Mike;\n#endif\n\n\nusing Alpha;\n"),
         []),
      ["ElseBranch"] = new(
         FileSource("using Zeta;\n#if RELEASE_ONLY\nusing Mike;\n#else\nusing Kilo;\nusing Juliet;\n#endif\nusing Alpha;\n"),
         []),
      ["NestedIf"] = new(
         FileSource("using Zeta;\n#if DEBUG\nusing Mike;\n#if !RELEASE_ONLY\nusing Kilo;\n#endif\nusing Juliet;\n#endif\n"
            + "using Alpha;\n"),
         []),
      ["RunsInOrder"] = new(
         FileSource("using System.Text;\nusing Alpha;\n#if DEBUG\nusing static Alpha.AlphaStatics;\nusing Beta;\n#endif\n"
            + "using Zeta;\nusing ZetaAlias = Zeta.ZetaType;\n"),
         []),
      ["StaticAndAliasAcrossRegion"] = new(
         FileSource("using static Zeta.ZetaStatics;\nusing Zeta;\n#region Usings\nusing AlphaAlias = Alpha.AlphaType;\n"
            + "using Alpha;\n#endregion\n"),
         []),
      ["SystemInRuns"] = new(
         FileSource("using Alpha;\nusing System.Text;\n#if DEBUG\nusing System.Globalization;\nusing Beta;\n#endif\n"),
         []),
      ["UsingsSharingLines"] = new(
         FileSource("using Zeta; using Alpha;\n#if DEBUG\nusing Mike; using Kilo;\n#endif\n"),
         []),
      ["DefineInHeader"] = new(
         FileSource("#define SPLIT\nusing Zeta;\nusing Alpha;\n#if SPLIT\nusing Mike;\n#endif\n"),
         []),
      ["DirectiveInsideUsing"] = new(
         FileSource("using Zeta;\nusing\n#if DEBUG\n#endif\nAlpha;\n"),
         []),
      ["PragmaPairs"] = new(
         FileSource("using Zeta;\n#pragma warning disable CS0168\nusing Alpha;\n#pragma warning restore CS0168\nusing Beta;\n"),
         [],
         Crlf: true),
      ["ExternAliasesAboveSplitUsings"] = new(
         FileSource("extern alias Zulu;\nextern alias Xray;\nusing Zeta;\n#if DEBUG\nusing Mike;\n#endif\n"),
         []),
      ["ExternAliasesOnly"] = new(
         FileSource("extern alias Zulu;\n#if DEBUG\nextern alias Yankee;\n#endif\nextern alias Xray;\n"),
         []),
      ["NamespaceExternAliasesAboveIf"] = new(
         "namespace CoreTestProject.DirectiveSplit\n{\n   extern alias Zulu;\n   extern alias Xray;\n#if DEBUG\n"
            + "   using Zeta;\n   using Alpha;\n#endif\n\n   public class SplitTarget\n   {\n      MEMBERS\n   }\n}\n",
         []),
      ["TabsInNamespace"] = new(
         "namespace CoreTestProject.DirectiveSplit\n{\n\tusing Zeta;\n\tusing Alpha;\n#if DEBUG\n\tusing Mike;\n"
            + "\tusing Kilo;\n#endif\n\n\tpublic class SplitTarget\n\t{\n\t\tMEMBERS\n\t}\n}\n",
         []),
   };

   private static readonly Dictionary<string, AddShape> AddShapes = new()
   {
      ["SortedTargetRun"] = new(
         FileSource("using Alpha;\nusing Zeta;\n#if DEBUG\nusing Mike;\n#endif\n"),
         "Beta",
         FileSource("using Alpha;\nusing Beta;\nusing Zeta;\n#if DEBUG\nusing Mike;\n#endif\n"),
         Crlf: true),
      ["UnsortedTargetRun"] = new(
         FileSource("using Zeta;\nusing Alpha;\n#if DEBUG\nusing Mike;\n#endif\n"),
         "Beta",
         FileSource("using Zeta;\nusing Alpha;\nusing Beta;\n#if DEBUG\nusing Mike;\n#endif\n")),
      ["TargetRunSortedAlphabetically"] = new(
         FileSource("using Alpha;\nusing System.Text;\n#region Usings\nusing Mike;\n#endregion\n"),
         "Beta",
         FileSource("using Alpha;\nusing Beta;\nusing System.Text;\n#region Usings\nusing Mike;\n#endregion\n"),
         PlacedSystemFirst: FileSource("using Alpha;\nusing System.Text;\nusing Beta;\n#region Usings\nusing Mike;\n#endregion\n")),
      ["TargetRunBelowIf"] = new(
         FileSource("#if DEBUG\nusing Mike;\n#endif\nusing Zeta;\nusing Alpha;\n"),
         "Beta",
         FileSource("#if DEBUG\nusing Mike;\n#endif\nusing Zeta;\nusing Alpha;\nusing Beta;\n")),
      ["PragmaAboveFirstUsingBelowExternAlias"] = new(
         FileSource("extern alias Ex;\n\n#pragma warning disable CS0168\nusing Alpha;\nusing Zeta;\n"),
         "Beta",
         FileSource("extern alias Ex;\n\n#pragma warning disable CS0168\nusing Alpha;\nusing Beta;\nusing Zeta;\n")),
      ["IfBetweenExternAliases"] = new(
         FileSource("extern alias Zulu;\n#if DEBUG\nextern alias Yankee;\n#endif\nextern alias Xray;\nusing Zeta;\nusing Alpha;\n"),
         "Beta",
         FileSource("extern alias Zulu;\n#if DEBUG\nextern alias Yankee;\n#endif\nextern alias Xray;\nusing Zeta;\n"
            + "using Alpha;\nusing Beta;\n")),
      ["SortedPlaceStartsRun"] = new(
         FileSource("using Zeta;\n#region Usings\nusing Beta;\nusing Mike;\n#endregion\n"),
         "Alpha",
         FileSource("using Alpha;\nusing Zeta;\n#region Usings\nusing Beta;\nusing Mike;\n#endregion\n")),
      ["SortedPlaceEndsRun"] = new(
         FileSource("using Alpha;\nusing Beta;\n#if DEBUG\nusing Mike;\n#endif\nusing Kilo;\n"),
         "Zeta",
         FileSource("using Alpha;\nusing Beta;\nusing Zeta;\n#if DEBUG\nusing Mike;\n#endif\nusing Kilo;\n")),
      ["SortedPlaceBetweenGroups"] = new(
         FileSource("using System.Text;\nusing Alpha;\n#if DEBUG\nusing Mike;\n#endif\n"),
         "Zeta",
         FileSource("using System.Text;\nusing Alpha;\nusing Zeta;\n#if DEBUG\nusing Mike;\n#endif\n")),
      ["BlockNamespace"] = new(
         "namespace CoreTestProject.DirectiveSplit\n{\n   using Zeta;\n   using Alpha;\n#if DEBUG\n   using Mike;\n#endif\n\n"
            + "   public class SplitTarget\n   {\n   }\n}\n",
         "Beta",
         "namespace CoreTestProject.DirectiveSplit\n{\n   using Zeta;\n   using Alpha;\n   using Beta;\n#if DEBUG\n"
            + "   using Mike;\n#endif\n\n   public class SplitTarget\n   {\n   }\n}\n",
         Crlf: true),
      ["GlobalUsingInTargetRun"] = new(
         FileSource("global using Alpha;\nusing Zeta;\n#if DEBUG\nusing Mike;\n#endif\n"),
         "Beta",
         FileSource("global using Alpha;\nusing Beta;\nusing Zeta;\n#if DEBUG\nusing Mike;\n#endif\n")),
      ["BlankLinesAndCommentsInTargetRun"] = new(
         FileSource("// Header comment\n\nusing Alpha;\n\n// Zeta comment\nusing Zeta;\n#pragma warning disable CS0168\n"
            + "using Mike;\n"),
         "Beta",
         FileSource("// Header comment\n\nusing Alpha;\nusing Beta;\n\n// Zeta comment\nusing Zeta;\n"
            + "#pragma warning disable CS0168\nusing Mike;\n")),
      ["NoRunEnclosesCode"] = new(
         FileSource("#if DEBUG\nusing Zeta;\n#endif\n#if DEBUG\nusing Alpha;\n#endif\n"),
         "Beta",
         FileSource("using Beta;\n#if DEBUG\nusing Zeta;\n#endif\n#if DEBUG\nusing Alpha;\n#endif\n")),
      ["StaticAndAliasInTargetRun"] = new(
         FileSource("using Alpha;\nusing static Alpha.AlphaStatics;\nusing AlphaAlias = Alpha.AlphaType;\n"
            + "#region Usings\nusing Mike;\n#endregion\n"),
         "Beta",
         FileSource("using Alpha;\nusing Beta;\nusing static Alpha.AlphaStatics;\nusing AlphaAlias = Alpha.AlphaType;\n"
            + "#region Usings\nusing Mike;\n#endregion\n")),
      ["TargetRunSharingLine"] = new(
         FileSource("using Alpha; using Zeta;\n#if DEBUG\nusing Mike;\n#endif\n"),
         "Beta",
         FileSource("using Alpha;\nusing Beta;\nusing Zeta;\n#if DEBUG\nusing Mike;\n#endif\n")),
      ["TargetRunBelowHeaderAndIf"] = new(
         FileSource("// Header comment\n#if DEBUG\nusing Zeta;\n#endif\nusing Beta;\nusing Alpha;\n"),
         "Gamma",
         FileSource("// Header comment\n#if DEBUG\nusing Zeta;\n#endif\nusing Beta;\nusing Alpha;\nusing Gamma;\n")),
      ["SortedPlaceBelowHeader"] = new(
         FileSource("// Header comment\n\nusing Zeta;\n#if DEBUG\nusing Mike;\n#endif\n"),
         "Alpha",
         FileSource("// Header comment\n\nusing Alpha;\nusing Zeta;\n#if DEBUG\nusing Mike;\n#endif\n")),
      ["SortedPlaceBelowPragmaBelowExternAlias"] = new(
         FileSource("extern alias Ex;\n\n#pragma warning disable CS0168\nusing Zeta;\n"),
         "Alpha",
         FileSource("extern alias Ex;\n\n#pragma warning disable CS0168\nusing Alpha;\nusing Zeta;\n")),
      ["SortedPlaceBelowBlankLineBelowExternAlias"] = new(
         FileSource("extern alias Ex;\n\nusing Zeta;\n#if DEBUG\nusing Mike;\n#endif\n"),
         "Alpha",
         FileSource("extern alias Ex;\n\nusing Alpha;\nusing Zeta;\n#if DEBUG\nusing Mike;\n#endif\n")),
      ["RegionRunAboveUnsortedRun"] = new(
         FileSource("#region Usings\nusing Mike;\n#endregion\n\nusing Zeta;\n\nusing Alpha;\n"),
         "Beta",
         FileSource("#region Usings\nusing Beta;\nusing Mike;\n#endregion\n\nusing Zeta;\n\nusing Alpha;\n")),
      ["DefineInHeader"] = new(
         FileSource("#define SPLIT\n\nusing Zeta;\nusing Alpha;\n#if SPLIT\nusing Mike;\n#endif\n"),
         "Beta",
         FileSource("#define SPLIT\n\nusing Zeta;\nusing Alpha;\nusing Beta;\n#if SPLIT\nusing Mike;\n#endif\n")),
   };

   public static TheoryData<string> OrganizeShapeNames => [.. OrganizeShapes.Keys];

   public static TheoryData<string> AddShapeNames => [.. AddShapes.Keys];

   [Theory]
   [MemberData(nameof(OrganizeShapeNames))]
   public async Task UsingTools_DirectiveSplit_OrganizeUsings_WritesWhatRoslynWrites(string shapeName)
   {
      var shape = OrganizeShapes[shapeName];
      await _fixture.AssertUnchangedAsync();
      var expected = new StringBuilder();
      var actual = new StringBuilder();
      foreach (var (newLine, folder) in Runs(shape.Crlf))
      {
         var path = _fixture.GetPath($"DirectiveSplit/{folder}/SplitTarget.cs");
         try
         {
            var source = WithMembers(shape.Source, shape.Unused).ReplaceLineEndings(newLine);
            Assert.Equal(path, await _fixture.WriteSourceAsync($"DirectiveSplit/{folder}/SplitTarget.cs", source));
            var roslynWrites = await OrganizeImportsAsync(path, WithoutLines(source, shape.Unused, newLine));
            var directives = shape.Written ?? [.. Directives(source).Where(d => !shape.Unused.Contains(d))];

            var result = await new OrganizeUsingsTool(WorkspaceManager).OrganizeUsingsAsync(
               new FileScope { Path = path }, preview: false, cancellationToken: CT);

            Assert.True(result.Data.Applied);
            var written = await File.ReadAllTextAsync(path, CT);
            var label = Label(folder, newLine);
            expected.Append(OrganizeReport(
               label, roslynWrites == source ? 0 : 1, shape.Unused.Length, roslynWrites, directives));
            actual.Append(OrganizeReport(
               label, result.Data.FilesChanged, result.Data.TotalUsingsRemoved, written, Directives(written)));
         }
         finally
         {
            await _fixture.RemoveSourceAsync(path);
         }
      }

      Assert.Equal(expected.ToString(), actual.ToString());
      await _fixture.AssertUnchangedAsync();
   }

   /// <summary>
   /// Roslyn's Organize Imports lays out the source with the new using at the place the tool picks, so it adds the
   /// blank lines between groups that the tool adds.
   /// </summary>
   [Theory]
   [MemberData(nameof(AddShapeNames))]
   public async Task UsingTools_DirectiveSplit_AddUsing_KeepsOrderAsRoslynOrganizeImports(string shapeName)
   {
      var shape = AddShapes[shapeName];
      var newUsing = $"using {shape.Namespace};";
      var expected = new StringBuilder();
      var actual = new StringBuilder();
      await _fixture.AssertUnchangedAsync();
      foreach (var (newLine, folder) in Runs(shape.Crlf))
      {
         var path = _fixture.GetPath($"DirectiveSplit/{folder}/SplitTarget.cs");
         try
         {
            var source = WithoutMembers(shape.Source).ReplaceLineEndings(newLine);
            var placed = WithoutMembers(
               Combinations.Single(c => c.Folder == folder).SortSystemFirst
                  ? shape.PlacedSystemFirst ?? shape.Placed
                  : shape.Placed).ReplaceLineEndings(newLine);
            Assert.Equal(path, await _fixture.WriteSourceAsync($"DirectiveSplit/{folder}/SplitTarget.cs", source));
            var roslynWrites = await OrganizeImportsAsync(path, placed);

            var result = await new AddUsingTool(WorkspaceManager).AddUsingAsync(
               path, shape.Namespace, preview: false, CT);

            Assert.True(result.Data.Added);
            Assert.True(result.Data.Applied);
            var written = await File.ReadAllTextAsync(path, CT);
            var label = Label(folder, newLine);
            expected.Append(AddReport(label, roslynWrites, Directives(source), roslynWrites));
            actual.Append(AddReport(
               label,
               written,
               [.. Directives(written).Where(d => d != newUsing)],
               await OrganizeImportsAsync(path, written)));
         }
         finally
         {
            await _fixture.RemoveSourceAsync(path);
         }
      }

      Assert.Equal(expected.ToString(), actual.ToString());
      await _fixture.AssertUnchangedAsync();
   }

   private static IEnumerable<(string NewLine, string Folder)> Runs(bool crlf) =>
      from newLine in crlf ? ["\n", "\r\n"] : new[] { "\n" }
      from combination in Combinations
      select (newLine, combination.Folder);

   private static string Label(string folder, string newLine) => $"== {folder}, {(newLine == "\n" ? "LF" : "CRLF")} ==\n";

   private static string OrganizeReport(
      string label,
      int filesChanged,
      int usingsRemoved,
      string text,
      IEnumerable<string> directives) =>
      $"{label}files changed: {filesChanged}, usings removed: {usingsRemoved}\n"
      + $"directives: {string.Join(" | ", directives)}\nparse diagnostics: {ParseDiagnostics(text)}\n{text}\n";

   private static string AddReport(string label, string text, IEnumerable<string> existingDirectives, string roslynWrites) =>
      $"{label}existing directives: {string.Join(" | ", existingDirectives)}\nparse diagnostics: {ParseDiagnostics(text)}\n"
      + $"Organize Imports keeps it: {roslynWrites == text}\n{text}\n";

   private static string FileSource(string usings) => usings + """

      namespace CoreTestProject.DirectiveSplit;

      public class SplitTarget
      {
         MEMBERS
      }

      """;

   /// <summary>
   /// Replaces MEMBERS with a member for each active using that is not in <paramref name="unused"/>.
   /// </summary>
   private static string WithMembers(string source, string[] unused)
   {
      var members = new List<string>();
      foreach (var usingDirective in ParseWithDebug(source).GetRoot().DescendantNodes().OfType<UsingDirectiveSyntax>())
      {
         if (unused.Contains(Text(usingDirective)))
            continue;

         var name = usingDirective.NamespaceOrType.ToString();
         var key = usingDirective.Alias is { } alias ? $"{alias.Name} = {name}"
            : usingDirective.StaticKeyword.IsKind(SyntaxKind.None) ? name
            : $"static {name}";
         Assert.True(MemberOf.TryGetValue(key, out var member), $"No member uses '{key}'");
         members.Add(member);
      }

      return source.Replace(Members, string.Join(" ", members.Distinct()), StringComparison.Ordinal);
   }

   private static string WithoutMembers(string source) => source.Replace(Members, "", StringComparison.Ordinal);

   private static string WithoutLines(string text, string[] lines, string newLine) =>
      string.Join(newLine, text.Split(newLine).Where(l => !lines.Contains(l.Trim())));

   /// <summary>
   /// Returns the text of each active extern alias and using of the text, in order, with line breaks as \n.
   /// </summary>
   private static List<string> Directives(string text) =>
      [.. ParseWithDebug(text).GetRoot().DescendantNodes()
         .Where(n => n is UsingDirectiveSyntax or ExternAliasDirectiveSyntax)
         .Select(Text)];

   private static string Text(SyntaxNode node) => node.ToString().ReplaceLineEndings("\n");

   private static SyntaxTree ParseWithDebug(string text) =>
      CSharpSyntaxTree.ParseText(text, DebugParseOptions, cancellationToken: TestContext.Current.CancellationToken);

   private static string ParseDiagnostics(string text) =>
      string.Join(" | ", CSharpSyntaxTree.ParseText(text, cancellationToken: TestContext.Current.CancellationToken)
         .GetDiagnostics()
         .Select(d => d.ToString()));

   private async Task<string> OrganizeImportsAsync(string path, string text)
   {
      var document = await WorkspaceManager.GetDocumentAsync(path, CT);
      var organized = await Formatter.OrganizeImportsAsync(document.WithText(SourceText.From(text)), CT);
      return (await organized.GetTextAsync(CT)).ToString();
   }
}
