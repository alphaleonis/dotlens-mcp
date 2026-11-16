using DotLens.Infrastructure;
using DotLens.Tools.Analysis;
using DotLens.Tools.Shared;
using Xunit;

namespace DotLens.Tests.Integration;

/// <summary>
/// Covers DotLens tools on a project that targets two frameworks, which the workspace loads as two projects that
/// share each source file.
/// </summary>
public class MultiTargetFixtureTests : IClassFixture<MultiTargetFixtureWorkspace>
{
   private readonly MultiTargetFixtureWorkspace _fixture;

   public MultiTargetFixtureTests(MultiTargetFixtureWorkspace fixture)
   {
      _fixture = fixture;
   }

   private static CancellationToken CT => TestContext.Current.CancellationToken;

   [Fact]
   public void Load_HasOneProjectPerTargetFramework()
   {
      var projects = _fixture.Workspace.EnsureSolutionLoaded().Projects;

      Assert.Equivalent(
         new[] { "MultiTargetTestProject(net10.0)", "MultiTargetTestProject(net11.0)" },
         projects.Select(p => p.Name),
         strict: true);
   }

   /// <summary>
   /// Find-references also returns the call from Outer to Inner in the other project's copy of InternalOnly.cs.
   /// </summary>
   [Fact]
   public async Task FindUnusedCode_StaticClassUsedOnlyByItsOwnMembers_ReportedInEachProject()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equal(
         [
            (typeof(ClassSymbolResponse), "InternalOnly", "InternalOnly.cs", 3),
            (typeof(MethodSymbolResponse), "Outer", "InternalOnly.cs", 5),
            (typeof(ClassSymbolResponse), "InternalOnly", "InternalOnly.cs", 3),
            (typeof(MethodSymbolResponse), "Outer", "InternalOnly.cs", 5),
         ],
         result.Data.UnusedSymbols.Select(s => (s.GetType(), s.Name, s.Location?.FilePath, s.Location?.Line ?? 0)));
   }

   [Fact]
   public async Task FindUnusedCode_EntryPointAndItsClass_NotReported()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Empty(result.Data.UnusedSymbols.Where(s => s.Location?.FilePath == "Program.cs").Select(s => s.Name));
   }
}
