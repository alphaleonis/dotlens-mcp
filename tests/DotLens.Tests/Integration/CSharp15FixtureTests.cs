using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Analysis;
using DotLens.Tools.Intelligence;
using DotLens.Tools.Navigation;
using DotLens.Tools.Search;
using DotLens.Tools.Shared;
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
      Assert.Equivalent(new[] { "Unions.cs", "LabeledLoops.cs", "ExtensionIndexer.cs", "ExtensionMembers.cs", "RefReceiverExtensions.cs", "PrimaryConstructors.cs" }, sourceFiles, strict: true);

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
      Assert.Empty(result.Data.References);
   }

   private static List<(Type Kind, string Name, int Line)> UnusedSymbolsIn(FindUnusedCodeResponse data, string fileName) =>
      data.UnusedSymbols
         .Where(s => s.Location?.FilePath == fileName)
         .Select(s => (s.GetType(), s.Name, s.Location?.Line ?? 0))
         .ToList();
}
