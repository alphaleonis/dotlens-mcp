using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tests.Helpers;
using DotLens.Tools.Analysis;
using DotLens.Tools.Intelligence;
using DotLens.Tools.Navigation;
using DotLens.Tools.Refactoring;
using DotLens.Tools.Search;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DotLens.Tests.Integration;

/// <summary>
/// Covers DotLens tools on a <c>net11.0</c> project that uses C# 15 unions, labeled <c>break</c> and
/// <c>continue</c>, and extension blocks, plus type declarations with primary constructors.
/// </summary>
public class CSharp15FixtureTests : IClassFixture<CSharp15FixtureWorkspace>
{
   private readonly CSharp15FixtureWorkspace _fixture;

   public CSharp15FixtureTests(CSharp15FixtureWorkspace fixture)
   {
      _fixture = fixture;
   }

   private static CancellationToken CT => TestContext.Current.CancellationToken;

   [Fact]
   public async Task Diagnostics_ReportsNoErrors()
   {
      // A load that missed the source files would report no errors too.
      var sourceFiles = _fixture.Workspace.EnsureSolutionLoaded().Projects
         .SelectMany(p => p.Documents)
         .Select(d => d.FilePath)
         .OfType<string>()
         .Where(path => !path.StartsWith(_fixture.GetPath("obj/"), StringComparison.Ordinal))
         .Select(Path.GetFileName);
      Assert.Equivalent(new[] { "Unions.cs", "LabeledLoops.cs", "ExtensionIndexer.cs", "ExtensionMembers.cs", "ExtensionBlockHeaders.cs", "MultiLineSignatures.cs", "RefReceiverExtensions.cs", "PrimaryConstructors.cs", "IndexerUses.cs", "IndexerWrites.cs", "AmbiguousMembers.cs", "EnumsAndDelegates.cs", "DelegateSignatures.cs", "InheritedMembers.cs", "AccessorNames.cs", "ExtensionContainers.cs", "ImplicitTypeUses.cs", "SelfReferences.cs", "ModuleInitializers.cs", "SelfReferencingMembers.cs", "AttributedFields.cs", "NullConditionalIndexers.cs", "DeclarationFidelity.cs", "OutlineFidelity.cs", "PartialBaseLists.cs" }, sourceFiles, strict: true);

      var tool = new GetDiagnosticsTool(_fixture.Workspace);
      var result = await tool.GetDiagnosticsAsync(severity: DiagnosticSeverityDto.Error, cancellationToken: CT);

      Assert.Empty(result.Data.Diagnostics.Select(d => $"{d.Id} at {d.Location}: {d.Message}"));
      Assert.Equal(0, result.Data.ErrorCount);
   }

   [Fact]
   public async Task FileOutline_IncludesUnion()
   {
      var tool = new GetFileOutlineTool(_fixture.Workspace);

      var result = await tool.GetFileOutlineAsync(_fixture.GetPath("Unions.cs"), ct: CT);

      Assert.Equal(
         """
             1: namespace CSharp15TestProject;

             3: public record Circle(double Radius);
             5: public record Square(double Side);
             7: public union Shape(Circle, Square);
             9: public static class ShapeMath
            10: {
            11:    public static double Area(Shape shape);
            16: }
         """.ReplaceLineEndings(),
         result.Data.Outline);
   }

   [Fact]
   public async Task FileOutline_ShowsPrimaryConstructorsWithAndWithoutBody()
   {
      var tool = new GetFileOutlineTool(_fixture.Workspace);

      var result = await tool.GetFileOutlineAsync(_fixture.GetPath("PrimaryConstructors.cs"), ct: CT);

      Assert.Equal(
         """
             1: namespace CSharp15TestProject;

             3: public record Point(int X, int Y);
             5: public class Logger(string name);
             7: public struct Size(int width, int height)
             8: {
             9:    public int Area => ...;
            10: }
            12: public record struct Pair(int Left, int Right);
            14: public class Counter(int start) : Tally(start);
            16: public class Tally(int initial)
            17: {
            18:    public int Initial { get; }
            19: }
            21: public interface IMeasurable
            22: {
            23:    int Measure();
            24: }
         """.ReplaceLineEndings(),
         result.Data.Outline);
   }

   [Fact]
   public async Task FileOutline_ExtensionBlock_NestsMembersUnderHeader()
   {
      var tool = new GetFileOutlineTool(_fixture.Workspace);

      var result = await tool.GetFileOutlineAsync(_fixture.GetPath("ExtensionMembers.cs"), ct: CT);

      Assert.Equal(
         """
             1: namespace CSharp15TestProject;

             3: public static class TextExtensions
             4: {
             5:    extension(string text)
             6:    {
             7:       public bool IsShout => ...;
             9:       public string Whisper();
            11:       public static string Blank();
            12:    }
            14:    extension(int number)
            15:    {
            16:       public bool IsEven => ...;
            18:       public int Doubled();
            19:    }
            21:    extension<T>(IEnumerable<T> items)
            22:    {
            23:       public bool IsEmpty => ...;
            24:    }
            25: }
            27: public static class TextUsage
            28: {
            29:    public static string Quiet(string text);
            30: }
         """.ReplaceLineEndings(),
         result.Data.Outline);
   }

   [Fact]
   public async Task FileOutline_ExtensionBlock_ShowsReceiverModifiersAndConstraints()
   {
      var tool = new GetFileOutlineTool(_fixture.Workspace);

      var result = await tool.GetFileOutlineAsync(_fixture.GetPath("RefReceiverExtensions.cs"), ct: CT);

      Assert.Equal(
         """
             1: namespace CSharp15TestProject;

             3: public struct Meter
             4: {
             5:    public int Reading;
             6: }
             8: public static class MeterExtensions
             9: {
            10:    extension(Meter meter)
            11:    {
            12:       public int Copy();
            13:    }
            15:    extension(ref Meter meter)
            16:    {
            17:       public void Advance();
            18:    }
            20:    extension(in Meter meter)
            21:    {
            22:       public int Peek();
            23:    }
            25:    extension(ref readonly Meter meter)
            26:    {
            27:       public int Look();
            28:    }
            30:    extension(scoped ref Meter meter)
            31:    {
            32:       public void Reset();
            33:    }
            35:    extension(scoped Span<int> span)
            36:    {
            37:       public int Size();
            38:    }
            40:    extension<T>(ref T value) where T : struct
            41:    {
            42:       public void Clear();
            43:    }
            44: }
         """.ReplaceLineEndings(),
         result.Data.Outline);
   }

   [Fact]
   public async Task FileOutline_ExtensionBlock_ShowsExpressionBodiedIndexer()
   {
      var tool = new GetFileOutlineTool(_fixture.Workspace);

      var result = await tool.GetFileOutlineAsync(_fixture.GetPath("ExtensionIndexer.cs"), ct: CT);

      Assert.Equal(
         """
             1: namespace CSharp15TestProject;

             3: public sealed class Grid(int[,] cells)
             4: {
             5:    public int[,] Cells { get; }
             6: }
             8: public static class GridExtensions
             9: {
            10:    extension(Grid grid)
            11:    {
            12:       public int this[int row, int column] => ...;
            13:    }
            14: }
            16: public static class GridUsage
            17: {
            18:    public static int SumThreeCorners(Grid grid);
            25: }
         """.ReplaceLineEndings(),
         result.Data.Outline);
   }

   [Fact]
   public async Task FileOutline_ExtensionBlock_ShowsReceiverlessAndMultiConstraintHeaders()
   {
      var tool = new GetFileOutlineTool(_fixture.Workspace);

      var result = await tool.GetFileOutlineAsync(_fixture.GetPath("ExtensionBlockHeaders.cs"), ct: CT);

      Assert.Equal(
         """
             1: namespace CSharp15TestProject;

             3: public static class CatalogExtensions
             4: {
             8:    extension(Catalog)
             9:    {
            10:       public static Catalog Empty();
            11:    }
            16:    extension<TKey, TValue>(IDictionary<TKey, TValue> map) where TKey : notnull where TValue : class, new()
            19:    {
            20:       public TValue GetOrCreate(TKey key);
            22:       private int Spare => ...;
            23:    }
            24: }
         """.ReplaceLineEndings(),
         result.Data.Outline);
   }

   [Fact]
   public async Task FileOutline_ExtensionBlock_ShowsDocCommentAndOmitsPrivateMembers()
   {
      var tool = new GetFileOutlineTool(_fixture.Workspace);

      var result = await tool.GetFileOutlineAsync(
         _fixture.GetPath("ExtensionBlockHeaders.cs"), includePrivate: false, includeDocComments: true, ct: CT);

      Assert.Equal(
         """
             1: namespace CSharp15TestProject;

             3: public static class CatalogExtensions
             4: {
              :    /// <summary>
              :    /// Factories for <see cref="Catalog"/>.
              :    /// </summary>
             8:    extension(Catalog)
             9:    {
            10:       public static Catalog Empty();
            11:    }
              :    /**
              :     * Lookups that fill in missing values.
              :     */
            16:    extension<TKey, TValue>(IDictionary<TKey, TValue> map) where TKey : notnull where TValue : class, new()
            19:    {
            20:       public TValue GetOrCreate(TKey key);
            23:    }
            24: }
         """.ReplaceLineEndings(),
         result.Data.Outline);
   }

   [Fact]
   public async Task FileOutline_MultiLineSignatures_PrintsEachSignatureOnOneLine()
   {
      var tool = new GetFileOutlineTool(_fixture.Workspace);

      var result = await tool.GetFileOutlineAsync(_fixture.GetPath("MultiLineSignatures.cs"), ct: CT);

      Assert.Equal(
         """
             1: namespace CSharp15TestProject;

             3: public interface ILedger
             4: {
             5:    int Count { get; }
             6: }
             8: public class Ledger<TKey, TValue>(IDictionary<TKey, TValue> entries, string title = "Ledger  one") : ILedger, IDisposable where TKey : notnull, IComparable<TKey>
            16: {
            17:    private int _disposals;
            19:    private event EventHandler? Disposed;
            21:    public Ledger(string name, bool strict = 1 < 2) : this(...);
            30:    public int Count => ...;
            32:    public Dictionary<TResult, List<TValue>> Group<TResult>(Func<TValue, TResult> selector, int limit) where TResult : notnull;
            41:    public TValue this[TKey key] => ...;
            44:    public void Dispose();
            49: }
            51: public delegate TResult Folder<in TItem, TResult>(TResult seed, TItem item) where TResult : struct;
            57: public static class LedgerExtensions
            58: {
            59:    extension<TKey, TValue>(Ledger<TKey, TValue> ledger) where TKey : notnull, IComparable<TKey>
            62:    {
            63:       public bool IsEmpty => ...;
            64:    }
            65: }
         """.ReplaceLineEndings(),
         result.Data.Outline);
   }

   [Fact]
   public async Task FileOutline_MultiLineSignatures_OmitsPrivateFieldAndEventField()
   {
      var tool = new GetFileOutlineTool(_fixture.Workspace);

      var result = await tool.GetFileOutlineAsync(_fixture.GetPath("MultiLineSignatures.cs"), includePrivate: false, ct: CT);

      Assert.Equal(
         """
             1: namespace CSharp15TestProject;

             3: public interface ILedger
             4: {
             5:    int Count { get; }
             6: }
             8: public class Ledger<TKey, TValue>(IDictionary<TKey, TValue> entries, string title = "Ledger  one") : ILedger, IDisposable where TKey : notnull, IComparable<TKey>
            16: {
            21:    public Ledger(string name, bool strict = 1 < 2) : this(...);
            30:    public int Count => ...;
            32:    public Dictionary<TResult, List<TValue>> Group<TResult>(Func<TValue, TResult> selector, int limit) where TResult : notnull;
            41:    public TValue this[TKey key] => ...;
            44:    public void Dispose();
            49: }
            51: public delegate TResult Folder<in TItem, TResult>(TResult seed, TItem item) where TResult : struct;
            57: public static class LedgerExtensions
            58: {
            59:    extension<TKey, TValue>(Ledger<TKey, TValue> ledger) where TKey : notnull, IComparable<TKey>
            62:    {
            63:       public bool IsEmpty => ...;
            64:    }
            65: }
         """.ReplaceLineEndings(),
         result.Data.Outline);
   }

   [Fact]
   public async Task FileOutline_DeclarationFidelity_PrintsEachDeclarationAsWritten()
   {
      var tool = new GetFileOutlineTool(_fixture.Workspace);
      var path = _fixture.GetPath("OutlineFidelity.cs");
      // A string written across lines holds the line break of the checked-out file.
      var newLine = (await File.ReadAllTextAsync(path, CT)).Contains("\r\n", StringComparison.Ordinal) ? @"\r\n" : @"\n";

      var result = await tool.GetFileOutlineAsync(path, includeDocComments: true, ct: CT);

      Assert.Equal(
         $$$$"""
             1: namespace CSharp15TestProject;

             3: public interface IDial
             4: {
             5:    int Read();
             7:    int Level { get; }
             9:    event EventHandler? Changed;
            11:    int this[int index] { get; }
            12: }
            14: public interface IScale
            15: {
            16:    int Read();
            17: }
            19: public interface ISummable<TSelf> where TSelf : ISummable<TSelf>
            20: {
            21:    static abstract TSelf operator +(TSelf left, TSelf right);
            23:    static abstract explicit operator int(TSelf value);
            24: }
            26: public sealed class Dial : IDial, IScale, ISummable<Dial>
            27: {
            28:    int IDial.Read();
            30:    int IScale.Read();
            32:    int IDial.Level => ...;
            34:    event EventHandler? IDial.Changed { add; remove; }
            36:    int IDial.this[int index] => ...;
            38:    static Dial ISummable<Dial>.operator +(Dial left, Dial right);
            40:    static explicit ISummable<Dial>.operator int(Dial value);
            41: }
            43: public readonly record struct Credit(decimal Amount)
            44: {
            45:    public static Credit operator -(Credit value);
            47:    public static Credit operator checked -(Credit value);
            49:    public static explicit operator int(Credit value);
            51:    public static explicit operator checked int(Credit value);
            52: }
            54: public record class Receipt(string Number);
            56: public unsafe struct Packet
            57: {
            58:    public fixed int Data[4];
            60:    public fixed byte Header[2], Footer[8];
            61: }
            64: public class Archived
            65: {
            66:    private const string Unit;
              :    /// <summary>Keeps the archive.</summary>
              :    /// <summary>Saves the archive.</summary>
            71:    public void Save();
            73:    public void Store([System.ComponentModel.Description("first{{{{newLine}}}}second \"quoted\"")] string note = "line one{{{{newLine}}}}  line two");
            79:    public void Measure(string label = $"{Unit} wide", string verbatim = $"{Unit}{{{{newLine}}}}{{tall}}");
            84:    public void Label(string text = $"{{{Unit}}}{{{{newLine}}}}{{{{newLine}}}}  {{x}}");
            89: }
            91: public enum Tone
            92: {
            94:    Low,
              :    /// <summary>The upper tone.</summary>
            96:    High,
            97: }
         """.ReplaceLineEndings(),
         result.Data.Outline);
   }

   [Fact]
   public async Task TypeOverview_UnionDoesNotThrow()
   {
      var tool = new GetTypeOverviewTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.GetTypeOverviewAsync(new LookupByName { Name = "Shape" }, cancellationToken: CT);

      var data = result.Data;
      Assert.Equal("Shape", data.Name);
      Assert.Equal("CSharp15TestProject.Shape", data.FullyQualifiedName);
      Assert.Equal("Struct", data.TypeKind);
      Assert.Equal("Public", data.Accessibility);
      Assert.False(data.IsAbstract);
      Assert.True(data.IsSealed);
      Assert.False(data.IsStatic);
      Assert.False(data.IsRecord);
      Assert.Equal(["System.ValueType"], data.BaseTypes);
      Assert.Equal(["System.Runtime.CompilerServices.IUnion"], data.Interfaces);
      // The compiler synthesizes the Value property, one constructor per case type, and the default struct constructor.
      Assert.Equal(new TypeMemberCounts { Methods = 0, Properties = 1, Fields = 0, Events = 0, Constructors = 3 }, data.MemberCounts);
      Assert.Equal(new TypeLocationInfo { FilePath = "Unions.cs", Line = 7 }, data.Location);
      Assert.False(data.IsMetadataType);
      Assert.Null(data.Documentation);

      var byPosition = await tool.GetTypeOverviewAsync(
         new LookupByPosition { FilePath = _fixture.GetPath("Unions.cs"), Line = 7, Column = 14 },
         cancellationToken: CT);
      Assert.Equivalent(data, byPosition.Data, strict: true);
   }

