using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Intelligence;
using DotLens.Tools.Navigation;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DotLens.Tests.Integration;

/// <summary>
/// Covers the reference and caller tools on a project that targets two frameworks, so each source file has one linked
/// copy per target.
/// </summary>
public class ReferenceSearchFixtureTests : IClassFixture<ReferenceSearchFixtureWorkspace>
{
   private const string LinkedUses = "LinkedUses.cs";
   private const string GeneratedUses = "GeneratedUses.cs";
   private const string RefAndAttributeWrites = "RefAndAttributeWrites.cs";
   private const string InitializerUses = "InitializerUses.cs";

   private readonly ReferenceSearchFixtureWorkspace _fixture;

   public ReferenceSearchFixtureTests(ReferenceSearchFixtureWorkspace fixture)
   {
      _fixture = fixture;
   }

   private static CancellationToken CT => TestContext.Current.CancellationToken;

   [Fact]
   public void Load_HasOneProjectPerTargetFramework()
   {
      var projects = _fixture.Workspace.EnsureSolutionLoaded().Projects;

      Assert.Equivalent(
         new[] { "ReferenceSearchTestProject(net10.0)", "ReferenceSearchTestProject(net11.0)" },
         projects.Select(p => p.Name),
         strict: true);
   }

   [Fact]
   public async Task FindReferences_LinkedSymbol_NoDuplicateReferences_Method()
   {
      await AssertReferencesAsync(new LookupByName { Name = "Counter.Add" }, (LinkedUses, 16, 15, ReferenceKind.Call));
   }

   [Fact]
   public async Task FindReferences_LinkedSymbol_NoDuplicateReferences_IndexerWithNullConditionalUse()
   {
      await AssertReferencesAsync(
         new LookupByName { Name = "Counter.this[]" },
         (LinkedUses, 17, 14, ReferenceKind.Write),
         (LinkedUses, 18, 13, ReferenceKind.Write),
         (LinkedUses, 19, 21, ReferenceKind.Read));
   }

   /// <summary>
   /// A parameter has no documentation ID, so its linked copies match by location alone.
   /// </summary>
   [Fact]
   public async Task FindReferences_LinkedSymbol_NoDuplicateReferences_Parameter()
   {
      await AssertReferencesAsync(
         new LookupByPosition { FilePath = _fixture.GetPath(LinkedUses), Line = 14, Column = 34 },
         (LinkedUses, 16, 7, ReferenceKind.Read),
         (LinkedUses, 17, 7, ReferenceKind.Read),
         (LinkedUses, 19, 14, ReferenceKind.Read),
         (LinkedUses, 19, 27, ReferenceKind.Read));
   }

   [Fact]
   public async Task FindCallers_LinkedSymbol_NoDuplicateReferences_Method()
   {
      await AssertCallersAsync(new LookupByName { Name = "Counter.Add" }, ("Use", LinkedUses, 16, 15, false));
   }

   [Fact]
   public async Task FindCallers_LinkedSymbol_NoDuplicateReferences_IndexerWithNullConditionalUse()
   {
      await AssertCallersAsync(
         new LookupByName { Name = "Counter.this[]" },
         ("Use", LinkedUses, 17, 14, false),
         ("Use", LinkedUses, 18, 13, false),
         ("Use", LinkedUses, 19, 21, false));
   }

   [Fact]
   public async Task AnalyzeMethod_LinkedSymbol_NoDuplicateReferences()
   {
      var tool = new AnalyzeMethodTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.AnalyzeMethodAsync(new LookupByName { Name = "Counter.Add" }, cancellationToken: CT);

      Assert.Equal(1, result.Data.CallerCount);
      Assert.Equal(
         [(LinkedUses, 16, 15, false)],
         (result.Data.Callers ?? throw new InvalidOperationException("Compact verbosity lists callers"))
            .Select(c => (c.FilePath, c.Line, c.Column, c.IsGenerated)));
   }

   [Fact]
   public async Task AnalyzeChangeImpact_LinkedSymbol_NoDuplicateReferences()
   {
      var tool = new AnalyzeChangeImpactTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.AnalyzeChangeImpactAsync(new LookupByName { Name = "Counter.Add" }, ChangeType.Rename, "Append", cancellationToken: CT);

      Assert.Equal(1, result.Data.ReferenceCount);
      Assert.Equal(
         [(LinkedUses, 16, 15, false)],
         (result.Data.ImpactedLocations ?? throw new InvalidOperationException("Compact verbosity lists locations"))
            .Select(l => (l.FilePath, l.Line, l.Column, l.IsDefinition)));
   }

