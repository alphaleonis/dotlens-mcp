using DotLens.Infrastructure;
using DotLens.Infrastructure.Results;
using DotLens.Tools.Analysis;
using DotLens.Tools.Worker;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DotLens.Tests.Infrastructure;

/// <summary>
/// Covers how a load reports a project that resolved no framework references, and how get_diagnostics carries it.
/// </summary>
/// <remarks>
/// Each test loads its own copy of AnalyzerTestProject. Without a restore, SDK 11 resolves no framework references
/// for its net10.0 target, as <see cref="TestFixtureRestore"/> describes.
/// </remarks>
public sealed class UnresolvedFrameworkReferencesTests : IAsyncLifetime
{
   private const string ExpectedWarning =
      "Project AnalyzerTestProject resolved no framework references (System.Object is missing), so its diagnostics "
      + "are incomplete. Run dotnet restore and call reload_solution.";

   private string? _tempDir;
   private WorkspaceManager? _workspaceManager;

   public enum BuildRestore
   {
      None,
      Own,
      OtherProject,
   }

   public enum DiagnosticsScope
   {
      Solution,
      Project,
      File,
   }

   private static CancellationToken CT => TestContext.Current.CancellationToken;

   private string TempDir => _tempDir ?? throw new InvalidOperationException("Test not initialized");

   private WorkspaceManager Workspace => _workspaceManager ?? throw new InvalidOperationException("Test not initialized");

   private string SolutionPath => Path.Combine(TempDir, "Analyzer.slnx");

   public async ValueTask InitializeAsync()
   {
      _tempDir = Path.Combine(Path.GetTempPath(), $"DotLensUnresolvedFrameworkTest_{Guid.NewGuid():N}");
      var repoRoot = FindRepoRoot();
      var projectDir = Path.Combine(_tempDir, "AnalyzerTestProject");
      Directory.CreateDirectory(projectDir);
      foreach (var file in Directory.GetFiles(Path.Combine(repoRoot, "tests", "TestFixtures", "AnalyzerTestProject")))
         File.Copy(file, Path.Combine(projectDir, Path.GetFileName(file)));

      // Outside the repo, MSBuild would otherwise load the copy with the newest installed SDK.
      File.Copy(Path.Combine(repoRoot, "global.json"), Path.Combine(_tempDir, "global.json"));
      await File.WriteAllTextAsync(SolutionPath, """
         <Solution>
           <Project Path="AnalyzerTestProject/AnalyzerTestProject.csproj" />
         </Solution>
         """, CT);

      _workspaceManager = new WorkspaceManager(
         NullLogger<WorkspaceManager>.Instance, Options.Create(new DotLensOptions()));
   }

   public ValueTask DisposeAsync()
   {
      _workspaceManager?.Dispose();
      if (_tempDir is not null && Directory.Exists(_tempDir))
      {
         try
         {
            Directory.Delete(_tempDir, recursive: true);
         }
         catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
         {
            // A leftover temp directory does not affect other tests.
         }
      }

      return ValueTask.CompletedTask;
   }

   [Fact]
   public async Task LoadSolution_UnresolvedFrameworkReferences_ReportsTheProject()
   {
      var result = await Workspace.LoadSolutionAsync(SolutionPath, CT);

      Assert.Equal([ExpectedWarning], result.Data.Warnings);
      var project = Assert.Single(Workspace.EnsureSolutionLoaded().Projects);
      Assert.Equal([new LoadWarning(ExpectedWarning, project.Id)], Workspace.LoadWarnings);

      var health = Assert.IsType<WorkerHealthCheckReadyResult>((await Workspace.GetHealthCheckAsync()).Data);
      Assert.Equal([ExpectedWarning], health.Solution.LoadWarnings);
   }

   [Theory]
   [InlineData(DiagnosticsScope.Solution)]
   [InlineData(DiagnosticsScope.Project)]
   [InlineData(DiagnosticsScope.File)]
   public async Task GetDiagnostics_UnresolvedFrameworkReferences_FilteredCallCarriesTheWarning(DiagnosticsScope scope)
   {
      await Workspace.LoadSolutionAsync(SolutionPath, CT);
      var project = Assert.Single(Workspace.EnsureSolutionLoaded().Projects);

      var result = await GetAnalyzerDiagnosticsAsync(scope, project);

      Assert.Equal(ExpectedWarning, result.Metadata?.Warning);
   }

