using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Navigation;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DotLens.Tests.Integration;

/// <summary>
/// Covers the kinds and callers that find_references and find_callers report for calls without an invocation
/// expression, such as Deconstruct calls, operators and constructor initializers, on a project with two targets.
/// </summary>
public class ImplicitCallTests : IClassFixture<ReferenceSearchFixtureWorkspace>
{
   private const string DeconstructionCalls = "DeconstructionCalls.cs";
   private const string ImplicitCalls = "ImplicitCalls.cs";

   private readonly ReferenceSearchFixtureWorkspace _fixture;

   public ImplicitCallTests(ReferenceSearchFixtureWorkspace fixture)
   {
      _fixture = fixture;
   }

   private static CancellationToken CT => TestContext.Current.CancellationToken;

   public static TheoryData<string> DeconstructedTypes => ["CallPair", "CallRecord", "CallExtended", "CallBlock"];

   [Theory]
   [MemberData(nameof(DeconstructedTypes))]
   public async Task FindCallers_Deconstruct_ListsEveryDeconstructionShape(string type)
   {
      await AssertCallersAsync(DeconstructLookup(type), ExpectedDeconstructions(type));
   }

   [Theory]
   [MemberData(nameof(DeconstructedTypes))]
   public async Task FindReferences_Deconstruct_ReportsEveryDeconstructionShapeAsCall(string type)
   {
      await AssertReferencesAsync(
         DeconstructLookup(type),
         [.. ExpectedDeconstructions(type).Select(call => (DeconstructionCalls, call.Line, call.Column, ReferenceKind.Call))]);
   }

   /// <summary>
   /// Find-callers follows the hierarchy from the searched method to the members it overrides or implements and to the
   /// members that override or implement it, as it does for the Deconstruct call of a deconstruction assignment.
   /// </summary>
   [Theory]
   [InlineData("ICallSource", new[] { "ViaInterface", "ViaImplementation" })]
   [InlineData("CallSource", new[] { "ViaInterface", "ViaImplementation" })]
   [InlineData("CallBase", new[] { "ViaBase", "ViaDerived", "ViaSibling" })]
   [InlineData("CallDerived", new[] { "ViaBase", "ViaDerived" })]
   public async Task FindCallers_DeconstructInHierarchy_ListsThePatternsThatCallItOrItsBaseOrOverride(string type, string[] callers)
   {
      await AssertCallersAsync(DeconstructLookup(type), [.. callers.Select(HierarchyUse)]);
   }

   /// <summary>
   /// Find-references cascades to the whole hierarchy, so a sibling override's pattern counts too.
   /// </summary>
   [Theory]
   [InlineData("ICallSource", new[] { "ViaInterface", "ViaImplementation" })]
   [InlineData("CallSource", new[] { "ViaInterface", "ViaImplementation" })]
   [InlineData("CallBase", new[] { "ViaBase", "ViaDerived", "ViaSibling" })]
   [InlineData("CallDerived", new[] { "ViaBase", "ViaDerived", "ViaSibling" })]
   public async Task FindReferences_DeconstructInHierarchy_ReportsThePatternsOfTheHierarchyAsCall(string type, string[] users)
   {
      await AssertReferencesAsync(
         DeconstructLookup(type),
         [.. users.Select(HierarchyUse).Select(use => (DeconstructionCalls, use.Line, use.Column, ReferenceKind.Call))]);
   }

   [Theory]
   [InlineData(10, 30, 77, 7)]
   [InlineData(17, 16, 77, 7)]
   [InlineData(22, 27, 82, 72)]
   [InlineData(62, 32, 95, 43)]
   [InlineData(67, 11, 97, 46)]
   public async Task FindReferences_MethodThatTheCompilerCalls_ReportsTheCallAsCall(
      int declarationLine, int declarationColumn, int referenceLine, int referenceColumn)
   {
      await AssertReferencesAsync(
         new LookupByPosition { FilePath = _fixture.GetPath(ImplicitCalls), Line = declarationLine, Column = declarationColumn },
         (ImplicitCalls, referenceLine, referenceColumn, ReferenceKind.Call));
   }

   [Fact]
   public async Task FindReferences_DisposeOfUsingStatementAndDeclaration_ReportsBothAsCall()
   {
      await AssertReferencesAsync(
         new LookupByPosition { FilePath = _fixture.GetPath(ImplicitCalls), Line = 36, Column = 16 },
         (ImplicitCalls, 86, 7, ReferenceKind.Call),
         (ImplicitCalls, 90, 7, ReferenceKind.Call));
   }

