using DotLens.Infrastructure;
using Xunit;

namespace DotLens.Tests.Infrastructure;

public class SolutionDiscoveryTests : IDisposable
{
   private readonly string _tempDir;

   public SolutionDiscoveryTests()
   {
      _tempDir = Path.Combine(Path.GetTempPath(), $"DotLensTests_{Guid.NewGuid():N}");
      Directory.CreateDirectory(_tempDir);
   }

   public void Dispose()
   {
      if (Directory.Exists(_tempDir))
         Directory.Delete(_tempDir, recursive: true);
   }

   private string CreateFile(string name)
   {
      var path = Path.Combine(_tempDir, name);
      File.WriteAllText(path, "");
      return path;
   }

   private string CreateSubDir(string name)
   {
      var path = Path.Combine(_tempDir, name);
      Directory.CreateDirectory(path);
      return path;
   }

   #region Auto-discovery from CWD (no SolutionPath configured)

   [Fact]
   public void Discover_SingleSlnInCwd_ReturnsLoad()
   {
      var slnPath = CreateFile("MyApp.sln");

      var result = SolutionDiscovery.Discover(null, autoLoadSolution: true, _tempDir);

      var load = Assert.IsType<SolutionDiscoveryResult.Load>(result);
      Assert.Equal(slnPath, load.SolutionPath);
   }

   [Fact]
   public void Discover_SingleSlnxInCwd_ReturnsLoad()
   {
      var slnxPath = CreateFile("MyApp.slnx");

      var result = SolutionDiscovery.Discover(null, autoLoadSolution: true, _tempDir);

      var load = Assert.IsType<SolutionDiscoveryResult.Load>(result);
      Assert.Equal(slnxPath, load.SolutionPath);
   }

   [Fact]
   public void Discover_MultipleSlnInCwd_ReturnsMultipleSolutions()
   {
      CreateFile("App1.sln");
      CreateFile("App2.sln");

      var result = SolutionDiscovery.Discover(null, autoLoadSolution: true, _tempDir);

      var multiple = Assert.IsType<SolutionDiscoveryResult.MultipleSolutions>(result);
      Assert.Equal(2, multiple.Solutions.Length);
   }

   [Fact]
   public void Discover_MixedSlnAndSlnxInCwd_ReturnsMultipleSolutions()
   {
      CreateFile("App.sln");
      CreateFile("App.slnx");

      var result = SolutionDiscovery.Discover(null, autoLoadSolution: true, _tempDir);

      var multiple = Assert.IsType<SolutionDiscoveryResult.MultipleSolutions>(result);
      Assert.Equal(2, multiple.Solutions.Length);
   }

   [Fact]
   public void Discover_NoSolutionsInCwd_ReturnsNone()
   {
      CreateFile("readme.md");

      var result = SolutionDiscovery.Discover(null, autoLoadSolution: true, _tempDir);

      Assert.IsType<SolutionDiscoveryResult.None>(result);
   }

   [Fact]
   public void Discover_EmptyCwd_ReturnsNone()
   {
      var result = SolutionDiscovery.Discover(null, autoLoadSolution: true, _tempDir);

      Assert.IsType<SolutionDiscoveryResult.None>(result);
   }

   [Fact]
   public void Discover_AutoLoadDisabled_ReturnsDisabled()
   {
      CreateFile("MyApp.sln");

      var result = SolutionDiscovery.Discover(null, autoLoadSolution: false, _tempDir);

      Assert.IsType<SolutionDiscoveryResult.Disabled>(result);
   }

   [Fact]
   public void Discover_EmptySolutionPath_TreatedAsNull()
   {
      CreateFile("MyApp.sln");

      var result = SolutionDiscovery.Discover("", autoLoadSolution: true, _tempDir);

      Assert.IsType<SolutionDiscoveryResult.Load>(result);
   }

   #endregion

   #region Configured SolutionPath (file)

   [Fact]
   public void Discover_SolutionPathIsAbsoluteFile_ReturnsLoad()
   {
      var slnPath = CreateFile("MyApp.sln");

      var result = SolutionDiscovery.Discover(slnPath, autoLoadSolution: true, _tempDir);

      var load = Assert.IsType<SolutionDiscoveryResult.Load>(result);
      Assert.Equal(slnPath, load.SolutionPath);
   }

   [Fact]
   public void Discover_SolutionPathIsRelativeFile_ResolvesAndReturnsLoad()
   {
      CreateFile("MyApp.sln");

      var result = SolutionDiscovery.Discover("MyApp.sln", autoLoadSolution: true, _tempDir);

      var load = Assert.IsType<SolutionDiscoveryResult.Load>(result);
      Assert.Equal(Path.Combine(_tempDir, "MyApp.sln"), load.SolutionPath);
   }

