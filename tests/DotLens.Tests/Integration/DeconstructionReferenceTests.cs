using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Navigation;
using DotLens.Tools.Shared;
using Xunit;

namespace DotLens.Tests.Integration;

/// <summary>
/// Covers the references that find_references reports for the Deconstruct call behind a positional pattern.
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
}
