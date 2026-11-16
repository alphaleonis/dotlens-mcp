using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Analysis;
using DotLens.Tools.Intelligence;
using DotLens.Tools.Search;
using Xunit;

namespace DotLens.Tests.Integration;

/// <summary>
/// Covers DotLens tools on a <c>net11.0</c> project that uses C# 15 unions, labeled <c>break</c> and
/// <c>continue</c>, and an extension indexer, plus type declarations with primary constructors.
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
      Assert.Equivalent(new[] { "Unions.cs", "LabeledLoops.cs", "ExtensionIndexer.cs", "PrimaryConstructors.cs" }, sourceFiles, strict: true);

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
}