   [Fact]
   public async Task ControlFlow_LabeledBreak()
   {
      var tool = new AnalyzeControlFlowTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));
      var path = _fixture.GetPath("LabeledLoops.cs");

      var method = await tool.AnalyzeControlFlowAsync(
         new ControlFlowTargetByMethod { Method = new LookupByName { Name = "LabeledLoops.FindNegativeRow" } }, CT);
      AssertControlFlow(
         method.Data,
         region: (43, 55),
         exitPoints: [new ControlFlowPoint { Line = 55, Kind = "ReturnStatement" }],
         returnStatements: [new ControlFlowPoint { Line = 55, Kind = "ReturnStatement", Text = "return i;" }],
         endPointIsReachable: false);

      var returnStatement = await tool.AnalyzeControlFlowAsync(
         new ControlFlowTargetByLines { FilePath = path, StartLine = 55, EndLine = 55 }, CT);
      AssertControlFlow(
         returnStatement.Data,
         region: (55, 55),
         exitPoints: [new ControlFlowPoint { Line = 55, Kind = "ReturnStatement" }],
         returnStatements: [new ControlFlowPoint { Line = 55, Kind = "ReturnStatement", Text = "return i;" }],
         endPointIsReachable: false,
         startPointIsReachable: true); // Only break outer reaches the return.

      var outerLoop = await tool.AnalyzeControlFlowAsync(
         new ControlFlowTargetByLines { FilePath = path, StartLine = 44, EndLine = 53 }, CT);
      AssertControlFlow(outerLoop.Data, region: (44, 53), exitPoints: [], returnStatements: [], endPointIsReachable: true);

      // The inner while (true) has no break of its own, so its end is unreachable.
      var innerWhile = await tool.AnalyzeControlFlowAsync(
         new ControlFlowTargetByLines { FilePath = path, StartLine = 47, EndLine = 52 }, CT);
      AssertControlFlow(
         innerWhile.Data,
         region: (47, 52),
         exitPoints: [new ControlFlowPoint { Line = 50, Kind = "BreakStatement" }],
         returnStatements: [],
         endPointIsReachable: false);

      var innerFor = await tool.AnalyzeControlFlowAsync(
         new ControlFlowTargetByLines { FilePath = path, StartLine = 11, EndLine = 15 }, CT);
      AssertControlFlow(
         innerFor.Data,
         region: (11, 15),
         exitPoints: [new ControlFlowPoint { Line = 14, Kind = "BreakStatement" }],
         returnStatements: [],
         endPointIsReachable: true);
   }

   [Fact]
   public async Task ControlFlow_LabeledContinue()
   {
      var tool = new AnalyzeControlFlowTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));
      var path = _fixture.GetPath("LabeledLoops.cs");

      var innerFor = await tool.AnalyzeControlFlowAsync(
         new ControlFlowTargetByLines { FilePath = path, StartLine = 29, EndLine = 33 }, CT);
      AssertControlFlow(
         innerFor.Data,
         region: (29, 33),
         exitPoints: [new ControlFlowPoint { Line = 32, Kind = "ContinueStatement" }],
         returnStatements: [],
         endPointIsReachable: true);

      var outerLoop = await tool.AnalyzeControlFlowAsync(
         new ControlFlowTargetByLines { FilePath = path, StartLine = 26, EndLine = 36 }, CT);
      AssertControlFlow(outerLoop.Data, region: (26, 36), exitPoints: [], returnStatements: [], endPointIsReachable: true);
   }

   private static void AssertControlFlow(
      ControlFlowResponse data,
      (int Start, int End) region,
      ControlFlowPoint[] exitPoints,
      ControlFlowPoint[] returnStatements,
      bool endPointIsReachable,
      bool startPointIsReachable = true)
   {
      Assert.Equal(new LineRange { StartLine = region.Start, EndLine = region.End }, data.Region);
      Assert.Empty(data.EntryPoints);
      Assert.Equal(exitPoints, data.ExitPoints);
      Assert.Equal(returnStatements, data.ReturnStatements);
      Assert.Equal(endPointIsReachable, data.EndPointIsReachable);
      Assert.Equal(startPointIsReachable, data.StartPointIsReachable);
   }


   [Fact]
   public async Task FindReferences_ExtensionIndexer()
   {
      var tool = new FindReferencesTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));
      ReferenceInfo[] expected =
      [
         new() { FilePath = "ExtensionIndexer.cs", Line = 20, Column = 25, Kind = ReferenceKind.Read },
         new() { FilePath = "ExtensionIndexer.cs", Line = 21, Column = 26, Kind = ReferenceKind.Read },
         new() { FilePath = "ExtensionIndexer.cs", Line = 22, Column = 28, Kind = ReferenceKind.Read },
      ];

      SymbolLookup[] lookups =
      [
         new LookupByPosition { FilePath = _fixture.GetPath("ExtensionIndexer.cs"), Line = 12, Column = 18 },
         new LookupByName { Name = "GridExtensions.this[]" },
      ];
      foreach (var lookup in lookups)
      {
         var result = await tool.FindReferencesAsync(lookup, cancellationToken: CT);

         Assert.Equal("this[]", result.Data.Symbol.Name);
         Assert.Equal("CSharp15TestProject.GridExtensions.extension(CSharp15TestProject.Grid)", result.Data.Symbol.ContainingType);
         Assert.Equal(3, result.Data.TotalReferences);
         Assert.Equal(expected, result.Data.References);
      }
   }

   [Fact]
   public async Task FindReferences_IndexerWrite_SettableIndexer()
   {
      await AssertIndexerWritesReferencesAsync(
         "Slots.this[]",
         (53, 23, ReferenceKind.Read),
         (54, 12, ReferenceKind.Write),
         (55, 12, ReferenceKind.Write),
         (56, 12, ReferenceKind.Write),
         (57, 12, ReferenceKind.Write),
         (58, 12, ReferenceKind.Write),
         (59, 14, ReferenceKind.Write),
         (60, 14, ReferenceKind.Write),
         (61, 13, ReferenceKind.Write),
         (61, 23, ReferenceKind.Write),
         (62, 14, ReferenceKind.Write),
         (62, 31, ReferenceKind.Write),
         (62, 48, ReferenceKind.Read),
         (63, 13, ReferenceKind.Write),
         (64, 31, ReferenceKind.Write),
         (70, 55, ReferenceKind.Write),
         (70, 76, ReferenceKind.Write));
   }

   [Fact]
   public async Task FindReferences_IndexerWrite_ExtensionIndexer()
   {
      await AssertIndexerWritesReferencesAsync(
         "BoardExtensions.this[]",
         (82, 23, ReferenceKind.Read),
         (83, 12, ReferenceKind.Write),
         (84, 12, ReferenceKind.Write),
         (85, 12, ReferenceKind.Write),
         (86, 14, ReferenceKind.Write),
         (87, 13, ReferenceKind.Write),
         (87, 26, ReferenceKind.Write));
   }

   [Fact]
   public async Task FindReferences_IndexerWrite_RefReturningIndexerAsArgument()
   {
      await AssertIndexerWritesReferencesAsync(
         "RefSlots.this[]",
         (74, 12, ReferenceKind.Write),
         (75, 21, ReferenceKind.Write),
         (76, 21, ReferenceKind.Write),
         (77, 27, ReferenceKind.Read));
   }

   /// <summary>
   /// The outer element initializer <c>[0] = { [1] = 4 }</c> gets the shelf row and sets an element of it.
   /// </summary>
   [Fact]
   public async Task FindReferences_IndexerWrite_NestedInitializerTargetIsRead()
   {
      await AssertIndexerWritesReferencesAsync("Shelf.this[]", (70, 47, ReferenceKind.Read));
   }

   [Fact]
   public async Task FindReferences_PropertyAssignmentTargets_AreWrites()
   {
      await AssertIndexerWritesReferencesAsync(
         "Slots.Count",
         (64, 40, ReferenceKind.Write),
         (65, 14, ReferenceKind.Write),
         (65, 27, ReferenceKind.Write),
         (66, 14, ReferenceKind.Write));
   }

   [Fact]
   public async Task FindReferences_PropertyNestedInitializerTarget_IsRead()
   {
      await AssertIndexerWritesReferencesAsync("Shelf.Front", (70, 66, ReferenceKind.Read));
   }

   [Fact]
   public async Task FindReferences_BareLocalDeconstructionTarget_IsWrite()
   {
      await AssertIndexerWritesReferencesAsync(
         IndexerWritesPosition(53, 11),
         (62, 19, ReferenceKind.Write),
         (67, 14, ReferenceKind.Read));
   }

   [Fact]
   public async Task FindReferences_BareLocalArguments_AreWritesOnlyByRefOrOut()
   {
      await AssertIndexerWritesReferencesAsync(
         IndexerWritesPosition(104, 11),
         (105, 34, ReferenceKind.Read),
         (106, 8, ReferenceKind.Write),
         (107, 16, ReferenceKind.Write),
         (107, 27, ReferenceKind.Write),
         (108, 19, ReferenceKind.Read));
   }

   [Fact]
   public async Task FindReferences_BarePropertyDeconstructionTarget_IsWrite()
   {
      await AssertIndexerWritesReferencesAsync(
         "Ledger.Total",
         (105, 8, ReferenceKind.Write),
         (105, 21, ReferenceKind.Write),
         (106, 15, ReferenceKind.Write),
         (106, 25, ReferenceKind.Read));
   }

   private LookupByPosition IndexerWritesPosition(int line, int column) =>
      new() { FilePath = _fixture.GetPath("IndexerWrites.cs"), Line = line, Column = column };

   private Task AssertIndexerWritesReferencesAsync(string name, params (int Line, int Column, ReferenceKind Kind)[] expected) =>
      AssertReferencesAsync("IndexerWrites.cs", new LookupByName { Name = name }, expected);

   private Task AssertIndexerWritesReferencesAsync(SymbolLookup lookup, params (int Line, int Column, ReferenceKind Kind)[] expected) =>
      AssertReferencesAsync("IndexerWrites.cs", lookup, expected);

   private async Task AssertReferencesAsync(string file, SymbolLookup lookup, params (int Line, int Column, ReferenceKind Kind)[] expected)
   {
      var tool = new FindReferencesTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.FindReferencesAsync(lookup, cancellationToken: CT);

      var references = result.Data.References ?? throw new InvalidOperationException("Compact verbosity lists references");
      Assert.Equal(
         expected.Select(e => new ReferenceInfo { FilePath = file, Line = e.Line, Column = e.Column, Kind = e.Kind }),
         references.OrderBy(r => r.Line).ThenBy(r => r.Column));
      Assert.Equal(expected.Length, result.Data.TotalReferences);
   }

   [Fact]
   public async Task FindReferences_NullConditionalIndexerAssignment_IsWrite()
   {
      await AssertNullConditionalReferencesAsync(
         "Locker.this[]",
         (79, 14, ReferenceKind.Write),
         (80, 14, ReferenceKind.Write),
         (81, 14, ReferenceKind.Write),
         (82, 20, ReferenceKind.Write),
         (83, 15, ReferenceKind.Write),
         (84, 24, ReferenceKind.Write),
         (85, 20, ReferenceKind.Write),
         (86, 29, ReferenceKind.Read),
         (87, 25, ReferenceKind.Read),
         (88, 26, ReferenceKind.Read),
         (93, 25, ReferenceKind.Read));
   }

   /// <summary>
   /// Line 88 is <c>ward?[1][2]</c> and line 89 is <c>ward?.Self[1]</c>, where the conditional access starts with
   /// a binding that is not the whole access.
   /// </summary>
   [Fact]
   public async Task FindReferences_NullConditionalIndexer_EachBindingReportsItsOwnIndexer()
   {
      await AssertNullConditionalReferencesAsync(
         "Ward.this[]",
         (83, 12, ReferenceKind.Read),
         (84, 20, ReferenceKind.Read),
         (85, 17, ReferenceKind.Read),
         (88, 23, ReferenceKind.Read),
         (89, 27, ReferenceKind.Read));
   }

   [Fact]
   public async Task FindReferences_NullConditionalIndexerAssignment_GenericIndexer()
   {
      await AssertNullConditionalReferencesAsync("Crate.this[]", (90, 13, ReferenceKind.Write));
   }

   [Fact]
   public async Task FindReferences_NullConditionalIndexerAssignment_ExtensionIndexer()
   {
      await AssertNullConditionalReferencesAsync(
         "WardExtensions.this[]",
         (91, 12, ReferenceKind.Write),
         (92, 24, ReferenceKind.Read));
   }

   /// <summary>
   /// Find-references cascades from an implementation through the interface to the sibling implementation, for a
   /// null-conditional use as for a direct one.
   /// </summary>
   [Theory]
   [InlineData("IStall.this[]")]
   [InlineData("Stall.this[]")]
   public async Task FindReferences_NullConditionalIndexerAssignment_CascadesThroughInterface(string name)
   {
      await AssertNullConditionalReferencesAsync(
         name,
         (99, 13, ReferenceKind.Write),
         (100, 13, ReferenceKind.Write),
         (101, 13, ReferenceKind.Write),
         (106, 12, ReferenceKind.Write),
         (107, 12, ReferenceKind.Write),
         (108, 12, ReferenceKind.Write));
   }

   [Fact]
   public async Task FindCallers_NullConditionalIndexerAssignment()
   {
      await AssertNullConditionalCallersAsync(
         "Locker.this[]",
         ("Use", 79, 14),
         ("Use", 80, 14),
         ("Use", 81, 14),
         ("Use", 82, 20),
         ("Use", 83, 15),
         ("Use", 84, 24),
         ("Use", 85, 20),
         ("Use", 86, 29),
         ("Use", 87, 25),
         ("Use", 88, 26),
         ("Use", 93, 25));
   }

   [Fact]
   public async Task FindCallers_NullConditionalIndexerAssignment_Setter()
   {
      await AssertNullConditionalCallersAsync(
         "Locker.set_Item",
         ("Use", 79, 14),
         ("Use", 80, 14),
         ("Use", 81, 14),
         ("Use", 82, 20),
         ("Use", 83, 15),
         ("Use", 84, 24),
         ("Use", 85, 20));
   }

   [Fact]
   public async Task FindCallers_NullConditionalIndexerAssignment_Getter()
   {
      await AssertNullConditionalCallersAsync(
         "Locker.get_Item",
         ("Use", 80, 14),
         ("Use", 81, 14),
         ("Use", 86, 29),
         ("Use", 87, 25),
         ("Use", 88, 26),
         ("Use", 93, 25));
   }

   /// <summary>
   /// Find-callers follows the hierarchy one way only, so an implementation's callers include calls through the
   /// interface but not calls to the sibling implementation.
   /// </summary>
   [Fact]
   public async Task FindCallers_NullConditionalIndexerAssignment_ImplementationOmitsSibling()
   {
      await AssertNullConditionalCallersAsync(
         "Stall.this[]",
         ("Rent", 99, 13),
         ("Rent", 100, 13),
         ("RentDirectly", 106, 12),
         ("RentDirectly", 107, 12));
   }

   [Fact]
   public async Task FindCallers_NullConditionalIndexerAssignment_InterfaceIncludesImplementations()
   {
      await AssertNullConditionalCallersAsync(
         "IStall.this[]",
         ("Rent", 99, 13),
         ("Rent", 100, 13),
         ("Rent", 101, 13),
         ("RentDirectly", 106, 12),
         ("RentDirectly", 107, 12),
         ("RentDirectly", 108, 12));
   }

   [Fact]
   public async Task ChangeImpact_NullConditionalIndexerAssignment_CountsEveryUse()
   {
      var tool = new AnalyzeChangeImpactTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.AnalyzeChangeImpactAsync(new LookupByName { Name = "Crate.this[]" }, ChangeType.Delete, cancellationToken: CT);

      Assert.Equal(1, result.Data.ReferenceCount);
      Assert.Equal([new ImpactLocation { FilePath = "NullConditionalIndexers.cs", Line = 90, Column = 13, IsDefinition = false }], result.Data.ImpactedLocations);
      Assert.False(result.Data.IsSafe);
   }

   [Fact]
   public async Task FindUnusedCode_NullConditionalIndexerAssignment_IndexerNotReported()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equal([(typeof(MethodSymbolResponse), "Empty", 37)], UnusedSymbolsOf(result.Data, "Crate<T>"));
   }

   private Task AssertNullConditionalReferencesAsync(string name, params (int Line, int Column, ReferenceKind Kind)[] expected) =>
      AssertReferencesAsync("NullConditionalIndexers.cs", new LookupByName { Name = name }, expected);

   private async Task AssertNullConditionalCallersAsync(string name, params (string Caller, int Line, int Column)[] expected)
   {
      var tool = new FindCallersTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.FindCallersAsync(new LookupByName { Name = name }, cancellationToken: CT);

      var callers = result.Data.Callers ?? throw new InvalidOperationException("Compact verbosity lists callers");
      Assert.Equal(
         expected.Select(e => (e.Caller, "NullConditionalIndexers.cs", e.Line, e.Column)),
         callers
            .Select(c => (c.CallingSymbol.Name, c.Location.FilePath, c.Location.Line, c.Location.Column))
            .OrderBy(c => c.Line).ThenBy(c => c.Column));
      Assert.Equal(expected.Length, result.Data.TotalCallers);
   }

   [Fact]
   public async Task FindCallers_ExtensionIndexer()
   {
      var tool = new FindCallersTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.FindCallersAsync(
         new LookupByPosition { FilePath = _fixture.GetPath("ExtensionIndexer.cs"), Line = 12, Column = 18 },
         cancellationToken: CT);

      Assert.Equal(3, result.Data.TotalCallers);
      var callers = result.Data.Callers ?? throw new InvalidOperationException("Compact verbosity lists callers");
      Assert.Equal(["SumThreeCorners", "SumThreeCorners", "SumThreeCorners"], callers.Select(c => c.CallingSymbol.Name));
      Assert.Equal(
         [
            new CallerLocationInfo { FilePath = "ExtensionIndexer.cs", Line = 20, Column = 25, LineText = "var topLeft = grid[0, 0];" },
            new CallerLocationInfo { FilePath = "ExtensionIndexer.cs", Line = 21, Column = 26, LineText = "var topRight = grid[0, 1];" },
            new CallerLocationInfo { FilePath = "ExtensionIndexer.cs", Line = 22, Column = 28, LineText = "var bottomLeft = grid[1, 0];" },
         ],
         callers.Select(c => c.Location));
   }

   /// <summary>
   /// Line 20 of ExtensionIndexer.cs is <c>var topLeft = grid[0, 0];</c>, where <c>find_references</c> reports the
   /// extension indexer at the <c>[</c>.
   /// </summary>
   [Theory]
   [InlineData("ExtensionIndexer.cs", 20, 25, "CSharp15TestProject.GridExtensions.extension(CSharp15TestProject.Grid).this[int, int]")]
   [InlineData("ExtensionIndexer.cs", 20, 27, "CSharp15TestProject.GridExtensions.extension(CSharp15TestProject.Grid).this[int, int]")]
   [InlineData("ExtensionIndexer.cs", 20, 30, "CSharp15TestProject.GridExtensions.extension(CSharp15TestProject.Grid).this[int, int]")]
   [InlineData("IndexerUses.cs", 25, 23, "CSharp15TestProject.Row.this[int]")]
   [InlineData("IndexerUses.cs", 25, 29, "CSharp15TestProject.Row.this[int]")]
   [InlineData("IndexerUses.cs", 26, 24, "CSharp15TestProject.Row.this[int]")]
   [InlineData("IndexerUses.cs", 27, 23, "CSharp15TestProject.Row.Slice(int, int)")]
   [InlineData("IndexerUses.cs", 28, 29, "CSharp15TestProject.Row.this[int]")]
   [InlineData("IndexerUses.cs", 28, 31, "CSharp15TestProject.Row.this[int]")]
   public async Task LookupByPosition_IndexerBracketOrComma_ResolvesToIndexer(string file, int line, int column, string expected)
   {
      Assert.Equal([expected], await LookUpDisplayStringsAsync(file, line, column));
   }

   [Theory]
   [InlineData(25, 24, "int index")]
   [InlineData(26, 25, "System.Index.Index(int, bool)")]
   [InlineData(27, 25, "System.Range.Range(System.Index, System.Index)")]
   public async Task LookupByPosition_IndexerArgument_ResolvesToArgument(int line, int column, string expected)
   {
      Assert.Equal([expected], await LookUpDisplayStringsAsync("IndexerUses.cs", line, column));
   }

   /// <summary>
   /// An array element access has no indexer symbol, so its <c>[</c> holds no symbol.
   /// </summary>
   [Fact]
   public async Task LookupByPosition_IndexerOnArray_ReportsSymbolNotFound()
   {
      var service = new SymbolLookupService(_fixture.Workspace);
      var lookup = new LookupByPosition { FilePath = _fixture.GetPath("IndexerUses.cs"), Line = 29, Column = 22 };

      var ex = await Assert.ThrowsAsync<ToolException>(() => service.FindSingleAsync<ISymbol>(lookup, cancellationToken: CT));

      Assert.Equal(ErrorCodes.SymbolNotFound, ex.Code);
      Assert.Equal($"No ISymbol found at {lookup.FilePath}:29:22", ex.Message);
   }

   [Fact]
   public async Task LookupByPosition_IndexerBracket_SymbolInfoDescribesIndexer()
   {
      var tool = new GetSymbolInfoTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.GetSymbolInfoAsync(
         new LookupByPosition { FilePath = _fixture.GetPath("ExtensionIndexer.cs"), Line = 20, Column = 25 },
         cancellationToken: CT);

      Assert.Equal("this[]", result.Data.Name);
      Assert.Equal("CSharp15TestProject.GridExtensions.extension(CSharp15TestProject.Grid)", result.Data.ContainingType);
   }

   [Theory]
   [InlineData("IndexerUses.cs", 28, 23, "CSharp15TestProject.RowUsage.Find(CSharp15TestProject.Row)")]
   [InlineData("IndexerUses.cs", 35, 29, "CSharp15TestProject.Score.Add(int, int)")]
   [InlineData("IndexerUses.cs", 35, 31, "CSharp15TestProject.Score.Add(int, int)")]
   [InlineData("IndexerUses.cs", 36, 19, "CSharp15TestProject.Score.Score(int)")]
   [InlineData("IndexerUses.cs", 36, 28, "CSharp15TestProject.Score.Score(int)")]
   [InlineData("IndexerUses.cs", 37, 24, "CSharp15TestProject.Score.operator +(CSharp15TestProject.Score, CSharp15TestProject.Score)")]
   [InlineData("PrimaryConstructors.cs", 14, 40, "CSharp15TestProject.Tally.Tally(int)")]
   public async Task LookupByPosition_CallToken_ResolvesToCalledMember(string file, int line, int column, string expected)
   {
      Assert.Equal([expected], await LookUpDisplayStringsAsync(file, line, column));
   }

   /// <summary>
   /// Each position sits inside a local's initializer, on a token that refers to no symbol.
   /// </summary>
   [Theory]
   [InlineData("ExtensionIndexer.cs", 20, 26)]
   [InlineData("IndexerUses.cs", 28, 28)]
   [InlineData("IndexerUses.cs", 29, 22)]
   [InlineData("IndexerUses.cs", 35, 30)]
   public async Task LookupByPosition_ExpressionTokenWithoutSymbol_FindsNothing(string file, int line, int column)
   {
      Assert.Empty(await LookUpDisplayStringsAsync(file, line, column));
   }

   private async Task<List<string>> LookUpDisplayStringsAsync(string file, int line, int column)
   {
      var service = new SymbolLookupService(_fixture.Workspace);
      var lookup = new LookupByPosition { FilePath = _fixture.GetPath(file), Line = line, Column = column };

      var results = await service.FindAsync<ISymbol>(lookup, cancellationToken: CT);

      return results.Select(r => r.Symbol.ToDisplayString()).ToList();
   }

   private const string TapeIntIndexer = "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape).this[int]";
   private const string TapeStringIndexer = "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape).this[string]";
   private const string ReelIndexer = "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Reel).this[int]";

   [Theory]
   [InlineData("SpoolExtensions.this[]", new[] { TapeIntIndexer, ReelIndexer, TapeStringIndexer })]
   [InlineData("CSharp15TestProject.SpoolExtensions.this[]", new[] { TapeIntIndexer, ReelIndexer, TapeStringIndexer })]
   [InlineData("SpoolExtensions.extension(Tape).this[]", new[] { TapeIntIndexer, TapeStringIndexer })]
   public async Task LookupByName_AmbiguousExtensionIndexer_ErrorListsEachCandidate(string name, string[] candidates)
   {
      await AssertAmbiguousAsync(name, candidates);
   }

   [Theory]
   [InlineData("SpoolExtensions.extension(Reel).this[]", null, ReelIndexer)]
   [InlineData("SpoolExtensions.extension(Tape).this[]", new[] { "int" }, TapeIntIndexer)]
   [InlineData("SpoolExtensions.extension(Tape).this[]", new[] { "string" }, TapeStringIndexer)]
   [InlineData("SpoolExtensions.this[]", new[] { "string" }, TapeStringIndexer)]
   [InlineData("CSharp15TestProject.SpoolExtensions.extension(Reel).this[]", null, ReelIndexer)]
   [InlineData("CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Reel).this[]", null, ReelIndexer)]
   [InlineData("SpoolExtensions.extension(CSharp15TestProject.Tape).this[]", new[] { "System.Int32" }, TapeIntIndexer)]
   public async Task LookupByName_AmbiguousExtensionIndexer_NameAndParameterTypesSelectOne(string name, string[]? parameterTypes, string expected)
   {
      await AssertSelectsAsync(new LookupByName { Name = name, ParameterTypes = parameterTypes }, expected);
   }

   [Fact]
   public async Task LookupByName_AmbiguousExtensionIndexer_ReturnTypeSelectsOne()
   {
      await AssertSelectsAsync(new LookupByName { Name = "SpoolExtensions.this[]", ReturnType = "string" }, ReelIndexer);
   }

   [Fact]
   public async Task LookupByName_AmbiguousIndexerOverload_ErrorListsEachCandidate()
   {
      await AssertAmbiguousAsync("Catalog.this[]", ["CSharp15TestProject.Catalog.this[int]", "CSharp15TestProject.Catalog.this[string]"]);
   }

   [Theory]
   [InlineData("int", "CSharp15TestProject.Catalog.this[int]")]
   [InlineData("string", "CSharp15TestProject.Catalog.this[string]")]
   public async Task LookupByName_AmbiguousIndexerOverload_ParameterTypesSelectOne(string parameterType, string expected)
   {
      await AssertSelectsAsync(new LookupByName { Name = "Catalog.this[]", ParameterTypes = [parameterType] }, expected);
   }

   private const string TapePeek = "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape).Peek()";
   private const string ReelPeek = "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Reel).Peek()";
   private const string TapeLength = "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape).Length";
   private const string ReelLength = "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Reel).Length";

   [Theory]
   [InlineData("SpoolExtensions.Peek", new[] { TapePeek, ReelPeek })]
   [InlineData("CSharp15TestProject.SpoolExtensions.Peek", new[] { TapePeek, ReelPeek })]
   [InlineData("SpoolExtensions.Length", new[] { TapeLength, ReelLength })]
   public async Task LookupByName_AmbiguousExtensionMember_ErrorListsEachCandidate(string name, string[] candidates)
   {
      await AssertAmbiguousAsync(name, candidates);
   }

   [Theory]
   [InlineData("SpoolExtensions.extension(Tape).Peek", null, null, TapePeek)]
   [InlineData("SpoolExtensions.extension(Reel).Peek", null, null, ReelPeek)]
   [InlineData("CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Reel).Peek", null, null, ReelPeek)]
   [InlineData("SpoolExtensions.Peek", new string[0], "string", ReelPeek)]
   [InlineData("CSharp15TestProject.SpoolExtensions.Peek", new string[0], "int", TapePeek)]
   [InlineData("SpoolExtensions.extension(Tape).Length", null, null, TapeLength)]
   [InlineData("SpoolExtensions.extension(Reel).Length", null, null, ReelLength)]
   public async Task LookupByName_AmbiguousExtensionMember_NameSelectsOne(string name, string[]? parameterTypes, string? returnType, string expected)
   {
      await AssertSelectsAsync(new LookupByName { Name = name, ParameterTypes = parameterTypes, ReturnType = returnType }, expected);
   }

   private const string Whisper = "CSharp15TestProject.TextExtensions.extension(string).Whisper()";

   /// <summary>
   /// The compiler declares a static implementation method in the holder class for each extension member, with the
   /// member's name, and a lookup by name finds the member alone.
   /// </summary>
   [Theory]
   [InlineData("Whisper", Whisper)]
   [InlineData("TextExtensions.Whisper", Whisper)]
   [InlineData("CSharp15TestProject.TextExtensions.Whisper", Whisper)]
   [InlineData("Blank", "CSharp15TestProject.TextExtensions.extension(string).Blank()")]
   public async Task LookupByName_ExtensionMember_FindsOnlyTheExtensionMember(string name, string expected)
   {
      await AssertSelectsAsync(new LookupByName { Name = name }, expected);
   }

   private const string MoneyCentsGetter = "CSharp15TestProject.Money.Cents.get";
   private const string IsShoutGetter = "CSharp15TestProject.TextExtensions.extension(string).IsShout.get";
   private const string GridIndexerGetter = "CSharp15TestProject.GridExtensions.extension(CSharp15TestProject.Grid).this[int, int].get";

   /// <summary>
   /// An accessor of an extension member resolves to the block's accessor, never to the implementation method.
   /// </summary>
   [Theory]
   [InlineData("get_Cents", MoneyCentsGetter)]
   [InlineData("Money.get_Cents", MoneyCentsGetter)]
   [InlineData("CSharp15TestProject.Money.get_Cents", MoneyCentsGetter)]
   [InlineData("Badge.set_Label", "CSharp15TestProject.Badge.Label.init")]
   [InlineData("Badge.get_Slot", "CSharp15TestProject.Badge.this[int].get")]
   [InlineData("CSharp15TestProject.Badge.get_Slot", "CSharp15TestProject.Badge.this[int].get")]
   [InlineData("Alarm.add_Rang", "CSharp15TestProject.Alarm.Rang.add")]
   [InlineData("Siren.remove_Rang", "CSharp15TestProject.Siren.Rang.remove")]
   [InlineData("get_IsShout", IsShoutGetter)]
   [InlineData("TextExtensions.get_IsShout", IsShoutGetter)]
   [InlineData("TextExtensions.extension(string).get_IsShout", IsShoutGetter)]
   [InlineData("CSharp15TestProject.TextExtensions.get_IsShout", IsShoutGetter)]
   [InlineData("GridExtensions.get_Item", GridIndexerGetter)]
   [InlineData("GridExtensions.extension(Grid).get_Item", GridIndexerGetter)]
   [InlineData("CSharp15TestProject.GridExtensions.get_Item", GridIndexerGetter)]
   [InlineData("BoardExtensions.set_Item", "CSharp15TestProject.BoardExtensions.extension(CSharp15TestProject.Board).this[int, int].set")]
   public async Task LookupByName_Accessor_FindsTheAccessor(string name, string expected)
   {
      await AssertSelectsAsync(new LookupByName { Name = name }, expected);
   }

   /// <summary>
   /// The compiler declares these accessors with no syntax of their own, at the declaration of their event or record.
   /// </summary>
   [Theory]
   [InlineData("Siren.add_Rang", "CSharp15TestProject.Siren.Rang.add", "InheritedMembers.cs", 104, 34)]
   [InlineData("Siren.remove_Rang", "CSharp15TestProject.Siren.Rang.remove", "InheritedMembers.cs", 104, 34)]
   [InlineData("Point.get_EqualityContract", "CSharp15TestProject.Point.EqualityContract.get", "PrimaryConstructors.cs", 3, 15)]
   public async Task LookupByName_GeneratedAccessor_IsSourceSymbolAtItsDeclaration(string name, string expected, string file, int line, int column)
   {
      var service = new SymbolLookupService(_fixture.Workspace);

      var result = await service.FindSingleSourceSymbolAsync<ISymbol>(new LookupByName { Name = name }, CT);

      var position = result.Symbol.Locations.Single().GetLineSpan();
      Assert.Equal(expected, result.Symbol.ToDisplayString());
      Assert.Equal(_fixture.GetPath(file), result.Document.FilePath);
      Assert.Null(result.Syntax);
      Assert.Equal(_fixture.GetPath(file), position.Path);
      Assert.Equal((line, column), (position.StartLinePosition.Line + 1, position.StartLinePosition.Character + 1));
   }

   [Theory]
   [InlineData("Box.Get", new[] { "CSharp15TestProject.Box<T>.Get()", "CSharp15TestProject.Box<T>.Get(int)" })]
   public async Task LookupByName_AmbiguousGenericMember_ErrorListsEachCandidate(string name, string[] candidates)
   {
      await AssertAmbiguousAsync(name, candidates);
   }

   [Theory]
   [InlineData("CSharp15TestProject.Box<T>.Get", new string[0], "CSharp15TestProject.Box<T>.Get()")]
   [InlineData("Box<T>.Get", new[] { "int" }, "CSharp15TestProject.Box<T>.Get(int)")]
   [InlineData("CSharp15TestProject.Mold.Make<T>", null, "CSharp15TestProject.Mold.Make<T>(int)")]
   [InlineData("Mold.Make<U>", new[] { "int" }, "CSharp15TestProject.Mold.Make<T>(int)")]
   public async Task LookupByName_AmbiguousGenericMember_NameWithTypeParametersSelectsOne(string name, string[]? parameterTypes, string expected)
   {
      await AssertSelectsAsync(new LookupByName { Name = name, ParameterTypes = parameterTypes }, expected);
   }

   /// <summary>
   /// Each name also matches a candidate with type parameters where the name lists none.
   /// </summary>
   [Theory]
   [InlineData("System.Threading.Tasks.Task", null, "System.Threading.Tasks.Task")]
   [InlineData("System.Action", null, "System.Action")]
   [InlineData("System.Nullable", null, "System.Nullable")]
   [InlineData("System.Threading.Tasks.Task.FromException", new[] { "System.Exception" }, "System.Threading.Tasks.Task.FromException(System.Exception)")]
   [InlineData("Mold.Make", null, "CSharp15TestProject.Mold.Make(int)")]
   [InlineData("Mold.Make", new[] { "int" }, "CSharp15TestProject.Mold.Make(int)")]
   [InlineData("CSharp15TestProject.Mold.Make", new[] { "int" }, "CSharp15TestProject.Mold.Make(int)")]
   [InlineData("Stamp.ToString", null, "CSharp15TestProject.Stamp.ToString()")]
   [InlineData("Stamp.Deconstruct", null, "CSharp15TestProject.Stamp.Deconstruct(out int)")]
   [InlineData("Stamp.Equals", new[] { "object" }, "CSharp15TestProject.Stamp.Equals(object?)")]
   [InlineData("Stamp.Value", null, "CSharp15TestProject.Stamp.Value")]
   [InlineData("Stamp.get_Value", null, "CSharp15TestProject.Stamp.Value.get")]
   [InlineData("CSharp15TestProject.Stamp", null, "CSharp15TestProject.Stamp")]
   [InlineData("System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter", null, "System.Runtime.CompilerServices.ConfiguredTaskAwaitable.ConfiguredTaskAwaiter")]
   public async Task LookupByName_TypeParameterCountZero_NameWithoutTypeParametersSelectsTheNonGenericCandidate(string name, string[]? parameterTypes, string expected)
   {
      await AssertSelectsAsync(new LookupByName { Name = name, ParameterTypes = parameterTypes }, expected);
   }

   [Theory]
   [InlineData("System.Threading.Tasks.Task`0", null, "System.Threading.Tasks.Task")]
   [InlineData("System.Threading.Tasks.Task<TResult>", null, "System.Threading.Tasks.Task<TResult>")]
   [InlineData("System.Threading.Tasks.Task`1", null, "System.Threading.Tasks.Task<TResult>")]
   [InlineData("System.Action`2", null, "System.Action<T1, T2>")]
   [InlineData("System.Threading.Tasks.Task.FromException<TResult>", new[] { "System.Exception" }, "System.Threading.Tasks.Task.FromException<TResult>(System.Exception)")]
   [InlineData("System.Threading.Tasks.Task.FromException`1", new[] { "System.Exception" }, "System.Threading.Tasks.Task.FromException<TResult>(System.Exception)")]
   [InlineData("Mold.Make`1", new[] { "int" }, "CSharp15TestProject.Mold.Make<T>(int)")]
   [InlineData("Stamp<T>.ToString", null, "CSharp15TestProject.Stamp<T>.ToString()")]
   [InlineData("Stamp`1.Deconstruct", null, "CSharp15TestProject.Stamp<T>.Deconstruct(out int)")]
   [InlineData("Stamp<T>.Value", null, "CSharp15TestProject.Stamp<T>.Value")]
   [InlineData("CSharp15TestProject.Stamp`1", null, "CSharp15TestProject.Stamp<T>")]
   [InlineData("System.Runtime.CompilerServices.ConfiguredTaskAwaitable<TResult>.ConfiguredTaskAwaiter", null, "System.Runtime.CompilerServices.ConfiguredTaskAwaitable<TResult>.ConfiguredTaskAwaiter")]
   public async Task LookupByName_TypeParameterCountGiven_SelectsThatCount(string name, string[]? parameterTypes, string expected)
   {
      await AssertSelectsAsync(new LookupByName { Name = name, ParameterTypes = parameterTypes }, expected);
   }

   [Theory]
   [InlineData("Box<T, U>.Get")]
   [InlineData("CSharp15TestProject.Box<T, U>.Get")]
   [InlineData("Mold.Make<T, U>")]
   [InlineData("Mold.Make`2")]
   [InlineData("Box`0.Get")]
   [InlineData("System.Threading.Tasks.Task`3")]
   [InlineData("Mold.Make`x")]
   public async Task LookupByName_TypeParameterCountDiffers_FindsNothing(string name)
   {
      var service = new SymbolLookupService(_fixture.Workspace);

      var results = await service.FindAsync<ISymbol>(new LookupByName { Name = name }, cancellationToken: CT);

      Assert.Empty(results);
   }

   [Theory]
   [InlineData("CSharp15TestProject.Money.op_Implicit", new[] { "CSharp15TestProject.Money.op_Implicit(int) returns CSharp15TestProject.Money", "CSharp15TestProject.Money.op_Implicit(long) returns CSharp15TestProject.Money" })]
   [InlineData("CSharp15TestProject.Money.op_Explicit", new[] { "CSharp15TestProject.Money.op_Explicit(CSharp15TestProject.Money) returns int", "CSharp15TestProject.Money.op_Explicit(CSharp15TestProject.Money) returns long" })]
   [InlineData("CSharp15TestProject.Money.op_LessThan", new[] { "CSharp15TestProject.Money.op_LessThan(CSharp15TestProject.Money, CSharp15TestProject.Money)", "CSharp15TestProject.Money.op_LessThan(CSharp15TestProject.Money, int)" })]
   [InlineData("CSharp15TestProject.Catalog.get_Item", new[] { "CSharp15TestProject.Catalog.get_Item(int)", "CSharp15TestProject.Catalog.get_Item(string)" })]
   [InlineData("Catalog.get_Item", new[] { "CSharp15TestProject.Catalog.get_Item(int)", "CSharp15TestProject.Catalog.get_Item(string)" })]
   [InlineData("add_Rang", new[] { "CSharp15TestProject.Alarm.add_Rang(System.Action)", "CSharp15TestProject.Siren.add_Rang(System.Action)" })]
   [InlineData("SpoolExtensions.get_Item", new[] { "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape).get_Item(int)", "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Reel).get_Item(int)", "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape).get_Item(string)" })]
   public async Task LookupByName_AmbiguousOperatorOrAccessor_ErrorListsEachCandidateByItsName(string name, string[] candidates)
   {
      await AssertAmbiguousAsync(name, candidates);
   }

   /// <summary>
   /// Only the first five of the nine implicit conversions to <c>decimal</c> are listed.
   /// </summary>
   [Fact]
   public async Task LookupByName_AmbiguousMetadataOperator_ErrorNamesTheTypeByItsName()
   {
      var service = new SymbolLookupService(_fixture.Workspace);

      var ex = await Assert.ThrowsAsync<ToolException>(
         () => service.FindSingleAsync<ISymbol>(new LookupByName { Name = "System.Decimal.op_Implicit" }, cancellationToken: CT));

      Assert.Equal(
         "Multiple symbols found (9): System.Decimal.op_Implicit(byte) returns decimal; System.Decimal.op_Implicit(char) returns decimal; System.Decimal.op_Implicit(short) returns decimal; System.Decimal.op_Implicit(int) returns decimal; System.Decimal.op_Implicit(long) returns decimal; and 4 more",
         ex.Message);
   }

   /// <summary>
   /// Each name matches several symbols, and the error lists them. Each listed candidate with its parameter list
   /// moved to parameterTypes, its return type to returnType, its location to position mode, or its project to project,
   /// as <see cref="SymbolLookupService.AmbiguityHint"/> says, must find that candidate alone.
   /// </summary>
   [Theory]
   [InlineData("SpoolExtensions.this[]", new[] { "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape).this[int]", "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Reel).this[int]", "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape).this[string]" })]
   [InlineData("SpoolExtensions.extension(Tape).this[]", new[] { "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape).this[int]", "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape).this[string]" })]
   [InlineData("Catalog.this[]", new[] { "CSharp15TestProject.Catalog.this[int]", "CSharp15TestProject.Catalog.this[string]" })]
   [InlineData("CSharp15TestProject.Catalog.get_Item", new[] { "CSharp15TestProject.Catalog.get_Item(int)", "CSharp15TestProject.Catalog.get_Item(string)" })]
   [InlineData("Catalog.get_Item", new[] { "CSharp15TestProject.Catalog.get_Item(int)", "CSharp15TestProject.Catalog.get_Item(string)" })]
   [InlineData("get_Item", new[] { "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape).get_Item(int)", "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Reel).get_Item(int)", "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape).get_Item(string)", "CSharp15TestProject.Catalog.get_Item(int)", "CSharp15TestProject.Catalog.get_Item(string)" })]
   [InlineData("add_Rang", new[] { "CSharp15TestProject.Alarm.add_Rang(System.Action)", "CSharp15TestProject.Siren.add_Rang(System.Action)" })]
   [InlineData("SpoolExtensions.get_Item", new[] { "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape).get_Item(int)", "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Reel).get_Item(int)", "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape).get_Item(string)" })]
   [InlineData("SpoolExtensions.extension(Tape).get_Item", new[] { "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape).get_Item(int)", "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape).get_Item(string)" })]
   [InlineData("SpoolExtensions.Peek", new[] { "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape).Peek()", "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Reel).Peek()" })]
   [InlineData("CSharp15TestProject.SpoolExtensions.Peek", new[] { "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape).Peek()", "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Reel).Peek()" })]
   [InlineData("SpoolExtensions.Length", new[] { "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape).Length", "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Reel).Length" })]
   [InlineData("Box.Get", new[] { "CSharp15TestProject.Box<T>.Get()", "CSharp15TestProject.Box<T>.Get(int)" })]
   [InlineData("CSharp15TestProject.Money.op_Implicit", new[] { "CSharp15TestProject.Money.op_Implicit(int) returns CSharp15TestProject.Money", "CSharp15TestProject.Money.op_Implicit(long) returns CSharp15TestProject.Money" })]
   [InlineData("CSharp15TestProject.Money.op_Explicit", new[] { "CSharp15TestProject.Money.op_Explicit(CSharp15TestProject.Money) returns int", "CSharp15TestProject.Money.op_Explicit(CSharp15TestProject.Money) returns long" })]
   [InlineData("CSharp15TestProject.Money.op_LessThan", new[] { "CSharp15TestProject.Money.op_LessThan(CSharp15TestProject.Money, CSharp15TestProject.Money)", "CSharp15TestProject.Money.op_LessThan(CSharp15TestProject.Money, int)" })]
   [InlineData("System.Decimal.op_Implicit", new[] { "System.Decimal.op_Implicit(byte) returns decimal", "System.Decimal.op_Implicit(char) returns decimal", "System.Decimal.op_Implicit(short) returns decimal", "System.Decimal.op_Implicit(int) returns decimal", "System.Decimal.op_Implicit(long) returns decimal" })]
   [InlineData("System.Decimal.op_Explicit", new[] { "System.Decimal.op_Explicit(decimal) returns byte", "System.Decimal.op_Explicit(decimal) returns char", "System.Decimal.op_Explicit(decimal) returns double", "System.Decimal.op_Explicit(decimal) returns short", "System.Decimal.op_Explicit(decimal) returns int" })]
   [InlineData("System.String.Join", new[] { "System.String.Join(char, params object[])", "System.String.Join(char, params System.ReadOnlySpan<object>)", "System.String.Join(char, params string[])", "System.String.Join(char, params System.ReadOnlySpan<string>)", "System.String.Join(char, string[], int, int)" })]
   [InlineData("System.Func", new[] { "System.Func<TResult>", "System.Func<T, TResult>", "System.Func<T1, T2, TResult>", "System.Func<T1, T2, T3, TResult>", "System.Func<T1, T2, T3, T4, TResult>" })]
   [InlineData("System.Tuple.Create", new[] { "System.Tuple.Create<T1>(T1)", "System.Tuple.Create<T1, T2>(T1, T2)", "System.Tuple.Create<T1, T2, T3>(T1, T2, T3)", "System.Tuple.Create<T1, T2, T3, T4>(T1, T2, T3, T4)", "System.Tuple.Create<T1, T2, T3, T4, T5>(T1, T2, T3, T4, T5)" })]
   [InlineData("System.Array.IndexOf", new[] { "System.Array.IndexOf(System.Array, object)", "System.Array.IndexOf(System.Array, object, int)", "System.Array.IndexOf(System.Array, object, int, int)", "System.Array.IndexOf<T>(T[], T)", "System.Array.IndexOf<T>(T[], T, int)" })]
   [InlineData("Stamp.Equals", new[] { "CSharp15TestProject.Stamp.Equals(object)", "CSharp15TestProject.Stamp.Equals(CSharp15TestProject.Stamp)", "CSharp15TestProject.Stamp<T>.Equals(CSharp15TestProject.Stamp<T>)" })]
   [InlineData("Stamp<T>.Equals", new[] { "CSharp15TestProject.Stamp<T>.Equals(object)", "CSharp15TestProject.Stamp<T>.Equals(CSharp15TestProject.Stamp<T>)" })]
   [InlineData("extension(Tape)", new[] { "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape) at AmbiguousMembers.cs:15:4", "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape) at AmbiguousMembers.cs:33:4" })]
   [InlineData("SpoolExtensions.extension(Tape)", new[] { "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape) at AmbiguousMembers.cs:15:4", "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape) at AmbiguousMembers.cs:33:4" })]
   [InlineData("CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape)", new[] { "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape) at AmbiguousMembers.cs:15:4", "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape) at AmbiguousMembers.cs:33:4" })]
   public async Task LookupByName_AmbiguousCandidates_EachListedCandidateFindsItself(string name, string[] expected)
   {
      await AmbiguityRoundTrip.AssertEachListedCandidateFindsItselfAsync(
         new SymbolLookupService(_fixture.Workspace), new LookupByName { Name = name }, expected, CT);
   }

   private const string GridBlock = "CSharp15TestProject.GridExtensions.extension(CSharp15TestProject.Grid)";

   [Theory]
   [InlineData("GridExtensions.extension(Grid)", GridBlock)]
   [InlineData("GridExtensions.extension(CSharp15TestProject.Grid)", GridBlock)]
   [InlineData("extension(Grid)", GridBlock)]
   [InlineData("extension(CSharp15TestProject.Grid)", GridBlock)]
   [InlineData("CSharp15TestProject.GridExtensions.extension(Grid)", GridBlock)]
   [InlineData("MeterExtensions.extension<T>(ref T)", "CSharp15TestProject.MeterExtensions.extension<T>(ref T)")]
   [InlineData("extension(scoped ref Meter)", "CSharp15TestProject.MeterExtensions.extension(scoped ref CSharp15TestProject.Meter)")]
   public async Task LookupByName_ExtensionBlockShortPath_FindsTheBlock(string name, string expected)
   {
      await AssertSelectsAsync(new LookupByName { Name = name }, expected);
   }

   /// <summary>
   /// SpoolExtensions declares two blocks for Tape, which only their locations tell apart.
   /// </summary>
   [Theory]
   [InlineData("extension(Tape)")]
   [InlineData("SpoolExtensions.extension(Tape)")]
   public async Task LookupByName_ExtensionBlockShortPath_AmbiguousHeaderListsEachBlock(string name)
   {
      await AssertAmbiguousAsync(name,
      [
         "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape) at AmbiguousMembers.cs:15:4",
         "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape) at AmbiguousMembers.cs:33:4",
      ]);
   }

   /// <summary>
   /// An empty parameterTypes stands for a parameterless method or indexer, and an ordinary property has neither.
   /// </summary>
   [Theory]
   [InlineData("System.String.Length")]
   [InlineData("SpoolExtensions.extension(Tape).Length")]
   [InlineData("Money.Cents")]
   public async Task LookupByName_EmptyParameterTypes_SkipsPropertiesThatAreNotIndexers(string name)
   {
      var service = new SymbolLookupService(_fixture.Workspace);

      var withFilter = await service.FindAsync<ISymbol>(new LookupByName { Name = name, ParameterTypes = [] }, cancellationToken: CT);
      var withoutFilter = await service.FindAsync<ISymbol>(new LookupByName { Name = name }, cancellationToken: CT);

      Assert.Empty(withFilter);
      Assert.Single(withoutFilter);
   }

   private async Task AssertAmbiguousAsync(string name, string[] candidates)
   {
      var service = new SymbolLookupService(_fixture.Workspace);
      var lookup = new LookupByName { Name = name };

      var any = await Assert.ThrowsAsync<ToolException>(() => service.FindSingleAsync<ISymbol>(lookup, cancellationToken: CT));
      var source = await Assert.ThrowsAsync<ToolException>(() => service.FindSingleSourceSymbolAsync<ISymbol>(lookup, CT));

      Assert.Equal(ErrorCodes.AmbiguousSymbol, any.Code);
      Assert.Equal($"Multiple symbols found ({candidates.Length}): {string.Join("; ", candidates)}", any.Message);
      Assert.Equal(ErrorCodes.AmbiguousSymbol, source.Code);
      Assert.Equal($"Multiple source symbols found ({candidates.Length}): {string.Join("; ", candidates)}", source.Message);
      Assert.Equal(SymbolLookupService.AmbiguityHint, any.Hint);
      Assert.Equal(SymbolLookupService.AmbiguityHint, source.Hint);
   }

   private async Task AssertSelectsAsync(LookupByName lookup, string expected)
   {
      var service = new SymbolLookupService(_fixture.Workspace);

      var results = await service.FindAsync<ISymbol>(lookup, cancellationToken: CT);

      Assert.Equal([expected], results.Select(r => r.Symbol.ToDisplayString()));
   }

   [Fact]
   public async Task FindUnusedCode_ExtensionBlockWithUsedMembers_NotReported()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equal(
         [
            (typeof(ClassSymbolResponse), "GridUsage", 16),
            (typeof(MethodSymbolResponse), "SumThreeCorners", 18),
            (typeof(ConstructorSymbolResponse), ".ctor", 3),
         ],
         UnusedSymbolsIn(result.Data, "ExtensionIndexer.cs"));
      Assert.DoesNotContain(result.Data.UnusedSymbols, s => s.Name.Length == 0);
      Assert.False(result.Data.CountByKind.ContainsKey("Extension"));
   }

   [Fact]
   public async Task FindUnusedCode_ExtensionBlockWithUnusedMembers_ReportsMembersOnly()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equal(
         [
            (typeof(ClassSymbolResponse), "TextUsage", 27),
            (typeof(PropertySymbolResponse), "IsEven", 16),
            (typeof(MethodSymbolResponse), "Doubled", 18),
            (typeof(MethodSymbolResponse), "Quiet", 29),
         ],
         UnusedSymbolsIn(result.Data, "ExtensionMembers.cs"));
   }

   [Fact]
   public async Task FindUnusedCode_ExtensionContainerWithUsedBlockMembers_NotReported()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);
      var holders = AllExtensionBlocks.Select(b => b.ContainingType).ToHashSet();

      var result = await tool.FindUnusedCodeAsync(verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equivalent(
         new[]
         {
            "CSharp15TestProject.CatalogExtensions",
            "CSharp15TestProject.LedgerExtensions",
            "CSharp15TestProject.MeterExtensions",
            "CSharp15TestProject.SpoolExtensions",
            "CSharp15TestProject.StaleExtensions",
         },
         result.Data.UnusedSymbols.OfType<ClassSymbolResponse>().Select(s => s.FullyQualifiedName).Where(holders.Contains),
         strict: true);
   }

   [Fact]
   public async Task FindUnusedCode_ExtensionContainers_ReportsExactlyUnusedContainersAndMembers()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equal(
         [
            (typeof(ClassSymbolResponse), "StaleExtensions", 10),
            (typeof(ClassSymbolResponse), "LoopExtensions", 20),
            (typeof(ClassSymbolResponse), "ContainerUsage", 36),
            (typeof(MethodSymbolResponse), "Shout", 7),
            (typeof(MethodSymbolResponse), "Tripled", 12),
            (typeof(PropertySymbolResponse), "Halved", 16),
            (typeof(MethodSymbolResponse), "Squared", 22),
            (typeof(MethodSymbolResponse), "Score", 38),
         ],
         UnusedSymbolsIn(result.Data, "ExtensionContainers.cs"));
   }

   [Fact]
   public async Task FindUnusedCode_ExtensionContainerWithUsedClassicMethod_NotReported()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equal([(typeof(MethodSymbolResponse), "Shout", 7)], UnusedSymbolsOf(result.Data, "WordExtensions"));
   }

   [Fact]
   public async Task FindUnusedCode_ExtensionContainerWithOnlyUnusedMembers_ReportsContainerAndMembers()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equal(
         [
            (typeof(ClassSymbolResponse), "StaleExtensions", 10),
            (typeof(MethodSymbolResponse), "Tripled", 12),
            (typeof(PropertySymbolResponse), "Halved", 16),
         ],
         UnusedSymbolsOf(result.Data, "StaleExtensions"));
   }

   /// <summary>
   /// Times has a use, but only from Squared in the same class, so no code outside LoopExtensions uses the class.
   /// </summary>
   [Fact]
   public async Task FindUnusedCode_ExtensionContainerUsedOnlyByItsOwnMembers_Reported()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equal(
         [
            (typeof(ClassSymbolResponse), "LoopExtensions", 20),
            (typeof(MethodSymbolResponse), "Squared", 22),
         ],
         UnusedSymbolsOf(result.Data, "LoopExtensions"));
   }

   [Fact]
   public async Task FindUnusedCode_ExtensionContainerStaticConstructor_NotReportedWhenMembersUsed()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(
         symbolKindFilter: SymbolKindFilter.Constructor, verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Empty(UnusedSymbolsOf(result.Data, "SeededExtensions"));
   }

   /// <summary>
   /// The summary of each class cites the class's own member, in CrefPartial from the other partial declaration.
   /// </summary>
   [Fact]
   public async Task FindUnusedCode_StaticClassCitedOnlyByItsOwnDocComment_Reported()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equal([(typeof(ClassSymbolResponse), "CrefSelf", 6)], UnusedSymbolsOf(result.Data, "CrefSelf"));
      Assert.Equal([(typeof(ClassSymbolResponse), "CrefPartial", 14)], UnusedSymbolsOf(result.Data, "CrefPartial"));
   }

   [Fact]
   public async Task FindUnusedCode_StaticClassNamedOnlyByItsOwnMembers_Reported()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equal(
         [
            (typeof(ClassSymbolResponse), "QualifiedSelf", 23),
            (typeof(MethodSymbolResponse), "First", 25),
         ],
         UnusedSymbolsOf(result.Data, "QualifiedSelf"));
   }

   [Fact]
   public async Task FindUnusedCode_ClassNamedOnlyByItsOwnMembers_Reported()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equal(
         [
            (typeof(ClassSymbolResponse), "Chain", 30),
            (typeof(PropertySymbolResponse), "Next", 32),
         ],
         UnusedSymbolsOf(result.Data, "Chain"));
   }

   [Fact]
   public async Task FindUnusedCode_SelfReferencingMembers_ReportsExactlyUnusedSymbols()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equal(
         [
            (typeof(ClassSymbolResponse), "SelfCitedMembers", 3),
            (typeof(ClassSymbolResponse), "TokenUsage", 32),
            (typeof(MethodSymbolResponse), "Echo", 8),
            (typeof(FieldSymbolResponse), "Stored", 13),
            (typeof(MethodSymbolResponse), "Countdown", 15),
            (typeof(MethodSymbolResponse), "Citer", 20),
            (typeof(MethodSymbolResponse), "Read", 34),
         ],
         UnusedSymbolsIn(result.Data, "SelfReferencingMembers.cs"));
   }

   [Fact]
   public async Task FindUnusedCode_RecursiveMethodCalledOnlyByItself_Reported()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(
         symbolKindFilter: SymbolKindFilter.Method, verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equal(
         [(typeof(MethodSymbolResponse), "Countdown", 15)],
         UnusedSymbolsIn(result.Data, "SelfReferencingMembers.cs").Where(s => s.Name == "Countdown"));
   }

   /// <summary>
   /// The doc comment of Echo cites Echo, and the doc comment of the field Stored cites Stored.
   /// </summary>
   [Fact]
   public async Task FindUnusedCode_MemberCitedOnlyByItsOwnDocComment_Reported()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equal(
         [
            (typeof(MethodSymbolResponse), "Echo", 8),
            (typeof(FieldSymbolResponse), "Stored", 13),
         ],
         UnusedSymbolsIn(result.Data, "SelfReferencingMembers.cs").Where(s => s.Name is "Echo" or "Stored"));
   }

   /// <summary>
   /// Only the doc comment of Citer cites Cited, and a cref counts as a use.
   /// </summary>
   [Fact]
   public async Task FindUnusedCode_MemberCitedByAnotherMembersDocComment_NotReported()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equal(
         [(typeof(MethodSymbolResponse), "Citer", 20)],
         UnusedSymbolsIn(result.Data, "SelfReferencingMembers.cs").Where(s => s.Name is "Citer" or "Cited"));
   }

   /// <summary>
   /// Only Create, a member of the same type, calls the primary constructor of Token.
   /// </summary>
   [Fact]
   public async Task FindUnusedCode_PrimaryConstructorCalledFromItsOwnType_NotReported()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(
         symbolKindFilter: SymbolKindFilter.Constructor, verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Empty(UnusedSymbolsOf(result.Data, "Token"));
   }

   /// <summary>
   /// The only reference to each field, event and property is the attribute on its own declaration.
   /// </summary>
   [Fact]
   public async Task FindUnusedCode_MembersNamedOnlyByTheirOwnAttributes_Reported()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equal(
         [
            (typeof(ClassSymbolResponse), "AttributedFields", 5),
            (typeof(FieldSymbolResponse), "Tag", 8),
            (typeof(EventSymbolResponse), "Changed", 11),
            (typeof(FieldSymbolResponse), "Left", 14),
            (typeof(FieldSymbolResponse), "Right", 14),
            (typeof(FieldSymbolResponse), "Derived", 16),
            (typeof(PropertySymbolResponse), "Label", 19),
         ],
         UnusedSymbolsIn(result.Data, "AttributedFields.cs"));
   }

   /// <summary>
   /// The attribute that names Left belongs to Left and Right alike, so it is part of Left's own declaration. The
   /// initializer of Derived names Base from outside Base's declarator.
   /// </summary>
   [Fact]
   public async Task FindUnusedCode_FieldsSharingADeclaration_OwnOnlyTheirDeclaratorAndTheSharedPrefix()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(
         symbolKindFilter: SymbolKindFilter.Field, verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equal(
         [
            (typeof(FieldSymbolResponse), "Left", 14),
            (typeof(FieldSymbolResponse), "Right", 14),
            (typeof(FieldSymbolResponse), "Derived", 16),
         ],
         UnusedSymbolsIn(result.Data, "AttributedFields.cs").Where(s => s.Line is 14 or 16));
   }

   [Fact]
   public async Task FindUnusedCode_ModuleInitializerAndItsClass_NotReported()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Empty(UnusedSymbolsIn(result.Data, "ModuleInitializers.cs"));
   }

   [Fact]
   public async Task FindUnusedCode_TypesNamedByUsingStaticNameofOrAttribute_NotReported()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equal(
         [
            (typeof(ClassSymbolResponse), "ImplicitTypeUsage", 18),
            (typeof(MethodSymbolResponse), "Use", 20),
         ],
         UnusedSymbolsIn(result.Data, "ImplicitTypeUses.cs"));
   }

   private static readonly ExtensionSymbolResponse GridExtensionBlock = new()
   {
      Name = "extension(Grid)",
      Location = new SymbolLocation { FilePath = "ExtensionIndexer.cs", Line = 10, Column = 4 },
      ContainingType = "CSharp15TestProject.GridExtensions",
      Accessibility = AccessibilityDto.Public,
      Modifiers = SymbolModifiers.Sealed,
   };

   private static readonly ExtensionSymbolResponse[] RefReceiverExtensionBlocks =
   [
      GridExtensionBlock with
      {
         Name = "extension(Meter)",
         Location = new SymbolLocation { FilePath = "RefReceiverExtensions.cs", Line = 10, Column = 4 },
         ContainingType = "CSharp15TestProject.MeterExtensions",
      },
      GridExtensionBlock with
      {
         Name = "extension(ref Meter)",
         Location = new SymbolLocation { FilePath = "RefReceiverExtensions.cs", Line = 15, Column = 4 },
         ContainingType = "CSharp15TestProject.MeterExtensions",
      },
      GridExtensionBlock with
      {
         Name = "extension(in Meter)",
         Location = new SymbolLocation { FilePath = "RefReceiverExtensions.cs", Line = 20, Column = 4 },
         ContainingType = "CSharp15TestProject.MeterExtensions",
      },
      GridExtensionBlock with
      {
         Name = "extension(ref readonly Meter)",
         Location = new SymbolLocation { FilePath = "RefReceiverExtensions.cs", Line = 25, Column = 4 },
         ContainingType = "CSharp15TestProject.MeterExtensions",
      },
      GridExtensionBlock with
      {
         Name = "extension(scoped ref Meter)",
         Location = new SymbolLocation { FilePath = "RefReceiverExtensions.cs", Line = 30, Column = 4 },
         ContainingType = "CSharp15TestProject.MeterExtensions",
      },
      GridExtensionBlock with
      {
         Name = "extension(scoped Span<int>)",
         Location = new SymbolLocation { FilePath = "RefReceiverExtensions.cs", Line = 35, Column = 4 },
         ContainingType = "CSharp15TestProject.MeterExtensions",
      },
      GridExtensionBlock with
      {
         Name = "extension<T>(ref T)",
         Location = new SymbolLocation { FilePath = "RefReceiverExtensions.cs", Line = 40, Column = 4 },
         ContainingType = "CSharp15TestProject.MeterExtensions",
      },
   ];

   private static readonly ExtensionSymbolResponse[] AllExtensionBlocks =
   [
      GridExtensionBlock with
      {
         Name = "extension(Tape)",
         Location = new SymbolLocation { FilePath = "AmbiguousMembers.cs", Line = 15, Column = 4 },
         ContainingType = "CSharp15TestProject.SpoolExtensions",
      },
      GridExtensionBlock with
      {
         Name = "extension(Reel)",
         Location = new SymbolLocation { FilePath = "AmbiguousMembers.cs", Line = 24, Column = 4 },
         ContainingType = "CSharp15TestProject.SpoolExtensions",
      },
      GridExtensionBlock with
      {
         Name = "extension(Tape)",
         Location = new SymbolLocation { FilePath = "AmbiguousMembers.cs", Line = 33, Column = 4 },
         ContainingType = "CSharp15TestProject.SpoolExtensions",
      },
      GridExtensionBlock with
      {
         Name = "extension(Catalog)",
         Location = new SymbolLocation { FilePath = "ExtensionBlockHeaders.cs", Line = 8, Column = 4 },
         ContainingType = "CSharp15TestProject.CatalogExtensions",
      },
      GridExtensionBlock with
      {
         Name = "extension<TKey, TValue>(IDictionary<TKey, TValue>)",
         Location = new SymbolLocation { FilePath = "ExtensionBlockHeaders.cs", Line = 16, Column = 4 },
         ContainingType = "CSharp15TestProject.CatalogExtensions",
      },
      GridExtensionBlock with
      {
         Name = "extension(long)",
         Location = new SymbolLocation { FilePath = "ExtensionContainers.cs", Line = 14, Column = 4 },
         ContainingType = "CSharp15TestProject.StaleExtensions",
      },
      GridExtensionBlock,
      GridExtensionBlock with
      {
         Name = "extension(string)",
         Location = new SymbolLocation { FilePath = "ExtensionMembers.cs", Line = 5, Column = 4 },
         ContainingType = "CSharp15TestProject.TextExtensions",
      },
      GridExtensionBlock with
      {
         Name = "extension(int)",
         Location = new SymbolLocation { FilePath = "ExtensionMembers.cs", Line = 14, Column = 4 },
         ContainingType = "CSharp15TestProject.TextExtensions",
      },
      GridExtensionBlock with
      {
         Name = "extension<T>(IEnumerable<T>)",
         Location = new SymbolLocation { FilePath = "ExtensionMembers.cs", Line = 21, Column = 4 },
         ContainingType = "CSharp15TestProject.TextExtensions",
      },
      GridExtensionBlock with
      {
         Name = "extension(Board)",
         Location = new SymbolLocation { FilePath = "IndexerWrites.cs", Line = 39, Column = 4 },
         ContainingType = "CSharp15TestProject.BoardExtensions",
      },
      GridExtensionBlock with
      {
         Name = "extension<TKey, TValue>(Ledger<TKey, TValue>)",
         Location = new SymbolLocation { FilePath = "MultiLineSignatures.cs", Line = 59, Column = 4 },
         ContainingType = "CSharp15TestProject.LedgerExtensions",
      },
      GridExtensionBlock with
      {
         Name = "extension(Ward)",
         Location = new SymbolLocation { FilePath = "NullConditionalIndexers.cs", Line = 42, Column = 4 },
         ContainingType = "CSharp15TestProject.WardExtensions",
      },
      .. RefReceiverExtensionBlocks,
   ];

   private LookupByPosition GridExtensionBlockPosition =>
      new() { FilePath = _fixture.GetPath("ExtensionIndexer.cs"), Line = 10, Column = 4 };

   [Fact]
   public async Task SymbolInfo_ExtensionBlock_HasExtensionKindAndName()
   {
      var tool = new GetSymbolInfoTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.GetSymbolInfoAsync(GridExtensionBlockPosition, cancellationToken: CT);

      Assert.Equal(
         GridExtensionBlock with
         {
            FullyQualifiedName = "CSharp15TestProject.GridExtensions.extension(CSharp15TestProject.Grid)",
            ContainingNamespace = "CSharp15TestProject",
            Declaration = "extension(Grid grid)",
         },
         result.Data);
   }

   [Fact]
   public async Task SearchSymbols_ExtensionBlocks_HaveExtensionKindAndName()
   {
      var tool = new SearchSymbolsTool(_fixture.Workspace);

      var result = await tool.SearchSymbolsAsync("*", maxResults: 500, cancellationToken: CT);

      Assert.False(result.Data.HasMore);
      Assert.Equal(AllExtensionBlocks, result.Data.Items.OfType<ExtensionSymbolResponse>());
      Assert.DoesNotContain(result.Data.Items, s => s is ClassSymbolResponse { Name.Length: 0 });
   }

   [Fact]
   public async Task FindSymbols_ExtensionBlocks_HaveExtensionKindAndName()
   {
      var tool = new FindSymbolsTool(_fixture.Workspace);

      var result = await tool.FindSymbolsAsync("*", maxResults: 500, cancellationToken: CT);

      Assert.False(result.Data.HasMore);
      Assert.Equal(AllExtensionBlocks, result.Data.Items.OfType<ExtensionSymbolResponse>());
      Assert.DoesNotContain(result.Data.Items, s => s is ClassSymbolResponse { Name.Length: 0 });
   }

   /// <summary>
   /// Each symbol declared in the fixture, as its file, line, column and name, in ordinal order. A record's and a
   /// delegate's compiler-generated members, a union's Value property, the global namespace and the implementation
   /// method of each extension member are absent, as <see cref="RoslynHelpers.IsExplicitSourceSymbol"/> says.
   /// </summary>
   private static readonly string[] AllExplicitlyDeclaredSymbols =
   [
      "AccessorNames.cs:1:11 CSharp15TestProject",
      "AccessorNames.cs:3:21 Badge",
      "AccessorNames.cs:5:18 Label",
      "AccessorNames.cs:8:15 this[]",
      "AmbiguousMembers.cs:10:20 Labels",
      "AmbiguousMembers.cs:13:21 SpoolExtensions",
      "AmbiguousMembers.cs:15:4 extension(Tape)",
      "AmbiguousMembers.cs:17:18 this[]",
      "AmbiguousMembers.cs:19:18 Length",
      "AmbiguousMembers.cs:21:18 Peek",
      "AmbiguousMembers.cs:24:4 extension(Reel)",
      "AmbiguousMembers.cs:26:21 this[]",
      "AmbiguousMembers.cs:28:18 Length",
      "AmbiguousMembers.cs:30:21 Peek",
      "AmbiguousMembers.cs:33:4 extension(Tape)",
      "AmbiguousMembers.cs:35:18 this[]",
      "AmbiguousMembers.cs:39:21 Catalog",
      "AmbiguousMembers.cs:3:21 Tape",
      "AmbiguousMembers.cs:41:15 this[]",
      "AmbiguousMembers.cs:43:15 this[]",
      "AmbiguousMembers.cs:46:21 Box",
      "AmbiguousMembers.cs:48:13 Get",
      "AmbiguousMembers.cs:50:13 Get",
      "AmbiguousMembers.cs:53:21 Mold",
      "AmbiguousMembers.cs:55:22 Make",
      "AmbiguousMembers.cs:57:21 Make",
      "AmbiguousMembers.cs:5:17 Cells",
      "AmbiguousMembers.cs:60:24 Money",
      "AmbiguousMembers.cs:62:16 Cents",
      "AmbiguousMembers.cs:81:15 Stamp",
      "AmbiguousMembers.cs:81:25 Value",
      "AmbiguousMembers.cs:83:15 Stamp",
      "AmbiguousMembers.cs:83:28 Value",
      "AmbiguousMembers.cs:8:21 Reel",
      "AttributedFields.cs:11:32 Changed",
      "AttributedFields.cs:14:22 Left",
      "AttributedFields.cs:14:28 Right",
      "AttributedFields.cs:16:22 Base",
      "AttributedFields.cs:16:32 Derived",
      "AttributedFields.cs:19:25 Label",
      "AttributedFields.cs:5:21 AttributedFields",
      "AttributedFields.cs:8:22 Tag",
      "DeclarationFidelity.cs:10:14 Inventory",
      "DeclarationFidelity.cs:12:16 _count",
      "DeclarationFidelity.cs:14:19 Count",
      "DeclarationFidelity.cs:16:28 Peek",
      "DeclarationFidelity.cs:18:19 Describe",
      "DeclarationFidelity.cs:20:16 Schedule",
      "DeclarationFidelity.cs:22:19 Convert",
      "DeclarationFidelity.cs:26:21 ObliviousEcho",
      "DeclarationFidelity.cs:28:25 Echo",
      "DeclarationFidelity.cs:32:21 Doubling",
      "DeclarationFidelity.cs:34:22 Twice",
      "DeclarationFidelity.cs:37:18 IFirstRunner",
      "DeclarationFidelity.cs:39:9 Run",
      "DeclarationFidelity.cs:3:34 SlotReader",
      "DeclarationFidelity.cs:42:18 ISecondRunner",
      "DeclarationFidelity.cs:44:9 Run",
      "DeclarationFidelity.cs:47:21 DualRunner",
      "DeclarationFidelity.cs:49:22 CSharp15TestProject.IFirstRunner.Run",
      "DeclarationFidelity.cs:51:23 CSharp15TestProject.ISecondRunner.Run",
      "DeclarationFidelity.cs:54:22 SampleBuffer",
      "DeclarationFidelity.cs:56:21 Samples",
      "DeclarationFidelity.cs:59:18 IConvertsTo",
      "DeclarationFidelity.cs:5:18 IProducer",
      "DeclarationFidelity.cs:68:21 Reading",
      "DeclarationFidelity.cs:77:22 SplitLedger",
      "DeclarationFidelity.cs:79:16 Dispose",
      "DeclarationFidelity.cs:7:12 Produce",
      "DeclarationFidelity.cs:84:18 Clone",
      "DeclarationFidelity.cs:87:13 Shade",
      "DeclarationFidelity.cs:89:4 Light",
      "DeclarationFidelity.cs:90:4 Dark",
      "DeclarationFidelity.cs:93:21 Guards",
      "DeclarationFidelity.cs:95:36 Missing",
      "DelegateSignatures.cs:3:21 DelegateSignatures",
      "DelegateSignatures.cs:5:24 Selector",
      "DelegateSignatures.cs:7:30 Formatter",
      "EnumsAndDelegates.cs:11:14 Dispatcher",
      "EnumsAndDelegates.cs:13:16 Priority",
      "EnumsAndDelegates.cs:15:7 Low",
      "EnumsAndDelegates.cs:16:7 High",
      "EnumsAndDelegates.cs:19:25 Handler",
      "EnumsAndDelegates.cs:3:13 Channel",
      "EnumsAndDelegates.cs:5:4 Email",
      "EnumsAndDelegates.cs:6:4 Sms",
      "EnumsAndDelegates.cs:9:22 Filter",
      "ExtensionBlockHeaders.cs:10:29 Empty",
      "ExtensionBlockHeaders.cs:16:4 extension<TKey, TValue>(IDictionary<TKey, TValue>)",
      "ExtensionBlockHeaders.cs:20:21 GetOrCreate",
      "ExtensionBlockHeaders.cs:22:19 Spare",
      "ExtensionBlockHeaders.cs:3:21 CatalogExtensions",
      "ExtensionBlockHeaders.cs:8:4 extension(Catalog)",
      "ExtensionContainers.cs:10:21 StaleExtensions",
      "ExtensionContainers.cs:12:22 Tripled",
      "ExtensionContainers.cs:14:4 extension(long)",
      "ExtensionContainers.cs:16:19 Halved",
      "ExtensionContainers.cs:20:21 LoopExtensions",
      "ExtensionContainers.cs:22:22 Squared",
      "ExtensionContainers.cs:24:22 Times",
      "ExtensionContainers.cs:27:21 SeededExtensions",
      "ExtensionContainers.cs:29:32 Seed",
      "ExtensionContainers.cs:33:22 Salted",
      "ExtensionContainers.cs:36:21 ContainerUsage",
      "ExtensionContainers.cs:38:22 Score",
      "ExtensionContainers.cs:3:21 WordExtensions",
      "ExtensionContainers.cs:5:22 Vowels",
      "ExtensionContainers.cs:7:25 Shout",
      "ExtensionIndexer.cs:10:4 extension(Grid)",
      "ExtensionIndexer.cs:12:18 this[]",
      "ExtensionIndexer.cs:16:21 GridUsage",
      "ExtensionIndexer.cs:18:22 SumThreeCorners",
      "ExtensionIndexer.cs:3:21 Grid",
      "ExtensionIndexer.cs:5:18 Cells",
      "ExtensionIndexer.cs:8:21 GridExtensions",
      "ExtensionMembers.cs:11:28 Blank",
      "ExtensionMembers.cs:14:4 extension(int)",
      "ExtensionMembers.cs:16:19 IsEven",
      "ExtensionMembers.cs:18:18 Doubled",
      "ExtensionMembers.cs:21:4 extension<T>(IEnumerable<T>)",
      "ExtensionMembers.cs:23:19 IsEmpty",
      "ExtensionMembers.cs:27:21 TextUsage",
      "ExtensionMembers.cs:29:25 Quiet",
      "ExtensionMembers.cs:3:21 TextExtensions",
      "ExtensionMembers.cs:5:4 extension(string)",
      "ExtensionMembers.cs:7:19 IsShout",
      "ExtensionMembers.cs:9:21 Whisper",
      "ImplicitTypeUses.cs:10:21 NamedOnly",
      "ImplicitTypeUses.cs:15:21 MarkerAttribute",
      "ImplicitTypeUses.cs:18:21 ImplicitTypeUsage",
      "ImplicitTypeUses.cs:20:22 Use",
      "ImplicitTypeUses.cs:5:21 Imported",
      "ImplicitTypeUses.cs:7:22 Answer",
      "IndexerUses.cs:12:21 Score",
      "IndexerUses.cs:14:15 Total",
      "IndexerUses.cs:18:17 Add",
      "IndexerUses.cs:21:21 RowUsage",
      "IndexerUses.cs:23:22 Read",
      "IndexerUses.cs:33:24 Combine",
      "IndexerUses.cs:3:21 Row",
      "IndexerUses.cs:41:24 Find",
      "IndexerUses.cs:5:15 Length",
      "IndexerUses.cs:7:15 this[]",
      "IndexerUses.cs:9:15 Slice",
      "IndexerWrites.cs:100:15 Total",
      "IndexerWrites.cs:102:15 Reset",
      "IndexerWrites.cs:111:24 Move",
      "IndexerWrites.cs:113:23 Echo",
      "IndexerWrites.cs:13:15 Count",
      "IndexerWrites.cs:16:21 RefSlots",
      "IndexerWrites.cs:18:27 _values",
      "IndexerWrites.cs:20:19 this[]",
      "IndexerWrites.cs:23:21 Shelf",
      "IndexerWrites.cs:25:29 _rows",
      "IndexerWrites.cs:27:17 this[]",
      "IndexerWrites.cs:29:17 Front",
      "IndexerWrites.cs:32:21 Board",
      "IndexerWrites.cs:34:18 Cells",
      "IndexerWrites.cs:37:21 BoardExtensions",
      "IndexerWrites.cs:39:4 extension(Board)",
      "IndexerWrites.cs:3:21 Slots",
      "IndexerWrites.cs:41:18 this[]",
      "IndexerWrites.cs:49:21 IndexerWriteUsage",
      "IndexerWrites.cs:51:23 WriteSlots",
      "IndexerWrites.cs:5:28 _values",
      "IndexerWrites.cs:70:24 FillShelf",
      "IndexerWrites.cs:72:22 PassRefs",
      "IndexerWrites.cs:7:16 this[]",
      "IndexerWrites.cs:80:22 WriteBoard",
      "IndexerWrites.cs:91:24 Fill",
      "IndexerWrites.cs:93:24 Bump",
      "IndexerWrites.cs:95:23 Peek",
      "IndexerWrites.cs:98:21 Ledger",
      "InheritedMembers.cs:102:14 Siren",
      "InheritedMembers.cs:104:34 Rang",
      "InheritedMembers.cs:106:16 Ring",
      "InheritedMembers.cs:11:15 Sides",
      "InheritedMembers.cs:13:25 Unit",
      "InheritedMembers.cs:15:13 Pick",
      "InheritedMembers.cs:17:13 Pick",
      "InheritedMembers.cs:19:16 Secret",
      "InheritedMembers.cs:22:14 Rhombus",
      "InheritedMembers.cs:24:27 Area",
      "InheritedMembers.cs:26:22 Scale",
      "InheritedMembers.cs:28:22 Sides",
      "InheritedMembers.cs:30:17 Pick",
      "InheritedMembers.cs:33:14 Gauge",
      "InheritedMembers.cs:35:21 Max",
      "InheritedMembers.cs:37:22 Count",
      "InheritedMembers.cs:39:23 Reset",
      "InheritedMembers.cs:3:14 Figure",
      "InheritedMembers.cs:41:17 Dial",
      "InheritedMembers.cs:43:24 Tools",
      "InheritedMembers.cs:45:16 Turn",
      "InheritedMembers.cs:47:16 Push",
      "InheritedMembers.cs:49:15 Level",
      "InheritedMembers.cs:51:15 this[]",
      "InheritedMembers.cs:53:15 Mode",
      "InheritedMembers.cs:55:16 Store",
      "InheritedMembers.cs:57:16 Pair",
      "InheritedMembers.cs:59:16 Many",
      "InheritedMembers.cs:5:26 Area",
      "InheritedMembers.cs:61:16 Shift",
      "InheritedMembers.cs:64:14 Barometer",
      "InheritedMembers.cs:66:16 Turn",
      "InheritedMembers.cs:68:16 Push",
      "InheritedMembers.cs:70:19 Level",
      "InheritedMembers.cs:72:19 this[]",
      "InheritedMembers.cs:74:20 Mode",
      "InheritedMembers.cs:76:20 Store",
      "InheritedMembers.cs:78:20 Pair",
      "InheritedMembers.cs:7:18 Scale",
      "InheritedMembers.cs:80:20 Many",
      "InheritedMembers.cs:83:14 Middle",
      "InheritedMembers.cs:85:27 Shift",
      "InheritedMembers.cs:88:14 Bottom",
      "InheritedMembers.cs:90:15 Ticket",
      "InheritedMembers.cs:90:26 Id",
      "InheritedMembers.cs:92:27 ToString",
      "InheritedMembers.cs:95:15 VipTicket",
      "InheritedMembers.cs:95:37 Seat",
      "InheritedMembers.cs:97:14 Alarm",
      "InheritedMembers.cs:99:33 Rang",
      "InheritedMembers.cs:9:18 Scale",
      "LabeledLoops.cs:23:22 CountRowsWithoutZero",
      "LabeledLoops.cs:3:21 LabeledLoops",
      "LabeledLoops.cs:41:22 FindNegativeRow",
      "LabeledLoops.cs:5:22 CountRowsBeforeNegative",
      "ModuleInitializers.cs:5:21 ModuleSetup",
      "ModuleInitializers.cs:8:23 Initialize",
      "MultiLineSignatures.cs:17:16 _disposals",
      "MultiLineSignatures.cs:19:32 Disposed",
      "MultiLineSignatures.cs:30:15 Count",
      "MultiLineSignatures.cs:33:21 Group",
      "MultiLineSignatures.cs:3:18 ILedger",
      "MultiLineSignatures.cs:41:18 this[]",
      "MultiLineSignatures.cs:44:16 Dispose",
      "MultiLineSignatures.cs:51:25 Folder",
      "MultiLineSignatures.cs:57:21 LedgerExtensions",
      "MultiLineSignatures.cs:59:4 extension<TKey, TValue>(Ledger<TKey, TValue>)",
      "MultiLineSignatures.cs:5:8 Count",
      "MultiLineSignatures.cs:63:19 IsEmpty",
      "MultiLineSignatures.cs:8:14 Ledger",
      "NullConditionalIndexers.cs:104:23 RentDirectly",
      "NullConditionalIndexers.cs:13:19 Next",
      "NullConditionalIndexers.cs:16:21 Ward",
      "NullConditionalIndexers.cs:18:30 _lockers",
      "NullConditionalIndexers.cs:20:18 this[]",
      "NullConditionalIndexers.cs:22:16 Self",
      "NullConditionalIndexers.cs:24:17 Parent",
      "NullConditionalIndexers.cs:27:21 Crate",
      "NullConditionalIndexers.cs:29:26 _items",
      "NullConditionalIndexers.cs:31:14 this[]",
      "NullConditionalIndexers.cs:37:16 Empty",
      "NullConditionalIndexers.cs:3:21 Locker",
      "NullConditionalIndexers.cs:40:21 WardExtensions",
      "NullConditionalIndexers.cs:42:4 extension(Ward)",
      "NullConditionalIndexers.cs:44:18 this[]",
      "NullConditionalIndexers.cs:52:18 IStall",
      "NullConditionalIndexers.cs:54:8 this[]",
      "NullConditionalIndexers.cs:57:21 Stall",
      "NullConditionalIndexers.cs:59:15 this[]",
      "NullConditionalIndexers.cs:5:28 _slots",
      "NullConditionalIndexers.cs:66:21 Kiosk",
      "NullConditionalIndexers.cs:68:15 this[]",
      "NullConditionalIndexers.cs:75:21 NullConditionalIndexerUsage",
      "NullConditionalIndexers.cs:77:23 Use",
      "NullConditionalIndexers.cs:7:16 this[]",
      "NullConditionalIndexers.cs:97:23 Rent",
      "OutlineFidelity.cs:11:8 this[]",
      "OutlineFidelity.cs:14:18 IScale",
      "OutlineFidelity.cs:16:8 Read",
      "OutlineFidelity.cs:19:18 ISummable",
      "OutlineFidelity.cs:26:21 Dial",
      "OutlineFidelity.cs:28:14 CSharp15TestProject.IDial.Read",
      "OutlineFidelity.cs:30:15 CSharp15TestProject.IScale.Read",
      "OutlineFidelity.cs:32:14 CSharp15TestProject.IDial.Level",
      "OutlineFidelity.cs:34:30 CSharp15TestProject.IDial.Changed",
      "OutlineFidelity.cs:36:14 CSharp15TestProject.IDial.this[]",
      "OutlineFidelity.cs:3:18 IDial",
      "OutlineFidelity.cs:43:31 Credit",
      "OutlineFidelity.cs:43:46 Amount",
      "OutlineFidelity.cs:54:21 Receipt",
      "OutlineFidelity.cs:54:36 Number",
      "OutlineFidelity.cs:56:22 Packet",
      "OutlineFidelity.cs:58:21 Data",
      "OutlineFidelity.cs:5:8 Read",
      "OutlineFidelity.cs:60:22 Header",
      "OutlineFidelity.cs:60:33 Footer",
      "OutlineFidelity.cs:64:14 Archived",
      "OutlineFidelity.cs:66:25 Unit",
      "OutlineFidelity.cs:71:16 Save",
      "OutlineFidelity.cs:73:16 Store",
      "OutlineFidelity.cs:79:16 Measure",
      "OutlineFidelity.cs:7:8 Level",
      "OutlineFidelity.cs:84:16 Label",
      "OutlineFidelity.cs:91:13 Tone",
      "OutlineFidelity.cs:94:4 Low",
      "OutlineFidelity.cs:96:4 High",
      "OutlineFidelity.cs:9:24 Changed",
      "PartialBaseLists.cs:10:22 MergedLedger",
      "PartialBaseLists.cs:12:15 CompareTo",
      "PartialBaseLists.cs:17:16 Dispose",
      "PartialBaseLists.cs:20:24 Entry",
      "PartialBaseLists.cs:20:34 Id",
      "PartialBaseLists.cs:22:23 PostedEntry",
      "PartialBaseLists.cs:26:23 DraftEntry",
      "PartialBaseLists.cs:30:15 CompareTo",
      "PartialBaseLists.cs:33:21 ConstructedUses",
      "PartialBaseLists.cs:35:36 Stock",
      "PartialBaseLists.cs:37:28 Numbers",
      "PartialBaseLists.cs:39:25 Anything",
      "PartialBaseLists.cs:5:14 LedgerBase",
      "PartialBaseLists.cs:7:15 Seed",
      "PrimaryConstructors.cs:12:22 Pair",
      "PrimaryConstructors.cs:12:31 Left",
      "PrimaryConstructors.cs:12:41 Right",
      "PrimaryConstructors.cs:14:14 Counter",
      "PrimaryConstructors.cs:16:14 Tally",
      "PrimaryConstructors.cs:18:15 Initial",
      "PrimaryConstructors.cs:21:18 IMeasurable",
      "PrimaryConstructors.cs:23:8 Measure",
      "PrimaryConstructors.cs:3:15 Point",
      "PrimaryConstructors.cs:3:25 X",
      "PrimaryConstructors.cs:3:32 Y",
      "PrimaryConstructors.cs:5:14 Logger",
      "PrimaryConstructors.cs:7:15 Size",
      "PrimaryConstructors.cs:9:15 Area",
      "RefReceiverExtensions.cs:10:4 extension(Meter)",
      "RefReceiverExtensions.cs:12:18 Copy",
      "RefReceiverExtensions.cs:15:4 extension(ref Meter)",
      "RefReceiverExtensions.cs:17:19 Advance",
      "RefReceiverExtensions.cs:20:4 extension(in Meter)",
      "RefReceiverExtensions.cs:22:18 Peek",
      "RefReceiverExtensions.cs:25:4 extension(ref readonly Meter)",
      "RefReceiverExtensions.cs:27:18 Look",
      "RefReceiverExtensions.cs:30:4 extension(scoped ref Meter)",
      "RefReceiverExtensions.cs:32:19 Reset",
      "RefReceiverExtensions.cs:35:4 extension(scoped Span<int>)",
      "RefReceiverExtensions.cs:37:18 Size",
      "RefReceiverExtensions.cs:3:15 Meter",
      "RefReceiverExtensions.cs:40:4 extension<T>(ref T)",
      "RefReceiverExtensions.cs:42:19 Clear",
      "RefReceiverExtensions.cs:5:15 Reading",
      "RefReceiverExtensions.cs:8:21 MeterExtensions",
      "SelfReferences.cs:14:29 CrefPartial",
      "SelfReferences.cs:20:22 Pong",
      "SelfReferences.cs:23:21 QualifiedSelf",
      "SelfReferences.cs:25:22 First",
      "SelfReferences.cs:27:22 Second",
      "SelfReferences.cs:30:21 Chain",
      "SelfReferences.cs:32:18 Next",
      "SelfReferences.cs:6:21 CrefSelf",
      "SelfReferences.cs:8:22 Ping",
      "SelfReferencingMembers.cs:13:22 Stored",
      "SelfReferencingMembers.cs:15:22 Countdown",
      "SelfReferencingMembers.cs:20:22 Citer",
      "SelfReferencingMembers.cs:22:22 Cited",
      "SelfReferencingMembers.cs:25:21 Token",
      "SelfReferencingMembers.cs:27:15 Value",
      "SelfReferencingMembers.cs:29:24 Create",
      "SelfReferencingMembers.cs:32:21 TokenUsage",
      "SelfReferencingMembers.cs:34:22 Read",
      "SelfReferencingMembers.cs:3:21 SelfCitedMembers",
      "SelfReferencingMembers.cs:8:22 Echo",
      "Unions.cs:11:25 Area",
      "Unions.cs:3:15 Circle",
      "Unions.cs:3:29 Radius",
      "Unions.cs:5:15 Square",
      "Unions.cs:5:29 Side",
      "Unions.cs:7:14 Shape",
      "Unions.cs:9:21 ShapeMath",
   ];

   [Fact]
   public async Task SearchSymbols_Wildcard_ListsOnlyExplicitlyDeclaredSymbols()
   {
      var tool = new SearchSymbolsTool(_fixture.Workspace);

      var result = await tool.SearchSymbolsAsync("*", maxResults: 500, cancellationToken: CT);

      Assert.False(result.Data.HasMore);
      Assert.Equal(AllExplicitlyDeclaredSymbols, result.Data.Items.Select(FormatPositionAndName).Order(StringComparer.Ordinal));
   }

   /// <summary>
   /// find_symbols also lists constructors, which search_symbols does not.
   /// </summary>
   [Fact]
   public async Task SearchSymbols_Wildcard_ListsWhatFindSymbolsListsExceptConstructors()
   {
      var search = new SearchSymbolsTool(_fixture.Workspace);
      var find = new FindSymbolsTool(_fixture.Workspace);

      var searched = await search.SearchSymbolsAsync("*", maxResults: 500, cancellationToken: CT);
      var found = await find.FindSymbolsAsync("*", maxResults: 500, cancellationToken: CT);

      Assert.False(searched.Data.HasMore);
      Assert.False(found.Data.HasMore);
      Assert.Equal(
         found.Data.Items.Where(s => s is not ConstructorSymbolResponse).Select(FormatPositionAndName).Order(StringComparer.Ordinal),
         searched.Data.Items.Select(FormatPositionAndName).Order(StringComparer.Ordinal));
   }

   [Fact]
   public async Task SearchSymbols_ExplicitImplementations_ListNoAccessorsOrOperators()
   {
      var search = new SearchSymbolsTool(_fixture.Workspace);
      var find = new FindSymbolsTool(_fixture.Workspace);

      var searched = await search.SearchSymbolsAsync("*", maxResults: 500, cancellationToken: CT);
      var found = await find.FindSymbolsAsync("*", maxResults: 500, cancellationToken: CT);

      string[] expected =
      [
         "OutlineFidelity.cs:26:21 Dial",
         "OutlineFidelity.cs:28:14 CSharp15TestProject.IDial.Read",
         "OutlineFidelity.cs:30:15 CSharp15TestProject.IScale.Read",
         "OutlineFidelity.cs:32:14 CSharp15TestProject.IDial.Level",
         "OutlineFidelity.cs:34:30 CSharp15TestProject.IDial.Changed",
         "OutlineFidelity.cs:36:14 CSharp15TestProject.IDial.this[]",
      ];
      Assert.Equal(expected, searched.Data.Items.Where(IsInDial).Select(FormatPositionAndName));
      Assert.Equal(expected, found.Data.Items.Where(IsInDial).Select(FormatPositionAndName));

      static bool IsInDial(SymbolResponse symbol) =>
         symbol.Location is { FilePath: "OutlineFidelity.cs", Line: >= 26 and <= 41 };
   }

   private static string FormatPositionAndName(SymbolResponse symbol) =>
      symbol.Location is { } location
         ? $"{location.FilePath}:{location.Line}:{location.Column} {symbol.Name}"
         : throw new InvalidOperationException($"{symbol.Name} has no location");

   [Fact]
   public async Task ContainingMember_InsideExtensionBlock_ReturnsBlock()
   {
      var tool = new GetContainingMemberTool(_fixture.Workspace);

      // Line 11 holds the block's opening brace, outside any member.
      var result = await tool.GetContainingMemberAsync(
         _fixture.GetPath("ExtensionIndexer.cs"), 11, 4, includeHierarchy: true, cancellationToken: CT);

      Assert.Equal(GridExtensionBlock, result.Data.Member);
      var hierarchy = result.Data.Hierarchy ?? throw new InvalidOperationException("includeHierarchy lists the hierarchy");
      Assert.Equal(
         [(typeof(ExtensionSymbolResponse), "extension(Grid)"), (typeof(ClassSymbolResponse), "GridExtensions"), (typeof(NamespaceSymbolResponse), "CSharp15TestProject")],
         hierarchy.Select(s => (s.GetType(), s.Name)));
   }

   [Fact]
   public async Task FileOverview_ListsExtensionBlocksWithExtensionKind()
   {
      var tool = new GetFileOverviewTool(_fixture.Workspace);

      var result = await tool.GetFileOverviewAsync(_fixture.GetPath("ExtensionMembers.cs"), ct: CT);

      var types = result.Data.Types ?? throw new InvalidOperationException("Compact verbosity lists types");
      Assert.Equal(
         [
            (typeof(ClassSymbolResponse), "TextExtensions"),
            (typeof(ExtensionSymbolResponse), "extension(string)"),
            (typeof(ExtensionSymbolResponse), "extension(int)"),
            (typeof(ExtensionSymbolResponse), "extension<T>(IEnumerable<T>)"),
            (typeof(ClassSymbolResponse), "TextUsage"),
         ],
         types.Select(t => (t.GetType(), t.Name)));
      Assert.Equal(AllExtensionBlocks.Where(b => b.Location?.FilePath == "ExtensionMembers.cs"), types.OfType<ExtensionSymbolResponse>());
   }

   [Fact]
   public async Task FileOverview_ExtensionBlockNames_IncludeReceiverModifiers()
   {
      var tool = new GetFileOverviewTool(_fixture.Workspace);

      var result = await tool.GetFileOverviewAsync(_fixture.GetPath("RefReceiverExtensions.cs"), ct: CT);

      var types = result.Data.Types ?? throw new InvalidOperationException("Compact verbosity lists types");
      Assert.Equal(RefReceiverExtensionBlocks, types.OfType<ExtensionSymbolResponse>());
   }

   private static readonly SymbolResponse[] EnumsAndDelegatesTypes =
   [
      new EnumSymbolResponse
      {
         Name = "Channel",
         Location = new SymbolLocation { FilePath = "EnumsAndDelegates.cs", Line = 3, Column = 13 },
         Accessibility = AccessibilityDto.Public,
         Modifiers = SymbolModifiers.Sealed,
      },
      new DelegateSymbolResponse
      {
         Name = "Filter",
         Location = new SymbolLocation { FilePath = "EnumsAndDelegates.cs", Line = 9, Column = 22 },
         Accessibility = AccessibilityDto.Public,
         Modifiers = SymbolModifiers.Sealed,
      },
      new ClassSymbolResponse
      {
         Name = "Dispatcher",
         Location = new SymbolLocation { FilePath = "EnumsAndDelegates.cs", Line = 11, Column = 14 },
         Accessibility = AccessibilityDto.Public,
      },
      new EnumSymbolResponse
      {
         Name = "Priority",
         Location = new SymbolLocation { FilePath = "EnumsAndDelegates.cs", Line = 13, Column = 16 },
         ContainingType = "CSharp15TestProject.Dispatcher",
         Accessibility = AccessibilityDto.Public,
         Modifiers = SymbolModifiers.Sealed,
      },
      new DelegateSymbolResponse
      {
         Name = "Handler",
         Location = new SymbolLocation { FilePath = "EnumsAndDelegates.cs", Line = 19, Column = 25 },
         ContainingType = "CSharp15TestProject.Dispatcher",
         Accessibility = AccessibilityDto.Public,
         Modifiers = SymbolModifiers.Sealed,
      },
   ];

   [Fact]
   public async Task FileOverview_EnumsAndDelegates_SummaryCountsEveryType()
   {
      var tool = new GetFileOverviewTool(_fixture.Workspace);

      var result = await tool.GetFileOverviewAsync(_fixture.GetPath("EnumsAndDelegates.cs"), ToolResultVerbosity.Summary, CT);

      Assert.Equal(5, result.Data.TypeCount);
      Assert.Null(result.Data.Types);
      Assert.Equal((5, 5), (result.Metadata?.TotalCount, result.Metadata?.ReturnedCount));
   }

   [Fact]
   public async Task FileOverview_EnumsAndDelegates_CompactListsEveryType()
   {
      var tool = new GetFileOverviewTool(_fixture.Workspace);

      var result = await tool.GetFileOverviewAsync(_fixture.GetPath("EnumsAndDelegates.cs"), ToolResultVerbosity.Compact, CT);

      Assert.Null(result.Data.TypeCount);
      Assert.Equal(EnumsAndDelegatesTypes, result.Data.Types);
      Assert.Equal((5, 5), (result.Metadata?.TotalCount, result.Metadata?.ReturnedCount));
   }

   [Fact]
   public async Task FileOverview_EnumsAndDelegates_FullListsEveryType()
   {
      var tool = new GetFileOverviewTool(_fixture.Workspace);

      var result = await tool.GetFileOverviewAsync(_fixture.GetPath("EnumsAndDelegates.cs"), ToolResultVerbosity.Full, CT);

      Assert.Null(result.Data.TypeCount);
      var types = result.Data.Types ?? throw new InvalidOperationException("Full verbosity lists types");
      Assert.Equal(
         [
            EnumsAndDelegatesTypes[0] with { FullyQualifiedName = "CSharp15TestProject.Channel", Declaration = "public enum Channel" },
            EnumsAndDelegatesTypes[1] with { FullyQualifiedName = "CSharp15TestProject.Filter<T>", Declaration = "public delegate bool Filter<T>(T item)" },
            EnumsAndDelegatesTypes[2] with { FullyQualifiedName = "CSharp15TestProject.Dispatcher", Declaration = "public class Dispatcher" },
            EnumsAndDelegatesTypes[3] with { FullyQualifiedName = "CSharp15TestProject.Dispatcher.Priority", Declaration = "public enum Priority" },
            EnumsAndDelegatesTypes[4] with { FullyQualifiedName = "CSharp15TestProject.Dispatcher.Handler", Declaration = "public delegate void Handler(Priority priority)" },
         ],
         types.Select(t => t with { ContainingNamespace = null, TypeParameters = null }));
      Assert.Equal(["CSharp15TestProject"], types.Select(t => t.ContainingNamespace).Distinct());
      Assert.Equal([null, "T", null, null, null], types.Select(t => t.TypeParameters is { } parameters ? string.Join(", ", parameters) : null));
      Assert.Equal((5, 5), (result.Metadata?.TotalCount, result.Metadata?.ReturnedCount));
   }

   /// <summary>
   /// Delegates whose parameters are ref, out, in, params, defaulted or nullable, which a delegate declaration
   /// shows with the conventions of a method declaration.
   /// </summary>
   [Fact]
   public async Task FileOverview_EnumsAndDelegates_FullDelegateDeclarationsShowSignature()
   {
      var tool = new GetFileOverviewTool(_fixture.Workspace);

      var result = await tool.GetFileOverviewAsync(_fixture.GetPath("DelegateSignatures.cs"), ToolResultVerbosity.Full, CT);

      var types = result.Data.Types ?? throw new InvalidOperationException("Full verbosity lists types");
      Assert.Equal(
         [
            (typeof(ClassSymbolResponse), "public static class DelegateSignatures"),
            (typeof(DelegateSymbolResponse), "public delegate int Selector(ref int first, out int second, in int third, params int[] rest)"),
            (typeof(DelegateSymbolResponse), "internal delegate string? Formatter<T>(T? value, string? format = \"G\", int width = 0) where T : struct"),
         ],
         types.Select(t => (t.GetType(), t.Declaration)));
   }

   [Theory]
   [InlineData(14, 19, "public ref int Count()")]
   [InlineData(16, 28, "public ref readonly int Peek()")]
   [InlineData(18, 19, "public string? Describe(string? prefix, string suffix, int? width = null)")]
   [InlineData(20, 16, "public void Schedule(DayOfWeek day = DayOfWeek.Friday, decimal rate = 2.5m, float scale = 1.5f, double limit = double.PositiveInfinity, FileShare share = FileShare.Read | FileShare.Delete)")]
   [InlineData(22, 19, "public TResult Convert<TResult>(TItem item) where TResult : struct")]
   [InlineData(28, 25, "public static string Echo(string text)")]
   [InlineData(34, 22, "public static int Twice(this int value)")]
   [InlineData(49, 22, "void IFirstRunner.Run()")]
   [InlineData(56, 21, "public fixed int Samples[4]")]
   [InlineData(70, 41, "static explicit IConvertsTo<Reading>.operator int(Reading value)")]
   [InlineData(72, 41, "static explicit IConvertsTo<Reading>.operator checked int(Reading value)")]
   [InlineData(74, 41, "static implicit IConvertsTo<Reading>.operator long(Reading value)")]
   [InlineData(95, 18, "public class ArgumentException : SystemException")]
   public async Task FullDeclaration_Fidelity_MemberDeclarations(int line, int column, string expected)
   {
      var tool = new GetSymbolInfoTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.GetSymbolInfoAsync(
         new LookupByPosition { FilePath = _fixture.GetPath("DeclarationFidelity.cs"), Line = line, Column = column }, cancellationToken: CT);

      Assert.Equal(expected, result.Data.Declaration);
   }

   [Fact]
   public async Task FullDeclaration_Fidelity_TypeDeclarations()
   {
      var tool = new GetFileOverviewTool(_fixture.Workspace);

      var result = await tool.GetFileOverviewAsync(_fixture.GetPath("DeclarationFidelity.cs"), ToolResultVerbosity.Full, CT);

      var types = result.Data.Types ?? throw new InvalidOperationException("Full verbosity lists types");
      Assert.Equal(
         [
            "public delegate ref readonly int SlotReader<T>(T[] items, string? label) where T : struct",
            "public interface IProducer<in TInput, out TOutput> where TOutput : class",
            "public class Inventory<TItem> where TItem : notnull, IComparable<TItem>",
            "public static class ObliviousEcho",
            "public static class Doubling",
            "public interface IFirstRunner",
            "public interface ISecondRunner",
            "public sealed class DualRunner : IFirstRunner, ISecondRunner",
            "public unsafe struct SampleBuffer",
            "public interface IConvertsTo<TSelf> where TSelf : IConvertsTo<TSelf>",
            "public sealed class Reading : IConvertsTo<Reading>",
            "public class SplitLedger : IDisposable, ICloneable",
            "public class SplitLedger : IDisposable, ICloneable",
            "public enum Shade : byte",
            "public static class Guards",
         ],
         types.Select(t => t.Declaration));
   }

   [Fact]
   public async Task FullDeclaration_Fidelity_PartialBaseLists()
   {
      var tool = new GetFileOverviewTool(_fixture.Workspace);

      var result = await tool.GetFileOverviewAsync(_fixture.GetPath("PartialBaseLists.cs"), ToolResultVerbosity.Full, CT);

      var types = result.Data.Types ?? throw new InvalidOperationException("Full verbosity lists types");
      Assert.Equal(
         [
            "public class LedgerBase(int seed)",
            "public class MergedLedger(int seed) : LedgerBase(seed), IComparable<int>, IDisposable",
            "public class MergedLedger(int seed) : LedgerBase(seed), IComparable<int>, IDisposable",
            "public abstract record Entry(int Id)",
            "public record PostedEntry(int Id) : Entry(Id), IEquatable<PostedEntry>",
            "public record PostedEntry(int Id) : Entry(Id), IEquatable<PostedEntry>",
            "public record DraftEntry(int Id) : Entry(Id), IComparable<DraftEntry>",
            "public record DraftEntry(int Id) : Entry(Id), IComparable<DraftEntry>",
            "public static class ConstructedUses",
         ],
         types.Select(t => t.Declaration));
   }

   /// <summary>
   /// Return types that name a constructed generic, a type from a referenced assembly and a special type.
   /// </summary>
   [Theory]
   [InlineData(35, 18, "public class Inventory<TItem> where TItem : notnull, IComparable<TItem>")]
   [InlineData(37, 18, "public class List<T> : IList<T>, IReadOnlyList<T>, IList")]
   [InlineData(39, 18, "public class Object")]
   public async Task FullDeclaration_Fidelity_ReferencedTypeDeclarations(int line, int column, string expected)
   {
      var tool = new GetSymbolInfoTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.GetSymbolInfoAsync(
         new LookupByPosition { FilePath = _fixture.GetPath("PartialBaseLists.cs"), Line = line, Column = column }, cancellationToken: CT);

      Assert.Equal(expected, result.Data.Declaration);
   }

   [Theory]
   [InlineData("PrimaryConstructors.cs", new[] { "public record Point(int X, int Y)", "public class Logger(string name)", "public struct Size(int width, int height)", "public record struct Pair(int Left, int Right)", "public class Counter(int start) : Tally(start)", "public class Tally(int initial)", "public interface IMeasurable" })]
   [InlineData("MultiLineSignatures.cs", new[] { "public interface ILedger", "public class Ledger<TKey, TValue>(IDictionary<TKey, TValue> entries, string title = \"Ledger  one\") : ILedger, IDisposable where TKey : notnull, IComparable<TKey>", "public delegate TResult Folder<in TItem, TResult>(TResult seed, TItem item) where TResult : struct", "public static class LedgerExtensions", "extension<TKey, TValue>(Ledger<TKey, TValue> ledger) where TKey : notnull, IComparable<TKey>" })]
   public async Task FullDeclaration_Fidelity_PrimaryConstructorsAndBaseLists(string fileName, string[] expected)
   {
      var tool = new GetFileOverviewTool(_fixture.Workspace);

      var result = await tool.GetFileOverviewAsync(_fixture.GetPath(fileName), ToolResultVerbosity.Full, CT);

      var types = result.Data.Types ?? throw new InvalidOperationException("Full verbosity lists types");
      Assert.Equal(expected, types.Select(t => t.Declaration));
   }

   [Theory]
   [InlineData("ExtensionMembers.cs", new[] { "extension(string text)", "extension(int number)", "extension<T>(IEnumerable<T> items)" })]
   [InlineData("ExtensionBlockHeaders.cs", new[] { "extension(Catalog)", "extension<TKey, TValue>(IDictionary<TKey, TValue> map) where TKey : notnull where TValue : class, new()" })]
   [InlineData("RefReceiverExtensions.cs", new[] { "extension(Meter meter)", "extension(ref Meter meter)", "extension(in Meter meter)", "extension(ref readonly Meter meter)", "extension(scoped ref Meter meter)", "extension(scoped Span<int> span)", "extension<T>(ref T value) where T : struct" })]
   public async Task FullDeclaration_Fidelity_ExtensionBlockDeclarations(string fileName, string[] expected)
   {
      var tool = new GetFileOverviewTool(_fixture.Workspace);

      var result = await tool.GetFileOverviewAsync(_fixture.GetPath(fileName), ToolResultVerbosity.Full, CT);

      var types = result.Data.Types ?? throw new InvalidOperationException("Full verbosity lists types");
      Assert.Equal(expected, types.OfType<ExtensionSymbolResponse>().Select(t => t.Declaration));
   }

   [Fact]
   public async Task GoToDefinition_ExtensionBlock_HasExtensionKindAndName()
   {
      var tool = new GoToDefinitionTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.GoToDefinitionAsync(GridExtensionBlockPosition, cancellationToken: CT);

      Assert.Equal(GridExtensionBlock, result.Data.Symbol);
      Assert.Equal(
         [new SourceSpan { FilePath = "ExtensionIndexer.cs", StartLine = 10, StartColumn = 4, EndLine = 10, EndColumn = 13 }],
         result.Data.Definitions);
   }

   [Fact]
   public async Task FindReferences_ExtensionBlock_HasExtensionKindAndName()
   {
      var tool = new FindReferencesTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.FindReferencesAsync(GridExtensionBlockPosition, cancellationToken: CT);

      Assert.Equal(GridExtensionBlock, result.Data.Symbol);
      Assert.Equal(0, result.Data.TotalReferences);
      Assert.NotNull(result.Data.References);
      Assert.Empty(result.Data.References);
   }

   [Fact]
   public async Task TypeMembers_ExtensionBlockName_ListsBlockByNameAndKind()
   {
      var tool = new GetTypeMembersTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.GetTypeMembersAsync(new LookupByName { Name = "GridExtensions" }, cancellationToken: CT);

      Assert.Equal(
         [new MemberInfo { Name = "extension(Grid)", Kind = "Extension", Accessibility = "Public", IsStatic = false, Line = 10 }],
         result.Data.Members);
      Assert.Equal(new Dictionary<string, int> { ["Extension"] = 1 }, result.Data.MemberCounts);
      Assert.Equal(1, result.Data.TotalMembers);
   }

   [Fact]
   public async Task TypeMembers_ExtensionBlockName_ListsEachBlock()
   {
      var tool = new GetTypeMembersTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.GetTypeMembersAsync(
         new LookupByName { Name = "MeterExtensions" }, verbosity: ToolResultVerbosity.Summary, cancellationToken: CT);

      Assert.Equal(
         RefReceiverExtensionBlocks.Select(b => new MemberInfo { Name = b.Name, Kind = "Extension" }),
         result.Data.Members);
      Assert.Equal(7, result.Data.TotalMembers);
   }

   [Fact]
   public async Task TypeMembers_ExtensionBlockName_ListsBlocksWithTheSameHeader()
   {
      var tool = new GetTypeMembersTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));
      var batch = new GetTypeMembersBatchTool(_fixture.Workspace);

      var result = await tool.GetTypeMembersAsync(new LookupByName { Name = "SpoolExtensions" }, cancellationToken: CT);
      var batchResult = await batch.GetTypeMembersBatchAsync(["SpoolExtensions"], cancellationToken: CT);

      (string, string, int?)[] expected = [("extension(Tape)", "Extension", 15), ("extension(Reel)", "Extension", 24), ("extension(Tape)", "Extension", 33)];
      Assert.Equal(expected, result.Data.Members.Select(m => (m.Name, m.Kind, m.Line)));
      Assert.Equal(3, result.Data.TotalMembers);
      Assert.Equal(expected, Assert.Single(batchResult.Data.Results).Members.Select(m => (m.Name, m.Kind, m.Line)));
   }

   [Fact]
   public async Task TypeMembers_Overloads_ListsEachOverload()
   {
      var tool = new GetTypeMembersTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));
      var batch = new GetTypeMembersBatchTool(_fixture.Workspace);

      var result = await tool.GetTypeMembersAsync(new LookupByName { Name = "Box" }, cancellationToken: CT);
      var batchResult = await batch.GetTypeMembersBatchAsync(["Box"], cancellationToken: CT);

      (string, string?)[] expected = [("Get", "T Box<T>.Get()"), ("Get", "T Box<T>.Get(int copies)")];
      Assert.Equal(expected, result.Data.Members.Where(m => m.Kind == "Method").Select(m => (m.Name, m.Signature)));
      Assert.Equal(expected, Assert.Single(batchResult.Data.Results).Members.Where(m => m.Kind == "Method").Select(m => (m.Name, m.Signature)));
   }

   /// <summary>
   /// Rhombus overrides or hides <c>Area</c>, <c>Scale(int)</c>, <c>Sides</c> and <c>Pick&lt;T&gt;(T)</c>, so Figure's
   /// other two overloads and its static method come through. Its private and constructor members never do.
   /// </summary>
   [Fact]
   public async Task TypeMembers_IncludeInherited_OmitsOverriddenAndHiddenBaseMembers()
   {
      var tool = new GetTypeMembersTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));
      var batch = new GetTypeMembersBatchTool(_fixture.Workspace);

      var result = await tool.GetTypeMembersAsync(new LookupByName { Name = "Rhombus" }, includeInherited: true, cancellationToken: CT);
      var batchResult = await batch.GetTypeMembersBatchAsync(["Rhombus"], includeInherited: true, cancellationToken: CT);

      (string, string?, int?)[] expected =
      [
         ("Area", "double Rhombus.Area()", 24),
         ("Scale", "double Rhombus.Scale(int factor)", 26),
         ("Sides", null, 28),
         ("Pick", "U Rhombus.Pick<U>(U value)", 30),
         ("Scale", "double Figure.Scale(double factor)", 9),
         ("Unit", "Figure Figure.Unit()", 13),
         ("Pick", "T Figure.Pick<T>(T value, int count)", 17),
      ];
      Assert.Equal(expected, result.Data.Members.Select(m => (m.Name, m.Signature, m.Line)));
      Assert.Equal(7, result.Data.TotalMembers);
      Assert.Equal(expected, Assert.Single(batchResult.Data.Results).Members.Select(m => (m.Name, m.Signature, m.Line)));
   }

   /// <summary>
   /// Barometer hides Gauge's members in every way that does or does not count as hiding: a different ref kind, an
   /// accessor-less property, indexer or method over a property with accessors, and types that differ only by
   /// <c>dynamic</c> or tuple element names.
   /// </summary>
   [Fact]
   public async Task TypeMembers_IncludeInherited_HidesByExactSignatureWithAccessors()
   {
      var members = await InheritedMembersOfAsync("Barometer");

      Assert.Equal(
         [
            ("CSharp15TestProject.Barometer", "Turn", "Method", 66),
            ("CSharp15TestProject.Barometer", "Push", "Method", 68),
            ("CSharp15TestProject.Barometer", "Level", "Property", 70),
            ("CSharp15TestProject.Barometer", "get_Level", "Method", 70),
            ("CSharp15TestProject.Barometer", "this[]", "Property", 72),
            ("CSharp15TestProject.Barometer", "get_Item", "Method", 72),
            ("CSharp15TestProject.Barometer", "Mode", "Method", 74),
            ("CSharp15TestProject.Barometer", "Store", "Method", 76),
            ("CSharp15TestProject.Barometer", "Pair", "Method", 78),
            ("CSharp15TestProject.Barometer", "Many", "Method", 80),
            ("CSharp15TestProject.Gauge", "Max", "Field", 35),
            ("CSharp15TestProject.Gauge", "Count", "Field", 37),
            ("CSharp15TestProject.Gauge", "Reset", "Method", 39),
            ("CSharp15TestProject.Gauge", "Dial", "NamedType", 41),
            ("CSharp15TestProject.Gauge", "Tools", "NamedType", 43),
            ("CSharp15TestProject.Gauge", "Turn", "Method", 45),
            ("CSharp15TestProject.Gauge", "Push", "Method", 47),
            ("CSharp15TestProject.Gauge", "Shift", "Method", 61),
         ],
         members);
   }

   [Fact]
   public async Task TypeMembers_IncludeInherited_StaticMemberOfIntermediateBaseHides()
   {
      var members = await InheritedMembersOfAsync("Bottom");

      Assert.Equal([("CSharp15TestProject.Middle", "Shift", "Method", 85)], members.Where(m => m.Name == "Shift"));
   }

   [Fact]
   public async Task TypeMembers_IncludeInherited_CompilerGeneratedOverrideHides()
   {
      var members = await InheritedMembersOfAsync("VipTicket");

      Assert.DoesNotContain(members, m => m.Name == "ToString");
   }

   [Fact]
   public async Task TypeMembers_IncludeInherited_OverriddenEventHidesItsAccessors()
   {
      var members = await InheritedMembersOfAsync("Siren");

      Assert.Equal(
         [("CSharp15TestProject.Siren", "Rang", "Event", 104), ("CSharp15TestProject.Siren", "Ring", "Method", 106)],
         members);
   }

   private async Task<List<(string? ContainingType, string Name, string Kind, int? Line)>> InheritedMembersOfAsync(string typeName)
   {
      var tool = new GetTypeMembersTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));
      var batch = new GetTypeMembersBatchTool(_fixture.Workspace);

      var result = await tool.GetTypeMembersAsync(
         new LookupByName { Name = typeName }, includeInherited: true, verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);
      var batchResult = await batch.GetTypeMembersBatchAsync(
         [typeName], includeInherited: true, maxResultsPerType: 500, verbosity: ToolResultVerbosity.Full, cancellationToken: CT);

      var members = result.Data.Members.Select(m => (m.ContainingType, m.Name, m.Kind, m.Line)).ToList();
      Assert.Equal(members, Assert.Single(batchResult.Data.Results).Members.Select(m => (m.ContainingType, m.Name, m.Kind, m.Line)));
      Assert.Equal(members.Count, result.Data.TotalMembers);
      return members;
   }

   [Fact]
   public async Task TypeMembersBatch_ExtensionBlockName_ListsBlockByNameAndKind()
   {
      var tool = new GetTypeMembersBatchTool(_fixture.Workspace);

      var result = await tool.GetTypeMembersBatchAsync(["GridExtensions"], cancellationToken: CT);

      var type = Assert.Single(result.Data.Results);
      Assert.Equal(
         [new MemberInfo { Name = "extension(Grid)", Kind = "Extension", Accessibility = "Public", IsStatic = false, Line = 10 }],
         type.Members);
      Assert.Empty(result.Data.Errors);
   }

   [Fact]
   public async Task TypeOverview_ExtensionBlockName_HasBlockName()
   {
      var tool = new GetTypeOverviewTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.GetTypeOverviewAsync(GridExtensionBlockPosition, cancellationToken: CT);

      Assert.Equal("extension(Grid)", result.Data.Name);
      Assert.Equal("CSharp15TestProject.GridExtensions.extension(CSharp15TestProject.Grid)", result.Data.FullyQualifiedName);
      Assert.Equal("Extension", result.Data.TypeKind);
      Assert.Equal(new TypeLocationInfo { FilePath = "ExtensionIndexer.cs", Line = 10 }, result.Data.Location);
   }

   [Fact]
   public async Task ChangeImpact_ExtensionBlockName_HasBlockNameAndExtensionKind()
   {
      var tool = new AnalyzeChangeImpactTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.AnalyzeChangeImpactAsync(GridExtensionBlockPosition, ChangeType.Delete, cancellationToken: CT);

      Assert.Equal("extension(Grid)", result.Data.SymbolName);
      Assert.Equal(SymbolKindDto.Extension, result.Data.SymbolKind);
      Assert.Equal(0, result.Data.ReferenceCount);
      Assert.True(result.Data.IsSafe);
   }

   [Fact]
   public async Task ChangeImpact_ExtensionBlockName_RenameIsNotSafe()
   {
      var tool = new AnalyzeChangeImpactTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.AnalyzeChangeImpactAsync(GridExtensionBlockPosition, ChangeType.Rename, "Other", cancellationToken: CT);

      Assert.Equal("extension(Grid)", result.Data.SymbolName);
      Assert.False(result.Data.IsSafe);
      Assert.Equal("An extension block has no name to rename", result.Data.BreakingChangeReason);
      Assert.Equal(["Rename the block's receiver parameter or its members instead"], result.Data.Suggestions);
   }

   [Fact]
   public async Task RenameSymbol_ExtensionBlockName_Refuses()
   {
      // The refusal comes before any change, and preview never writes, so the shared fixture stays untouched.
      var workspace = _fixture.Workspace as IWorkspace ?? throw new InvalidOperationException("The fixture workspace is writable");
      var tool = new RenameSymbolTool(workspace, new SymbolLookupService(workspace));

      var error = await Assert.ThrowsAsync<ToolException>(
         () => tool.RenameSymbolAsync(GridExtensionBlockPosition, "Other", preview: true, cancellationToken: CT));

      Assert.Equal(ErrorCodes.NotSupported, error.Code);
      Assert.Equal(
         "extension(Grid) is an extension block, which has no name to rename. Rename its receiver parameter or its members instead.",
         error.Message);
   }

   [Theory]
   [InlineData("CSharp15TestProject.Siren.add_Rang", "add_Rang is an accessor of Rang, which takes its name from it. Rename Rang instead.")]
   [InlineData("CSharp15TestProject.Money.get_Cents", "get_Cents is an accessor of Cents, which takes its name from it. Rename Cents instead.")]
   [InlineData("CSharp15TestProject.Badge.set_Label", "set_Label is an accessor of Label, which takes its name from it. Rename Label instead.")]
   public async Task RenameSymbol_Accessor_Refuses(string name, string expectedMessage)
   {
      // The refusal comes before any change, and preview never writes, so the shared fixture stays untouched.
      var workspace = _fixture.Workspace as IWorkspace ?? throw new InvalidOperationException("The fixture workspace is writable");
      var tool = new RenameSymbolTool(workspace, new SymbolLookupService(workspace));

      var error = await Assert.ThrowsAsync<ToolException>(
         () => tool.RenameSymbolAsync(new LookupByName { Name = name }, "Other", preview: true, cancellationToken: CT));

      Assert.Equal(ErrorCodes.NotSupported, error.Code);
      Assert.Equal(expectedMessage, error.Message);
   }

   [Theory]
   [InlineData("extension*")]
   [InlineData("*)")]
   public async Task SearchSymbols_ExtensionBlockName_QueryMatchesBlockName(string query)
   {
      var tool = new SearchSymbolsTool(_fixture.Workspace);

      var result = await tool.SearchSymbolsAsync(query, maxResults: 500, cancellationToken: CT);

      Assert.False(result.Data.HasMore);
      Assert.Equal(AllExtensionBlocks, result.Data.Items);
   }

   [Theory]
   [InlineData("extension(ref Meter)", "extension(ref Meter)")]
   [InlineData("extension(Tape)", "extension(Tape)", "extension(Tape)")]
   [InlineData("extension<T>(ref T)", "extension<T>(ref T)")]
   public async Task SearchSymbols_ExtensionBlockName_ExactQueryFindsBlock(string query, params string[] expected)
   {
      var tool = new SearchSymbolsTool(_fixture.Workspace);

      var result = await tool.SearchSymbolsAsync(query, maxResults: 500, cancellationToken: CT);

      Assert.Equal(expected.Select(name => (typeof(ExtensionSymbolResponse), name)), result.Data.Items.Select(s => (s.GetType(), s.Name)));
   }

   [Fact]
   public async Task SearchSymbols_ExtensionBlockName_SortByNameUsesBlockName()
   {
      var tool = new SearchSymbolsTool(_fixture.Workspace);

      var result = await tool.SearchSymbolsAsync("e", sortBy: SymbolSortBy.Name, maxResults: 500, cancellationToken: CT);

      var names = result.Data.Items.Select(s => s.Name).ToList();
      Assert.NotEmpty(names);
      Assert.Equal(names.Order(StringComparer.OrdinalIgnoreCase), names);
      Assert.Equal(AllExtensionBlocks.Length, names.Count(n => n.StartsWith("extension", StringComparison.Ordinal)));
   }

   [Fact]
   public async Task SearchSymbols_ExtensionBlockName_SortByKindPlacesBlocksAfterClasses()
   {
      var tool = new SearchSymbolsTool(_fixture.Workspace);

      var result = await tool.SearchSymbolsAsync("extension", sortBy: SymbolSortBy.Kind, maxResults: 500, cancellationToken: CT);

      Assert.Equal(
         [
            (typeof(ClassSymbolResponse), "BoardExtensions"),
            (typeof(ClassSymbolResponse), "CatalogExtensions"),
            (typeof(ClassSymbolResponse), "GridExtensions"),
            (typeof(ClassSymbolResponse), "LedgerExtensions"),
            (typeof(ClassSymbolResponse), "LoopExtensions"),
            (typeof(ClassSymbolResponse), "MeterExtensions"),
            (typeof(ClassSymbolResponse), "SeededExtensions"),
            (typeof(ClassSymbolResponse), "SpoolExtensions"),
            (typeof(ClassSymbolResponse), "StaleExtensions"),
            (typeof(ClassSymbolResponse), "TextExtensions"),
            (typeof(ClassSymbolResponse), "WardExtensions"),
            (typeof(ClassSymbolResponse), "WordExtensions"),
            .. AllExtensionBlocks.Select(b => (typeof(ExtensionSymbolResponse), b.Name)).OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase),
         ],
         result.Data.Items.Select(s => (s.GetType(), s.Name)));
   }

   [Theory]
   [InlineData("extension*", 20)]
   [InlineData("extension(ref Meter)", 1)]
   [InlineData("Extension(Grid)", 1)]
   public async Task FindSymbols_ExtensionBlockName_NameMatchesBlockName(string name, int expectedCount)
   {
      var tool = new FindSymbolsTool(_fixture.Workspace);

      var result = await tool.FindSymbolsAsync(name, maxResults: 500, cancellationToken: CT);

      var expected = AllExtensionBlocks.Where(b => new GlobMatcher(name).IsMatch(b.Name)).ToList();
      Assert.Equal(expectedCount, expected.Count);
      Assert.Equal(expected, result.Data.Items);
   }

   [Fact]
   public async Task SymbolInfo_ExtensionBlockName_SuggestionsNameBlock()
   {
      var tool = new GetSymbolInfoTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.GetSymbolInfoAsync(GridExtensionBlockPosition, cancellationToken: CT);

      Assert.Equal(
         [
            "find_references to see all usages of extension(Grid)",
            "get_type_members for extension(Grid) to see members",
            "get_type_hierarchy to see inheritance chain",
         ],
         result.Metadata?.SuggestedNextTools ?? throw new InvalidOperationException("The result lists suggested tools"));
   }

   [Fact]
   public async Task ComplexityMetrics_ExtensionBlockName_IsContainingTypeOfBlockMembers()
   {
      var tool = new GetComplexityMetricsTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.GetComplexityMetricsAsync(
         new ComplexityTargetFile { FilePath = _fixture.GetPath("ExtensionMembers.cs") }, cancellationToken: CT);

      var file = Assert.IsType<FileComplexityResponse>(result.Data);
      var methods = file.Methods ?? throw new InvalidOperationException("Compact verbosity lists methods");
      Assert.Equal(
         [
            ("Whisper", "extension(string)"),
            ("Blank", "extension(string)"),
            ("Doubled", "extension(int)"),
            ("Quiet", "TextUsage"),
         ],
         methods.Select(m => (m.Name, m.ContainingType)));
   }

   [Fact]
   public async Task ComplexityMetrics_ExtensionBlockName_IsContainingTypeOfMethod()
   {
      var tool = new GetComplexityMetricsTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.GetComplexityMetricsAsync(
         new ComplexityTargetMethod { Method = new LookupByName { Name = "TextExtensions.extension(string).Whisper" } }, cancellationToken: CT);

      var method = Assert.IsType<MethodComplexityResponse>(result.Data);
      Assert.Equal(("Whisper", "extension(string)"), (method.Name, method.ContainingType));
   }

   [Fact]
   public async Task OutgoingCalls_ExtensionBlockName_IsContainingTypeOfMethod()
   {
      var tool = new GetOutgoingCallsTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.GetOutgoingCallsAsync(new LookupByName { Name = "TextExtensions.extension(string).Whisper" }, cancellationToken: CT);

      Assert.Equal(("Whisper", "extension(string)"), (result.Data.MethodName, result.Data.ContainingType));
      Assert.Equal(
         ["find_callers to see who calls Whisper", "get_method_source to see the full implementation", "get_type_members for extension(string)"],
         result.Metadata?.SuggestedNextTools ?? throw new InvalidOperationException("The result lists suggested tools"));
   }

   private static List<(Type Kind, string Name, int Line)> UnusedSymbolsIn(FindUnusedCodeResponse data, string fileName) =>
      data.UnusedSymbols
         .Where(s => s.Location?.FilePath == fileName)
         .Select(s => (s.GetType(), s.Name, s.Location?.Line ?? 0))
         .ToList();

   /// <summary>
   /// Returns the unused symbols that are the fixture's type <paramref name="typeName"/> or that it contains, an extension
   /// block's members included.
   /// </summary>
   private static List<(Type Kind, string Name, int Line)> UnusedSymbolsOf(FindUnusedCodeResponse data, string typeName)
   {
      var qualifiedName = $"CSharp15TestProject.{typeName}";
      return data.UnusedSymbols
         .Where(s => s.FullyQualifiedName == qualifiedName
            || s.ContainingType == qualifiedName
            || s.ContainingType?.StartsWith($"{qualifiedName}.extension", StringComparison.Ordinal) == true)
         .Select(s => (s.GetType(), s.Name, s.Location?.Line ?? 0))
         .ToList();
   }
}
