using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Navigation;
using DotLens.Tools.Search;
using DotLens.Tools.Shared;
using Xunit;

namespace DotLens.Tests.Navigation;

[Collection("TestFixtures")]
public class FindReferencesToolTests : TestFixtureTestBase
{
   public FindReferencesToolTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private FindReferencesTool _tool = null!;
   private FindSymbolsTool _findSymbols = null!;
   private SymbolLookupService _symbolLookup = null!;

   public override async ValueTask InitializeAsync()
   {
      _symbolLookup = new SymbolLookupService(Workspace);
      _tool = new FindReferencesTool(Workspace, _symbolLookup);
      _findSymbols = new FindSymbolsTool(Workspace);
   }

   [Fact]
   public async Task FindReferences_OnPublicMethod_FindsUsages()
   {
      // Use name-based lookup for VirtualMethod in AbstractBase class
      var lookup = new LookupByName { Name = "AbstractBase.VirtualMethod" };

      var result = await _tool.FindReferencesAsync(lookup, cancellationToken: CT);
      AssertSuccess(result);

      var data = GetData(result);
      var symbol = GetNode(data, "Symbol");
      Assert.NotNull(symbol);
      Assert.Equal("VirtualMethod", GetStringProperty(symbol, "Name"));

      var references = GetNode(data, "References")?.AsArray();
      Assert.NotNull(references);
      // VirtualMethod has references: override in ConcreteClass, override in DerivedClass, base.VirtualMethod() call
      Assert.True(references!.Count > 0, "VirtualMethod should have references");
   }

   [Fact]
   public async Task FindReferences_OnClass_FindsUsages()
   {
      // Use name-based lookup for SampleClass
      var lookup = new LookupByName { Name = "SampleClass" };

      var result = await _tool.FindReferencesAsync(lookup, cancellationToken: CT);
      AssertSuccess(result);

      var data = GetData(result);
      var symbol = GetNode(data, "Symbol");
      Assert.NotNull(symbol);
      Assert.Equal("SampleClass", GetStringProperty(symbol, "Name"));

      var references = GetNode(data, "References")?.AsArray();
      Assert.NotNull(references);
      // SampleClass is referenced in constructors, Create method, and UnusedUsingsTests
      Assert.True(references!.Count > 0, "SampleClass should have references");
   }

   [Fact]
   public async Task FindReferences_ByPosition_FindsUsages()
   {
      // First find a symbol to get its position
      var searchResult = await _findSymbols.FindSymbolsAsync(name: "VirtualMethod", kinds: [SymbolKindFilter.Method], maxResults: 10, cancellationToken: CT);
      var results = GetNode(GetData(searchResult), "Items")?.AsArray();
      Assert.NotNull(results);
      Assert.True(results!.Count > 0, "Expected to find VirtualMethod in test fixtures");

      var symbolResult = results[0];
      var location = GetNode(symbolResult, "Location");
      Assert.NotNull(location);

      var relativePath = GetStringProperty(location, "FilePath");
      var file = GetAbsolutePath(relativePath!);
      var line = GetProperty<int>(location, "Line") ?? 0;
      var col = GetProperty<int>(location, "Column") ?? 0;

      // Use position-based lookup
      var lookup = new LookupByPosition { FilePath = file, Line = line, Column = col };

      var result = await _tool.FindReferencesAsync(lookup, cancellationToken: CT);
      AssertSuccess(result);

      var data = GetData(result);
      var references = GetNode(data, "References")?.AsArray();
      // Position-based lookup returns references (may be empty for unused symbols)
      Assert.NotNull(references);
   }

   [Fact]
   public async Task FindReferences_SummaryVerbosity_ReturnsOnlyCount()
   {
      var lookup = new LookupByName { Name = "SampleClass" };

      var result = await _tool.FindReferencesAsync(lookup, verbosity: ToolResultVerbosity.Summary, cancellationToken: CT);
      AssertSuccess(result);

      var data = GetData(result);
      var totalReferences = GetProperty<int>(data, "TotalReferences");
      Assert.NotNull(totalReferences);
      Assert.True(totalReferences > 0, "SampleClass should have references in test fixtures");

      // At summary verbosity, References should be null
      var references = GetNode(data, "References");
      Assert.Null(references);
   }

   [Fact]
   public async Task FindReferences_FullVerbosity_IncludesLineText()
   {
      var lookup = new LookupByName { Name = "AbstractBase.VirtualMethod" };

      var result = await _tool.FindReferencesAsync(lookup, verbosity: ToolResultVerbosity.Full, cancellationToken: CT);
      AssertSuccess(result);

      var data = GetData(result);
      var references = GetNode(data, "References")?.AsArray();
      Assert.NotNull(references);
      Assert.True(references!.Count > 0, "VirtualMethod should have references");

      var firstRef = references[0];
      var lineText = GetStringProperty(firstRef, "LineText");
      Assert.NotNull(lineText);
      // Line text should contain VirtualMethod (either the override or a call)
      Assert.False(string.IsNullOrWhiteSpace(lineText), "LineText should have content");
   }

   [Fact]
   public async Task FindReferences_FileNotFound_ThrowsToolException()
   {
      var lookup = new LookupByPosition { FilePath = "nonexistent.cs", Line = 1, Column = 1 };
      await AssertThrowsToolExceptionAsync(
         () => _tool.FindReferencesAsync(lookup, cancellationToken: CT));
   }

   // ===========================================
   // Reference kind classification tests
   // ===========================================