   [Fact]
   public void Discover_SolutionPathFileNotFound_ReturnsConfiguredPathNotFound()
   {
      var result = SolutionDiscovery.Discover(
         Path.Combine(_tempDir, "NonExistent.sln"), autoLoadSolution: true, _tempDir);

      var notFound = Assert.IsType<SolutionDiscoveryResult.ConfiguredPathNotFound>(result);
      Assert.Contains("NonExistent.sln", notFound.Path);
   }

   #endregion

   #region Configured SolutionPath (directory)

   [Fact]
   public void Discover_SolutionPathIsDirectoryWithOneSln_ReturnsLoad()
   {
      var subDir = CreateSubDir("projects");
      var slnPath = Path.Combine(subDir, "MyApp.sln");
      File.WriteAllText(slnPath, "");

      var result = SolutionDiscovery.Discover(subDir, autoLoadSolution: true, _tempDir);

      var load = Assert.IsType<SolutionDiscoveryResult.Load>(result);
      Assert.Equal(slnPath, load.SolutionPath);
   }

   [Fact]
   public void Discover_SolutionPathIsDirectoryWithMultipleSln_ReturnsMultiple()
   {
      var subDir = CreateSubDir("projects");
      File.WriteAllText(Path.Combine(subDir, "App1.sln"), "");
      File.WriteAllText(Path.Combine(subDir, "App2.sln"), "");

      var result = SolutionDiscovery.Discover(subDir, autoLoadSolution: true, _tempDir);

      var multiple = Assert.IsType<SolutionDiscoveryResult.MultipleSolutions>(result);
      Assert.Equal(2, multiple.Solutions.Length);
   }

   [Fact]
   public void Discover_SolutionPathIsDirectoryWithNoSln_ReturnsNone()
   {
      var subDir = CreateSubDir("empty");

      var result = SolutionDiscovery.Discover(subDir, autoLoadSolution: true, _tempDir);

      Assert.IsType<SolutionDiscoveryResult.None>(result);
   }

   [Fact]
   public void Discover_SolutionPathIsRelativeDirectory_ResolvesAndSearches()
   {
      var subDir = CreateSubDir("projects");
      var slnPath = Path.Combine(subDir, "MyApp.sln");
      File.WriteAllText(slnPath, "");

      var result = SolutionDiscovery.Discover("projects", autoLoadSolution: true, _tempDir);

      var load = Assert.IsType<SolutionDiscoveryResult.Load>(result);
      Assert.Equal(slnPath, load.SolutionPath);
   }

   #endregion

   #region SolutionPath takes precedence over auto-discovery

   [Fact]
   public void Discover_SolutionPathSet_IgnoresCwdSolutions()
   {
      // CWD has a solution, but SolutionPath points to a specific file
      CreateFile("CwdApp.sln");
      var subDir = CreateSubDir("other");
      var targetSln = Path.Combine(subDir, "Target.sln");
      File.WriteAllText(targetSln, "");

      var result = SolutionDiscovery.Discover(targetSln, autoLoadSolution: true, _tempDir);

      var load = Assert.IsType<SolutionDiscoveryResult.Load>(result);
      Assert.Equal(targetSln, load.SolutionPath);
   }

   [Fact]
   public void Discover_SolutionPathSet_AutoLoadSettingIgnored()
   {
      var slnPath = CreateFile("MyApp.sln");

      // Even with autoLoadSolution=false, explicit SolutionPath still works
      var result = SolutionDiscovery.Discover(slnPath, autoLoadSolution: false, _tempDir);

      var load = Assert.IsType<SolutionDiscoveryResult.Load>(result);
      Assert.Equal(slnPath, load.SolutionPath);
   }

   #endregion

   #region FindSolutions ordering

   [Fact]
   public void FindSolutions_ReturnsSortedByName()
   {
      CreateFile("Zebra.sln");
      CreateFile("Alpha.slnx");
      CreateFile("Bravo.sln");

      var solutions = SolutionDiscovery.FindSolutions(_tempDir);

      Assert.Equal(3, solutions.Length);
      Assert.Equal("Alpha.slnx", Path.GetFileName(solutions[0]));
      Assert.Equal("Bravo.sln", Path.GetFileName(solutions[1]));
      Assert.Equal("Zebra.sln", Path.GetFileName(solutions[2]));
   }

   [Fact]
   public void FindSolutions_DoesNotSearchSubdirectories()
   {
      CreateFile("Root.sln");
      var subDir = CreateSubDir("sub");
      File.WriteAllText(Path.Combine(subDir, "Nested.sln"), "");

      var solutions = SolutionDiscovery.FindSolutions(_tempDir);

      var solution = Assert.Single(solutions);
      Assert.Equal("Root.sln", Path.GetFileName(solution));
   }

   #endregion
}
