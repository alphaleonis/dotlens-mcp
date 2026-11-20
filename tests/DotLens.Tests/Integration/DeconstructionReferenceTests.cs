using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Navigation;
using DotLens.Tools.Shared;
using Xunit;

namespace DotLens.Tests.Integration;

/// <summary>
/// Covers the references and callers that find_references and find_callers report for the Deconstruct call behind a
/// positional pattern.
/// </summary>
public class DeconstructionReferenceTests : IClassFixture<UnusedCodeFixtureWorkspace>
{
   private readonly UnusedCodeFixtureWorkspace _fixture;

   public DeconstructionReferenceTests(UnusedCodeFixtureWorkspace fixture)
   {
      _fixture = fixture;
   }

   private static CancellationToken CT => TestContext.Current.CancellationToken;

   [Theory]
   [InlineData(27, 16, 98, 32)]
   [InlineData(52, 23, 104, 36)]
   public async Task FindReferences_DeconstructUsedOnlyByPositionalPattern_ReportsThePatternsPositionalClause(
      int declarationLine, int declarationColumn, int referenceLine, int referenceColumn)
   {
      var tool = new FindReferencesTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));
      var lookup = new LookupByPosition { FilePath = _fixture.GetPath("Deconstruction.cs"), Line = declarationLine, Column = declarationColumn };

      var result = await tool.FindReferencesAsync(lookup, cancellationToken: CT);

      Assert.Equal("Deconstruct", result.Data.Symbol.Name);
      Assert.Equal(1, result.Data.TotalReferences);
      var references = result.Data.References ?? throw new InvalidOperationException("Compact verbosity lists references");
      Assert.Equal(
         [("Deconstruction.cs", referenceLine, referenceColumn)],
         references.Select(r => (Path.GetFileName(r.FilePath), r.Line, r.Column)));
   }

   [Fact]
   public async Task FindReferences_DeconstructUsedOnlyByPositionalPattern_ReportsTheCallAsCall()
   {
      var tool = new FindReferencesTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));
      var lookup = new LookupByPosition { FilePath = _fixture.GetPath("Deconstruction.cs"), Line = 27, Column = 16 };

      var result = await tool.FindReferencesAsync(lookup, cancellationToken: CT);

      var references = result.Data.References ?? throw new InvalidOperationException("Compact verbosity lists references");
      Assert.Equal([(98, 32, ReferenceKind.Call)], references.Select(r => (r.Line, r.Column, r.Kind)));
      Assert.Equal(1, result.Data.TotalReferences);
   }

   [Fact]
   public async Task FindCallers_DeconstructUsedOnlyByPositionalPattern_ListsTheMemberThatHoldsThePattern()
   {
      var tool = new FindCallersTool(_fixture.Workspace, new SymbolLookupService(_fixture.Workspace));
      var lookup = new LookupByPosition { FilePath = _fixture.GetPath("Deconstruction.cs"), Line = 27, Column = 16 };

      var result = await tool.FindCallersAsync(lookup, cancellationToken: CT);

      var callers = result.Data.Callers ?? throw new InvalidOperationException("Compact verbosity lists callers");
      Assert.Equal(
         [("Run", "Deconstruction.cs", 98, 32)],
         callers.Select(c => (c.CallingSymbol?.Name, Path.GetFileName(c.Location.FilePath), c.Location.Line, c.Location.Column)));
      Assert.Equal(1, result.Data.TotalCallers);
   }
}
