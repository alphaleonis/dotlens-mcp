using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Intelligence;
using DotLens.Tools.Navigation;
using DotLens.Tools.Shared;
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