   [Fact]
   public async Task FindReferences_SourceGenerated_ListsGeneratedReference()
   {
      var tool = new FindReferencesTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.FindReferencesAsync(
         new LookupByName { Name = "HandleMarshaller.ConvertToUnmanaged" }, verbosity: ToolResultVerbosity.Full, cancellationToken: CT);

      Assert.Equal(
         [
            new ReferenceInfo
            {
               FilePath = GeneratedUses, Line = 21, Column = 62, Kind = ReferenceKind.Call,
               LineText = "public static nint Raw(Handle handle) => HandleMarshaller.ConvertToUnmanaged(handle);",
            },
            new ReferenceInfo
            {
               FilePath = GeneratedPath, Line = 12, Column = 83, Kind = ReferenceKind.Call, IsGenerated = true,
               LineText = "__handle_native = global::ReferenceSearchTestProject.HandleMarshaller.ConvertToUnmanaged(handle);",
            },
         ],
         (result.Data.References ?? throw new InvalidOperationException("Full verbosity lists references")).OrderBy(r => r.FilePath, StringComparer.Ordinal));
      Assert.Equal(2, result.Data.TotalReferences);
   }

   [Fact]
   public async Task AnalyzeChangeImpact_SourceGenerated_MarksGeneratedLocation()
   {
      var tool = new AnalyzeChangeImpactTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.AnalyzeChangeImpactAsync(
         new LookupByName { Name = "HandleMarshaller.ConvertToUnmanaged" }, ChangeType.Rename, "ToNative", cancellationToken: CT);

      Assert.Equal(2, result.Data.ReferenceCount);
      Assert.Equal(
         [(GeneratedUses, 21, 62, false), (GeneratedPath, 12, 83, true)],
         (result.Data.ImpactedLocations ?? throw new InvalidOperationException("Compact verbosity lists locations"))
            .Select(l => (l.FilePath, l.Line, l.Column, l.IsGenerated))
            .OrderBy(l => l.FilePath, StringComparer.Ordinal));
   }

   [Fact]
   public async Task FindCallers_SourceGenerated_ListsGeneratedCaller()
   {
      await AssertCallersAsync(
         new LookupByName { Name = "HandleMarshaller.ConvertToUnmanaged" },
         ("Raw", GeneratedUses, 21, 62, false),
         ("Close", GeneratedPath, 12, 83, true));
   }

   [Fact]
   public async Task FindReferences_RefAndAttributeWrites_RefToIndexer()
   {
      await AssertReferencesAsync(
         new LookupByName { Name = "Cells.this[]" },
         (RefAndAttributeWrites, 37, 35, ReferenceKind.Write),
         (RefAndAttributeWrites, 38, 44, ReferenceKind.Read),
         (RefAndAttributeWrites, 39, 27, ReferenceKind.Write),
         (RefAndAttributeWrites, 40, 27, ReferenceKind.Read),
         (RefAndAttributeWrites, 41, 44, ReferenceKind.Write),
         (RefAndAttributeWrites, 41, 59, ReferenceKind.Write),
         (RefAndAttributeWrites, 43, 23, ReferenceKind.Write),
         (RefAndAttributeWrites, 46, 65, ReferenceKind.Read),
         (RefAndAttributeWrites, 48, 66, ReferenceKind.Write),
         (RefAndAttributeWrites, 53, 85, ReferenceKind.Write),
         (RefAndAttributeWrites, 53, 100, ReferenceKind.Write));
   }

   [Fact]
   public async Task FindReferences_RefAndAttributeWrites_RefToField()
   {
      await AssertReferencesAsync(
         new LookupByName { Name = "Cells._first" },
         (RefAndAttributeWrites, 20, 32, ReferenceKind.Write),
         (RefAndAttributeWrites, 22, 49, ReferenceKind.Read),
         (RefAndAttributeWrites, 26, 27, ReferenceKind.Write),
         (RefAndAttributeWrites, 27, 35, ReferenceKind.Read));
   }