   [Theory]
   [InlineData(DiagnosticsScope.Solution)]
   [InlineData(DiagnosticsScope.Project)]
   [InlineData(DiagnosticsScope.File)]
   public async Task GetDiagnostics_NoUnresolvedFrameworkReferences_WhenRestored(DiagnosticsScope scope)
   {
      await TestFixtureRestore.EnsureRestoredAsync(SolutionPath);

      var loaded = await Workspace.LoadSolutionAsync(SolutionPath, CT);
      var project = Assert.Single(Workspace.EnsureSolutionLoaded().Projects);
      var result = await GetAnalyzerDiagnosticsAsync(scope, project);

      Assert.Empty(loaded.Data.Warnings);
      Assert.Empty(Workspace.LoadWarnings);
      Assert.Null(result.Metadata?.Warning);
   }

   /// <summary>
   /// The project opts out of framework references, as Microsoft.Build.NoTargets projects do, so only a missing restore
   /// is worth a warning. An assets file that another project's restore wrote to the same obj directory does not count.
   /// </summary>
   [Theory]
   [InlineData(BuildRestore.None)]
   [InlineData(BuildRestore.Own)]
   [InlineData(BuildRestore.OtherProject)]
   public async Task LoadSolution_NoFrameworkReferencesByDesign_UnresolvedFrameworkReferencesOnlyWithoutRestore(
      BuildRestore restore)
   {
      var projectDir = Path.Combine(TempDir, "Build");
      Directory.CreateDirectory(projectDir);
      await File.WriteAllTextAsync(Path.Combine(projectDir, "Build.csproj"), """
         <Project Sdk="Microsoft.NET.Sdk">
           <PropertyGroup>
             <TargetFramework>net10.0</TargetFramework>
             <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
             <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>
             <NoStdLib>true</NoStdLib>
           </PropertyGroup>
         </Project>
         """, CT);
      var solutionPath = Path.Combine(TempDir, "Build.slnx");
      await File.WriteAllTextAsync(solutionPath, """
         <Solution>
           <Project Path="Build/Build.csproj" />
         </Solution>
         """, CT);
      if (restore == BuildRestore.Own)
         await TestFixtureRestore.EnsureRestoredAsync(solutionPath);
      if (restore == BuildRestore.OtherProject)
      {
         await TestFixtureRestore.EnsureRestoredAsync(SolutionPath);
         Directory.CreateDirectory(Path.Combine(projectDir, "obj"));
         File.Copy(
            Path.Combine(TempDir, "AnalyzerTestProject", "obj", "project.assets.json"),
            Path.Combine(projectDir, "obj", "project.assets.json"));
      }

      var result = await Workspace.LoadSolutionAsync(solutionPath, CT);

      string[] expected = restore == BuildRestore.Own
         ? []
         : ["Project Build resolved no framework references (System.Object is missing), so its diagnostics are "
            + "incomplete. Run dotnet restore and call reload_solution."];
      Assert.Equal(expected, result.Data.Warnings);
   }

   private Task<ToolResult<GetDiagnosticsResponse>> GetAnalyzerDiagnosticsAsync(DiagnosticsScope scope, Project project)
   {
      var tool = new GetDiagnosticsTool(Workspace);
      return scope switch
      {
         DiagnosticsScope.Solution => tool.GetDiagnosticsAsync(
            idPattern: "CA*", includeAnalyzers: true, cancellationToken: CT),
         DiagnosticsScope.Project => tool.GetDiagnosticsAsync(
            projectPath: project.FilePath, idPattern: "CA*", includeAnalyzers: true, cancellationToken: CT),
         DiagnosticsScope.File => tool.GetDiagnosticsAsync(
            filePath: Path.Combine(TempDir, "AnalyzerTestProject", "AnalyzerWarnings.cs"),
            idPattern: "CA*",
            includeAnalyzers: true,
            cancellationToken: CT),
         _ => throw new InvalidOperationException($"Unexpected value: {scope}"),
      };
   }

   private static string FindRepoRoot()
   {
      for (var dir = Directory.GetCurrentDirectory(); dir is not null; dir = Directory.GetParent(dir)?.FullName)
      {
         if (File.Exists(Path.Combine(dir, "global.json")) && Directory.Exists(Path.Combine(dir, "tests", "TestFixtures")))
            return dir;
      }

      throw new InvalidOperationException("Could not find the repository root");
   }
}