   [Fact]
   public async Task FindReferences_PropertyRead_DetectsReadKind()
   {
      var lookup = new LookupByName { Name = "ReferenceKindTarget.Value" };

      var result = await _tool.FindReferencesAsync(lookup, verbosity: ToolResultVerbosity.Full, cancellationToken: CT);
      var data = GetTypedData<FindReferencesResponse>(result);
      Assert.NotNull(data.References);
      var refs = data.References;

      // Value property: 1 read (ReadExamples) + 2 writes (WriteExamples) = 3 total
      Assert.Equal(3, refs.Count);

      // Filter to references in ReadExamples (reads: "var v = target.Value")
      var readRefs = refs.Where(r => r.LineText?.Contains("var v = target.Value") == true).ToList();
      Assert.Single(readRefs);
      Assert.Equal(ReferenceKind.Read, readRefs[0].Kind);
   }

   [Fact]
   public async Task FindReferences_PropertyWrite_DetectsWriteKind()
   {
      var lookup = new LookupByName { Name = "ReferenceKindTarget.Value" };

      var result = await _tool.FindReferencesAsync(lookup, verbosity: ToolResultVerbosity.Full, cancellationToken: CT);
      var data = GetTypedData<FindReferencesResponse>(result);
      Assert.NotNull(data.References);
      var refs = data.References;

      // Value property: 1 read (ReadExamples) + 2 writes (WriteExamples) = 3 total
      Assert.Equal(3, refs.Count);

      // Direct assignment: "target.Value = 42"
      var directWrite = refs.Where(r => r.LineText?.Contains("target.Value = 42") == true).ToList();
      Assert.Single(directWrite);
      Assert.Equal(ReferenceKind.Write, directWrite[0].Kind);

      // Compound assignment: "target.Value += 10"
      var compoundWrite = refs.Where(r => r.LineText?.Contains("target.Value += 10") == true).ToList();
      Assert.Single(compoundWrite);
      Assert.Equal(ReferenceKind.Write, compoundWrite[0].Kind);
   }

   [Fact]
   public async Task FindReferences_MethodCall_DetectsCallKind()
   {
      var lookup = new LookupByName { Name = "ReferenceKindTarget.DoWork" };

      var result = await _tool.FindReferencesAsync(lookup, verbosity: ToolResultVerbosity.Full, cancellationToken: CT);
      var data = GetTypedData<FindReferencesResponse>(result);
      Assert.NotNull(data.References);
      var refs = data.References;

      // DoWork: 2 calls in CallExamples
      Assert.Equal(2, refs.Count);

      // All references to DoWork in CallExamples should be calls
      var callRefs = refs.Where(r => r.LineText?.Contains("DoWork()") == true).ToList();
      Assert.Equal(2, callRefs.Count);
      Assert.All(callRefs, r => Assert.Equal(ReferenceKind.Call, r.Kind));
   }

   [Fact]
   public async Task FindReferences_Constructor_DetectsCallKind()
   {
      var lookup = new LookupByName { Name = "ReferenceKindTarget" };

      var result = await _tool.FindReferencesAsync(lookup, verbosity: ToolResultVerbosity.Full, cancellationToken: CT);
      var data = GetTypedData<FindReferencesResponse>(result);
      Assert.NotNull(data.References);
      var refs = data.References;

      // "new ReferenceKindTarget()" should be classified as Call
      var ctorRefs = refs.Where(r => r.LineText?.Contains("new ReferenceKindTarget()") == true).ToList();
      Assert.Single(ctorRefs);
      Assert.Equal(ReferenceKind.Call, ctorRefs[0].Kind);
   }

   [Fact]
   public async Task FindReferences_MixedKinds_DetectsAllKinds()
   {
      var lookup = new LookupByName { Name = "ReferenceKindTarget.Value" };

      var result = await _tool.FindReferencesAsync(lookup, verbosity: ToolResultVerbosity.Full, cancellationToken: CT);
      var data = GetTypedData<FindReferencesResponse>(result);
      Assert.NotNull(data.References);
      var refs = data.References;

      var kinds = refs.Select(r => r.Kind).Distinct().ToList();
      // Value property should have exactly Read and Write references (no Call)
      Assert.Equivalent(new[] { ReferenceKind.Read, ReferenceKind.Write }, kinds);
   }

   [Fact]
   public async Task FindReferences_KindSerializesAsCamelCase()
   {
      var lookup = new LookupByName { Name = "ReferenceKindTarget.DoWork" };

      var result = await _tool.FindReferencesAsync(lookup, verbosity: ToolResultVerbosity.Full, cancellationToken: CT);
      var data = GetData(result);
      var references = GetNode(data, "References")?.AsArray();
      Assert.NotNull(references);
      Assert.Equal(2, references.Count);

      // Verify that Kind serializes as a camelCase string (e.g., "call" not "Call" or 2)
      var kindValue = GetStringProperty(references[0], "Kind");
      Assert.Equal("call", kindValue);
   }

   [Fact]
   public async Task FindReferences_RefArgument_DetectsWriteKind()
   {
      var lookup = new LookupByName { Name = "ReferenceKindTarget.Counter" };

      var result = await _tool.FindReferencesAsync(lookup, verbosity: ToolResultVerbosity.Full, cancellationToken: CT);
      var data = GetTypedData<FindReferencesResponse>(result);
      Assert.NotNull(data.References);
      var refs = data.References;

      // "TakeRef(ref target.Counter)" — ref argument should be classified as Write
      var refArgRefs = refs.Where(r => r.LineText?.Contains("ref target.Counter") == true).ToList();
      Assert.Single(refArgRefs);
      Assert.Equal(ReferenceKind.Write, refArgRefs[0].Kind);
   }
}