   /// <summary>
   /// The argument's keyword can differ from the parameter's: ref is allowed for an in or ref readonly parameter.
   /// </summary>
   [Fact]
   public async Task FindReferences_RefAndAttributeWrites_ArgumentFollowsParameterRefKind()
   {
      await AssertReferencesAsync(
         new LookupByName { Name = "Arguments._slot" },
         (RefAndAttributeWrites, 71, 18, ReferenceKind.Write),
         (RefAndAttributeWrites, 72, 16, ReferenceKind.Read),
         (RefAndAttributeWrites, 73, 15, ReferenceKind.Read),
         (RefAndAttributeWrites, 74, 27, ReferenceKind.Read),
         (RefAndAttributeWrites, 75, 29, ReferenceKind.Write),
         (RefAndAttributeWrites, 76, 16, ReferenceKind.Read),
         (RefAndAttributeWrites, 77, 18, ReferenceKind.Write),
         (RefAndAttributeWrites, 78, 18, ReferenceKind.Read),
         (RefAndAttributeWrites, 79, 24, ReferenceKind.Write),
         (RefAndAttributeWrites, 80, 23, ReferenceKind.Read),
         (RefAndAttributeWrites, 81, 20, ReferenceKind.Read),
         (RefAndAttributeWrites, 82, 16, ReferenceKind.Write));
   }

   [Fact]
   public async Task FindReferences_RefAndAttributeWrites_AttributeNamedArgumentOfProperty()
   {
      await AssertReferencesAsync(
         new LookupByName { Name = "TagAttribute.Level" },
         (RefAndAttributeWrites, 32, 6, ReferenceKind.Write),
         (RefAndAttributeWrites, 50, 9, ReferenceKind.Write),
         (RefAndAttributeWrites, 51, 53, ReferenceKind.Read));
   }

   [Fact]
   public async Task FindReferences_RefAndAttributeWrites_AttributeNamedArgumentOfField()
   {
      await AssertReferencesAsync(new LookupByName { Name = "TagAttribute.Label" }, (RefAndAttributeWrites, 32, 17, ReferenceKind.Write));
   }

   [Fact]
   public async Task FindCallers_UsesOutsideMemberBodies_NameTheDeclaredMember_Property()
   {
      await AssertCallingMembersAsync(
         new LookupByName { Name = "InitSource.Value" },
         "Method Next InitializerUses.cs:14:22 <- 14:32",
         "Property StaticArea InitializerUses.cs:19:22 <- 19:55",
         "Property Area InitializerUses.cs:21:15 <- 21:50",
         "Property Deferred InitializerUses.cs:25:21 <- 25:58",
         "Field Count InitializerUses.cs:29:24 <- 29:43",
         "Constructor .ctor InitializerUses.cs:42:14 <- 42:52",
         "Property Scaled InitializerUses.cs:46:15 <- 46:52");
   }

   [Fact]
   public async Task FindCallers_UsesOutsideMemberBodies_NameTheDeclaredMember_Method()
   {
      await AssertCallingMembersAsync(
         new LookupByName { Name = "InitSource.Next" },
         "Property Tracked InitializerUses.cs:23:15 <- 23:76",
         "Field StaticCount InitializerUses.cs:27:31 <- 27:56",
         "Event Changed InitializerUses.cs:31:28 <- 31:49",
         "Property Both InitializerUses.cs:35:15 <- 35:40",
         "Property Both InitializerUses.cs:35:15 <- 35:63");
   }

   [Fact]
   public async Task FindCallers_UsesOutsideMemberBodies_NameTheDeclaredMember_IndexerWithNullConditionalUse()
   {
      await AssertCallingMembersAsync(new LookupByName { Name = "InitTable.this[]" }, "Property Head InitializerUses.cs:33:23 <- 33:45");
   }

   [Fact]
   public async Task FindCallers_GetterAndInitializerOfOneProperty_GroupUnderOneCaller()
   {
      var solution = _fixture.Workspace.EnsureSolutionLoaded();
      var lookup = await new SymbolLookupService(_fixture.Workspace).FindSingleAsync<ISymbol>(new LookupByName { Name = "InitSource.Next" }, cancellationToken: CT);

      var callers = await ReferenceSearch.FindCallersAsync(lookup.Symbol, solution, CT);

      Assert.Equivalent(
         new[] { ("Tracked", 1), ("StaticCount", 1), ("Changed", 1), ("Both", 2) },
         callers.Select(c => (c.CallingSymbol.Name, c.Locations.Count)),
         strict: true);
   }

