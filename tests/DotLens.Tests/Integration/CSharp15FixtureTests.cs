using DotLens.Tools.Analysis;
using Xunit;

namespace DotLens.Tests.Integration;

/// <summary>
/// Covers DotLens tools on a <c>net11.0</c> project that uses C# 15 unions, labeled <c>break</c> and
/// <c>continue</c>, and an extension indexer.
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
      Assert.Equivalent(new[] { "Unions.cs", "LabeledLoops.cs", "ExtensionIndexer.cs" }, sourceFiles, strict: true);

      var tool = new GetDiagnosticsTool(_fixture.Workspace);
      var result = await tool.GetDiagnosticsAsync(severity: DiagnosticSeverityDto.Error, cancellationToken: CT);

      Assert.Empty(result.Data.Diagnostics.Select(d => $"{d.Id} at {d.Location}: {d.Message}"));
      Assert.Equal(0, result.Data.ErrorCount);
   }
}
