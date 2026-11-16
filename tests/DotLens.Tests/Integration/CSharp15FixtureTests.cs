using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Analysis;
using DotLens.Tools.Intelligence;
using DotLens.Tools.Navigation;
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
      Assert.Equivalent(new[] { "Unions.cs", "LabeledLoops.cs", "ExtensionIndexer.cs", "ExtensionMembers.cs", "RefReceiverExtensions.cs", "PrimaryConstructors.cs", "IndexerUses.cs", "IndexerWrites.cs", "AmbiguousMembers.cs", "EnumsAndDelegates.cs", "DelegateSignatures.cs" }, sourceFiles, strict: true);

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
      AssertIndexerWritesReferencesAsync(new LookupByName { Name = name }, expected);

   private async Task AssertIndexerWritesReferencesAsync(SymbolLookup lookup, params (int Line, int Column, ReferenceKind Kind)[] expected)
   {
      var tool = new FindReferencesTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.FindReferencesAsync(lookup, cancellationToken: CT);

      var references = result.Data.References ?? throw new InvalidOperationException("Compact verbosity lists references");
      Assert.Equal(
         expected.Select(e => new ReferenceInfo { FilePath = "IndexerWrites.cs", Line = e.Line, Column = e.Column, Kind = e.Kind }),
         references.OrderBy(r => r.Line).ThenBy(r => r.Column));
      Assert.Equal(expected.Length, result.Data.TotalReferences);
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
   private const string TapePeekImplementation = "CSharp15TestProject.SpoolExtensions.Peek(CSharp15TestProject.Tape)";
   private const string ReelPeekImplementation = "CSharp15TestProject.SpoolExtensions.Peek(CSharp15TestProject.Reel)";
   private const string TapeLength = "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Tape).Length";
   private const string ReelLength = "CSharp15TestProject.SpoolExtensions.extension(CSharp15TestProject.Reel).Length";

   /// <summary>
   /// The compiler declares a static implementation method in the holder class for each extension method, and a name
   /// lookup finds those too.
   /// </summary>
   [Theory]
   [InlineData("SpoolExtensions.Peek", new[] { TapePeekImplementation, ReelPeekImplementation, TapePeek, ReelPeek })]
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
   [InlineData("CSharp15TestProject.SpoolExtensions.Peek", new[] { "CSharp15TestProject.Tape" }, null, TapePeekImplementation)]
   [InlineData("SpoolExtensions.extension(Tape).Length", null, null, TapeLength)]
   [InlineData("SpoolExtensions.extension(Reel).Length", null, null, ReelLength)]
   public async Task LookupByName_AmbiguousExtensionMember_NameSelectsOne(string name, string[]? parameterTypes, string? returnType, string expected)
   {
      await AssertSelectsAsync(new LookupByName { Name = name, ParameterTypes = parameterTypes, ReturnType = returnType }, expected);
   }

   [Theory]
   [InlineData("Box.Get", new[] { "CSharp15TestProject.Box<T>.Get()", "CSharp15TestProject.Box<T>.Get(int)" })]
   [InlineData("Mold.Make", new[] { "CSharp15TestProject.Mold.Make(int) at AmbiguousMembers.cs:55:22", "CSharp15TestProject.Mold.Make<T>(int) at AmbiguousMembers.cs:57:21" })]
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

   [Theory]
   [InlineData("Box<T, U>.Get")]
   [InlineData("CSharp15TestProject.Box<T, U>.Get")]
   [InlineData("Mold.Make<T, U>")]
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
   /// moved to parameterTypes, its return type to returnType, or its location to position mode, as
   /// <see cref="SymbolLookupService.AmbiguityHint"/> says, must find that candidate. A listed name without a location
   /// also finds the candidates that differ from it only in type parameters, as the hint warns.
   /// </summary>
   [Theory]
   [InlineData("SpoolExtensions.this[]")]
   [InlineData("SpoolExtensions.extension(Tape).this[]")]
   [InlineData("Catalog.this[]")]
   [InlineData("CSharp15TestProject.Catalog.get_Item")]
   [InlineData("SpoolExtensions.Peek")]
   [InlineData("SpoolExtensions.Length")]
   [InlineData("Box.Get")]
   [InlineData("Mold.Make")]
   [InlineData("CSharp15TestProject.Money.op_Implicit")]
   [InlineData("CSharp15TestProject.Money.op_Explicit")]
   [InlineData("CSharp15TestProject.Money.op_LessThan")]
   [InlineData("System.Decimal.op_Implicit")]
   [InlineData("System.Decimal.op_Explicit")]
   [InlineData("System.String.Join")]
   [InlineData("Stamp.Value")]
   [InlineData("Stamp.Deconstruct")]
   public async Task LookupByName_AmbiguousCandidates_EachListedCandidateFindsItself(string name)
   {
      var service = new SymbolLookupService(_fixture.Workspace);
      var lookup = new LookupByName { Name = name };
      var matches = await service.FindAsync<ISymbol>(lookup, cancellationToken: CT);

      var ex = await Assert.ThrowsAsync<ToolException>(() => service.FindSingleAsync<ISymbol>(lookup, cancellationToken: CT));

      var listed = ex.Message[(ex.Message.IndexOf(": ", StringComparison.Ordinal) + 2)..]
         .Split("; ")
         .Where(candidate => !candidate.StartsWith("and ", StringComparison.Ordinal))
         .ToList();
      Assert.Equal(Math.Min(matches.Count, 5), listed.Count);
      var lookups = listed.Select(ParseListedCandidate).ToList();
      for (var i = 0; i < listed.Count; i++)
      {
         var expected = lookups[i] is LookupByName byName
            ? Enumerable.Range(0, listed.Count)
               .Where(j => lookups[j] is LookupByName other && NameAlsoFinds(byName, other))
               .Select(j => matches[j].Symbol)
               .ToList()
            : [matches[i].Symbol];

         var found = await service.FindAsync<ISymbol>(lookups[i], cancellationToken: CT);

         Assert.Equal(expected, found.Select(r => r.Symbol), SymbolEqualityComparer.Default);
      }
   }

   /// <summary>
   /// Returns whether a listed name also finds another candidate: one with the same name, or, when the listed name has
   /// no type-parameter lists, one whose name only adds them.
   /// </summary>
   private static bool NameAlsoFinds(LookupByName listed, LookupByName other) =>
      (listed.Name == other.Name || (listed.Name == RemoveTypeParameters(listed.Name) && listed.Name == RemoveTypeParameters(other.Name)))
      && (listed.ParameterTypes ?? []).SequenceEqual(other.ParameterTypes ?? [])
      && listed.ParameterTypes is null == other.ParameterTypes is null
      && listed.ReturnType == other.ReturnType;

   private static string RemoveTypeParameters(string name) =>
      System.Text.RegularExpressions.Regex.Replace(name, "<[^<>()]*>(?=\\.|$)", "");

   private static SymbolLookup ParseListedCandidate(string candidate)
   {
      var at = candidate.IndexOf(" at ", StringComparison.Ordinal);
      if (at >= 0)
      {
         var position = candidate[(at + 4)..].Split(':');
         return new LookupByPosition { FilePath = position[0], Line = int.Parse(position[1]), Column = int.Parse(position[2]) };
      }

      string? returnType = null;
      var returns = candidate.IndexOf(" returns ", StringComparison.Ordinal);
      if (returns >= 0)
      {
         returnType = candidate[(returns + 9)..];
         candidate = candidate[..returns];
      }

      var (open, close) = candidate.EndsWith(')') ? ('(', ')') : candidate.EndsWith(']') ? ('[', ']') : ('\0', '\0');
      if (open == '\0')
         return new LookupByName { Name = candidate };

      var start = FindOpeningBracket(candidate, open, close);
      var name = open == '[' ? candidate[..(start + 1)] + "]" : candidate[..start];
      return new LookupByName { Name = name, ParameterTypes = SplitTopLevel(candidate[(start + 1)..^1]), ReturnType = returnType };
   }

   private static int FindOpeningBracket(string text, char open, char close)
   {
      var depth = 0;
      for (var i = text.Length - 1; i >= 0; i--)
      {
         if (text[i] == close)
            depth++;
         else if (text[i] == open && --depth == 0)
            return i;
      }

      throw new InvalidOperationException($"No opening '{open}' in {text}");
   }

   private static string[] SplitTopLevel(string parameters)
   {
      var parts = new List<string>();
      var depth = 0;
      var start = 0;
      for (var i = 0; i < parameters.Length; i++)
      {
         switch (parameters[i])
         {
            case '<' or '(' or '[':
               depth++;
               break;
            case '>' or ')' or ']':
               depth--;
               break;
            case ',' when depth == 0:
               parts.Add(parameters[start..i].Trim());
               start = i + 1;
               break;
         }
      }

      if (parameters.Trim().Length > 0)
         parts.Add(parameters[start..].Trim());
      return parts.ToArray();
   }

   /// <summary>
   /// A record's positional property is declared by its primary-constructor parameter, where a position lookup finds
   /// the parameter instead.
   /// </summary>
   [Fact]
   public async Task LookupByName_AmbiguousRecordPositionalProperty_ListsNoLocation()
   {
      await AssertAmbiguousAsync("Stamp.Value", ["CSharp15TestProject.Stamp.Value", "CSharp15TestProject.Stamp<T>.Value"]);
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
            (typeof(ClassSymbolResponse), "GridExtensions", 8),
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
            (typeof(ClassSymbolResponse), "TextExtensions", 3),
            (typeof(ClassSymbolResponse), "TextUsage", 27),
            (typeof(PropertySymbolResponse), "IsEven", 16),
            (typeof(MethodSymbolResponse), "Doubled", 18),
            (typeof(MethodSymbolResponse), "Quiet", 29),
         ],
         UnusedSymbolsIn(result.Data, "ExtensionMembers.cs"));
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
            Declaration = "public GridExtensions.extension(Grid)",
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
            (typeof(DelegateSymbolResponse), "internal delegate string Formatter<T>(T? value, string format = \"G\", int width = 0)"),
         ],
         types.Select(t => (t.GetType(), t.Declaration)));
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

   private static List<(Type Kind, string Name, int Line)> UnusedSymbolsIn(FindUnusedCodeResponse data, string fileName) =>
      data.UnusedSymbols
         .Where(s => s.Location?.FilePath == fileName)
         .Select(s => (s.GetType(), s.Name, s.Location?.Line ?? 0))
         .ToList();
}