   /// <summary>
   /// Find-callers reports the call through the interface after the direct call, though it comes first in source.
   /// </summary>
   [Fact]
   public async Task FindCallers_DirectAndInterfaceCallsInOneMember_ListsTheCallsInSourceOrder()
   {
      var solution = _fixture.Workspace.EnsureSolutionLoaded();
      var lookup = await new SymbolLookupService(_fixture.Workspace).FindSingleAsync<ISymbol>(new LookupByName { Name = "OrderSource.Read" }, cancellationToken: CT);

      var callers = await ReferenceSearch.FindCallersAsync(lookup.Symbol, solution, CT);

      var caller = Assert.Single(callers);
      Assert.Equal("ReadBoth", caller.CallingSymbol?.Name);
      Assert.Equal(
         [(15, 77), (15, 93)],
         caller.Locations.Select(l => (l.GetLineSpan().StartLinePosition.Line + 1, l.GetLineSpan().StartLinePosition.Character + 1)));
   }

   // The workspace gives a generated document a path under the target's intermediate directory.
   private static string GeneratedPath => Path.Combine(
      "obj", "Debug", "net10.0", "Microsoft.Interop.LibraryImportGenerator", "Microsoft.Interop.LibraryImportGenerator", "LibraryImports.g.cs");

   private async Task AssertReferencesAsync(SymbolLookup lookup, params (string File, int Line, int Column, ReferenceKind Kind)[] expected)
   {
      var tool = new FindReferencesTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.FindReferencesAsync(lookup, cancellationToken: CT);

      Assert.Equal(
         expected.Select(e => new ReferenceInfo { FilePath = e.File, Line = e.Line, Column = e.Column, Kind = e.Kind }),
         (result.Data.References ?? throw new InvalidOperationException("Compact verbosity lists references"))
            .OrderBy(r => r.FilePath, StringComparer.Ordinal).ThenBy(r => r.Line).ThenBy(r => r.Column));
      Assert.Equal(expected.Length, result.Data.TotalReferences);
   }

   /// <summary>
   /// Asserts each caller as its kind, name and declaration, then the use, all in InitializerUses.cs.
   /// </summary>
   private async Task AssertCallingMembersAsync(SymbolLookup lookup, params string[] expected)
   {
      var tool = new FindCallersTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.FindCallersAsync(lookup, cancellationToken: CT);

      var callers = (result.Data.Callers ?? throw new InvalidOperationException("Compact verbosity lists callers"))
         .OrderBy(c => c.Location.Line).ThenBy(c => c.Location.Column)
         .ToList();
      Assert.All(callers, c => Assert.Equal(InitializerUses, c.Location.FilePath));
      Assert.Equal(
         expected,
         callers.Select(c => $"{Describe(c.CallingSymbol ?? throw new InvalidOperationException("InitializerUses.cs has no top-level statements"))} <- {c.Location.Line}:{c.Location.Column}"));
      Assert.Equal(expected.Length, result.Data.TotalCallers);
   }

   private static string Describe(SymbolResponse symbol) =>
      symbol.Location is { } location
         ? $"{symbol.GetType().Name.Replace("SymbolResponse", "")} {symbol.Name} {location.FilePath}:{location.Line}:{location.Column}"
         : $"{symbol.GetType().Name.Replace("SymbolResponse", "")} {symbol.Name} (no location)";

   private async Task AssertCallersAsync(SymbolLookup lookup, params (string Caller, string File, int Line, int Column, bool IsGenerated)[] expected)
   {
      var tool = new FindCallersTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));

      var result = await tool.FindCallersAsync(lookup, cancellationToken: CT);

      Assert.Equal(
         expected,
         (result.Data.Callers ?? throw new InvalidOperationException("Compact verbosity lists callers"))
            .Select(c => (c.CallingSymbol.Name, c.Location.FilePath, c.Location.Line, c.Location.Column, c.Location.IsGenerated))
            .OrderBy(c => c.FilePath, StringComparer.Ordinal).ThenBy(c => c.Line).ThenBy(c => c.Column));
      Assert.Equal(expected.Length, result.Data.TotalCallers);
   }
}