   [Fact]
   public async Task FindReferences_AddOfCollectionInitializer_ReportsEachElementAsCall()
   {
      await AssertReferencesAsync(
         new LookupByPosition { FilePath = _fixture.GetPath(ImplicitCalls), Line = 43, Column = 16 },
         (ImplicitCalls, 93, 58, ReferenceKind.Call),
         (ImplicitCalls, 93, 61, ReferenceKind.Call));
   }

   /// <summary>
   /// The compiler gets Current through its getter, so the use stays a read.
   /// </summary>
   [Fact]
   public async Task FindReferences_CurrentOfForeach_ReportsRead()
   {
      await AssertReferencesAsync(
         new LookupByPosition { FilePath = _fixture.GetPath(ImplicitCalls), Line = 15, Column = 15 },
         (ImplicitCalls, 77, 7, ReferenceKind.Read));
   }

   /// <summary>
   /// An operator, a conversion or a constructor can only be invoked, so each reference to one is a call, except in a
   /// documentation cref. A method keeps its kind by syntax, so a method group and nameof stay reads.
   /// </summary>
   [Theory]
   [InlineData("operator +")]
   [InlineData("operator &")]
   [InlineData("operator ++")]
   [InlineData("explicit operator int")]
   [InlineData("ImplicitOperand(int)")]
   [InlineData("ImplicitBase(int)")]
   [InlineData("ImplicitChained()")]
   [InlineData("ImplicitTagAttribute()")]
   [InlineData("ImplicitTagAttribute(int)")]
   [InlineData("ImplicitTarget.Next()")]
   [InlineData("ImplicitQualified()")]
   [InlineData("ImplicitGeneric()")]
   public async Task FindReferences_InvokedMember_ReportsEachInvocationAsCall(string member)
   {
      (SymbolLookup Lookup, (int Line, int Column, ReferenceKind Kind)[] Expected) invoked = member switch
      {
         "operator +" => (AtImplicitCalls(110, 34), [(101, 35, ReferenceKind.Read), (180, 23, ReferenceKind.Call), (183, 11, ReferenceKind.Call)]),
         "operator &" => (AtImplicitCalls(112, 34), [(181, 24, ReferenceKind.Call)]),
         "operator ++" => (AtImplicitCalls(118, 34), [(182, 10, ReferenceKind.Call)]),
         "explicit operator int" => (AtImplicitCalls(120, 27), [(184, 14, ReferenceKind.Call)]),
         "ImplicitOperand(int)" => (AtImplicitCalls(106, 11), [(101, 86, ReferenceKind.Read), (187, 50, ReferenceKind.Call)]),
         "ImplicitBase(int)" => (AtImplicitCalls(125, 11), [(130, 48, ReferenceKind.Call), (135, 9, ReferenceKind.Call)]),
         "ImplicitChained()" => (AtImplicitCalls(134, 11), [(140, 9, ReferenceKind.Call)]),
         "ImplicitTagAttribute()" => (AtImplicitCalls(148, 11), [(189, 5, ReferenceKind.Call), (194, 5, ReferenceKind.Call)]),
         "ImplicitTagAttribute(int)" => (AtImplicitCalls(152, 11), [(199, 5, ReferenceKind.Call)]),
         "ImplicitTarget.Next()" => (AtImplicitCalls(159, 15), [(102, 31, ReferenceKind.Read), (204, 70, ReferenceKind.Call), (206, 67, ReferenceKind.Read), (208, 57, ReferenceKind.Read)]),
         "ImplicitQualified()" => (AtImplicitCalls(164, 11), [(210, 82, ReferenceKind.Call)]),
         "ImplicitGeneric()" => (AtImplicitCalls(171, 11), [(212, 56, ReferenceKind.Call)]),
         _ => throw new InvalidOperationException($"Unexpected member: {member}"),
      };

      await AssertReferencesAsync(invoked.Lookup, [.. invoked.Expected.Select(e => (ImplicitCalls, e.Line, e.Column, e.Kind))]);
   }

   private LookupByPosition AtImplicitCalls(int line, int column) =>
      new() { FilePath = _fixture.GetPath(ImplicitCalls), Line = line, Column = column };

   /// <summary>
   /// A documentation ID in a SuppressMessage target names the member without a call, so it stays a read. Roslyn
   /// reports the target in the verbatim string one column before the name.
   /// </summary>
   [Theory]
   [InlineData(217, 11, 3, 125, 223, 54)]
   [InlineData(221, 37, 4, 125, 223, 49)]
   public async Task FindReferencesAndCallers_InvokedMemberNamedInSuppressionTarget_ReportsTheTargetAsRead(
      int declarationLine, int declarationColumn, int targetLine, int targetColumn, int callLine, int callColumn)
   {
      var lookup = AtImplicitCalls(declarationLine, declarationColumn);

      await AssertReferencesAsync(
         lookup,
         ("GlobalSuppressions.cs", targetLine, targetColumn, ReferenceKind.Read),
         (ImplicitCalls, callLine, callColumn, ReferenceKind.Call));

      var result = await new FindCallersTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace))
         .FindCallersAsync(lookup, cancellationToken: CT);
      var callers = result.Data.Callers ?? throw new InvalidOperationException("Compact verbosity lists callers");
      Assert.Equal(
         [("Create", ImplicitCalls, callLine, callColumn)],
         callers.Select(c => (c.CallingSymbol?.Name, c.Location.FilePath, c.Location.Line, c.Location.Column)));
      Assert.Equal(1, result.Data.TotalCallers);
   }

   /// <summary>
   /// The static method that the compiler emits for an extension block member has the calls of the member.
   /// </summary>
   [Fact]
   public async Task FindCallersAndReferences_ExtensionBlockImplementationMethod_ListTheCallsOfTheMember()
   {
      var solution = _fixture.Workspace.EnsureSolutionLoaded();
      var compilation = await solution.Projects.First().GetCompilationAsync(CT) ?? throw new InvalidOperationException("The fixture project compiles");
      var extensions = compilation.GetTypeByMetadataName("ReferenceSearchTestProject.CallBlockExtensions")
         ?? throw new InvalidOperationException("The fixture declares CallBlockExtensions");
      var implementation = Assert.Single(extensions.GetMembers("Deconstruct").OfType<IMethodSymbol>());
      Assert.Equal(3, implementation.Parameters.Length);

      var callers = await ReferenceSearch.FindCallersAsync(implementation, solution, CT);
      var references = await ReferenceSearch.FindReferencesAsync(implementation, solution, CT);

      var expected = ExpectedDeconstructions("CallBlock").Select(call => (call.Line, call.Column)).ToList();
      Assert.Equal(expected, callers.SelectMany(caller => caller.Locations).Select(Position).Order());
      Assert.Equal(expected, references.Select(reference => Position(reference.Location)).Order());
   }

   private static (int Line, int Column) Position(Location location)
   {
      var start = location.GetLineSpan().StartLinePosition;
      return (start.Line + 1, start.Character + 1);
   }

   private SymbolLookup DeconstructLookup(string type)
   {
      if (type == "CallRecord")
         return new LookupByName { Name = "CallRecord.Deconstruct" };

      var (line, column) = type switch
      {
         "CallPair" => (20, 16),
         "CallExtended" => (172, 23),
         "CallBlock" => (255, 19),
         "ICallSource" => (330, 9),
         "CallSource" => (335, 16),
         "CallBase" => (344, 24),
         "CallDerived" => (353, 25),
         _ => throw new InvalidOperationException($"Unexpected type: {type}"),
      };
      return new LookupByPosition { FilePath = _fixture.GetPath(DeconstructionCalls), Line = line, Column = column };
   }

   private static (string Caller, int Line, int Column)[] ExpectedDeconstructions(string type) => type switch
   {
      "CallPair" =>
      [
         ("Field FieldPattern", 35, 61),
         ("Property PropertyPattern", 37, 45),
         ("Method Positional", 39, 61),
         ("Method Untyped", 41, 51),
         ("Method VarPattern", 43, 58),
         ("Method NestedPositional", 45, 78),
         ("Method NestedVar", 47, 66),
         ("Method Switch", 49, 63),
         ("Method Lambda", 51, 70),
         ("Method Declaration", 55, 7),
         ("Method Typed", 61, 7),
         ("Method Existing", 69, 7),
         ("Method NestedAssignment", 75, 7),
         ("Method Loop", 82, 16),
         ("Method TypedLoop", 90, 16),
      ],
      "CallRecord" =>
      [
         ("Field FieldPattern", 102, 61),
         ("Property PropertyPattern", 104, 45),
         ("Method Positional", 106, 63),
         ("Method Untyped", 108, 53),
         ("Method VarPattern", 110, 60),
         ("Method NestedPositional", 112, 82),
         ("Method NestedVar", 114, 68),
         ("Method Switch", 116, 65),
         ("Method Lambda", 118, 72),
         ("Method Declaration", 122, 7),
         ("Method Typed", 128, 7),
         ("Method Existing", 136, 7),
         ("Method NestedAssignment", 142, 7),
         ("Method Loop", 149, 16),
         ("Method TypedLoop", 157, 16),
      ],
      "CallExtended" =>
      [
         ("Field FieldPattern", 183, 61),
         ("Property PropertyPattern", 185, 45),
         ("Method Positional", 187, 65),
         ("Method Untyped", 189, 55),
         ("Method VarPattern", 191, 62),
         ("Method NestedPositional", 193, 86),
         ("Method NestedVar", 195, 70),
         ("Method Switch", 197, 67),
         ("Method Lambda", 199, 74),
         ("Method Declaration", 203, 7),
         ("Method Typed", 209, 7),
         ("Method Existing", 217, 7),
         ("Method NestedAssignment", 223, 7),
         ("Method Loop", 230, 16),
         ("Method TypedLoop", 238, 16),
      ],
      "CallBlock" =>
      [
         ("Field FieldPattern", 267, 61),
         ("Property PropertyPattern", 269, 45),
         ("Method Positional", 271, 62),
         ("Method Untyped", 273, 52),
         ("Method VarPattern", 275, 59),
         ("Method NestedPositional", 277, 80),
         ("Method NestedVar", 279, 67),
         ("Method Switch", 281, 64),
         ("Method Lambda", 283, 71),
         ("Method Declaration", 287, 7),
         ("Method Typed", 293, 7),
         ("Method Existing", 301, 7),
         ("Method NestedAssignment", 307, 7),
         ("Method Loop", 314, 16),
         ("Method TypedLoop", 322, 16),
      ],
      _ => throw new InvalidOperationException($"Unexpected type: {type}"),
   };

   private static (string Caller, int Line, int Column) HierarchyUse(string member) => member switch
   {
      "ViaInterface" => ("Method ViaInterface", 371, 69),
      "ViaImplementation" => ("Method ViaImplementation", 373, 73),
      "ViaBase" => ("Method ViaBase", 375, 59),
      "ViaDerived" => ("Method ViaDerived", 377, 65),
      "ViaSibling" => ("Method ViaSibling", 379, 65),
      _ => throw new InvalidOperationException($"Unexpected member: {member}"),
   };

   /// <summary>
   /// Asserts each caller as its kind and name, then the use, all in DeconstructionCalls.cs.
   /// </summary>
   private async Task AssertCallersAsync(SymbolLookup lookup, (string Caller, int Line, int Column)[] expected)
   {
      var tool = new FindCallersTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.FindCallersAsync(lookup, maxResults: 100, cancellationToken: CT);

      var callers = (result.Data.Callers ?? throw new InvalidOperationException("Compact verbosity lists callers"))
         .OrderBy(c => c.Location.Line).ThenBy(c => c.Location.Column)
         .ToList();
      Assert.All(callers, c => Assert.Equal(DeconstructionCalls, c.Location.FilePath));
      Assert.Equal(expected, callers.Select(c => (Describe(c.CallingSymbol), c.Location.Line, c.Location.Column)));
      Assert.Equal(expected.Length, result.Data.TotalCallers);
   }

   private static string Describe(SymbolResponse? symbol) =>
      symbol is null ? "(top-level statements)" : $"{symbol.GetType().Name.Replace("SymbolResponse", "")} {symbol.Name}";

   private async Task AssertReferencesAsync(SymbolLookup lookup, params (string File, int Line, int Column, ReferenceKind Kind)[] expected)
   {
      var tool = new FindReferencesTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.FindReferencesAsync(lookup, maxResults: 100, cancellationToken: CT);

      Assert.Equal(
         expected.Select(e => new ReferenceInfo { FilePath = e.File, Line = e.Line, Column = e.Column, Kind = e.Kind })
            .OrderBy(r => r.Line).ThenBy(r => r.Column),
         (result.Data.References ?? throw new InvalidOperationException("Compact verbosity lists references"))
            .OrderBy(r => r.FilePath, StringComparer.Ordinal).ThenBy(r => r.Line).ThenBy(r => r.Column));
      Assert.Equal(expected.Length, result.Data.TotalReferences);
   }
}
