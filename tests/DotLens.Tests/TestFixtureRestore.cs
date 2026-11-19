using System.Collections.Concurrent;
using System.Diagnostics;

namespace DotLens.Tests;

/// <summary>
/// Runs <c>dotnet restore</c> on a test fixture solution before a workspace loads it.
/// </summary>
/// <remarks>
/// MSBuildWorkspace does not restore. An unrestored project resolves its framework references only from the packs
/// folder of the dotnet root, and only when that folder holds the exact targeting pack version that the SDK expects.
/// SDK 11 does not bundle the pack for <c>net10.0</c>, so whether an unrestored <c>net10.0</c> project resolves depends
/// on the machine. <see cref="RequireRestoreForTargetingPacksAsync"/> removes that dependency.
/// </remarks>
internal static class TestFixtureRestore
{
   private const string EmptyTargetingPackRoot = "empty-targeting-pack-root";

   private static readonly ConcurrentDictionary<string, Lazy<Task>> s_restores = new(StringComparer.Ordinal);

   /// <summary>
   /// Restores the solution once per test run. Callers that pass the same path share one restore, so two restores
   /// never write the same <c>obj</c> folders at once.
   /// </summary>
   /// <remarks>
   /// It takes no cancellation token because the restore is shared, so one test's cancellation would fail it for all.
   /// </remarks>
   public static Task EnsureRestoredAsync(string solutionPath)
   {
      var fullPath = Path.GetFullPath(solutionPath);
      return s_restores.GetOrAdd(fullPath, path => new Lazy<Task>(() => RestoreAsync(path))).Value;
   }

   /// <summary>
   /// Writes a <c>Directory.Build.props</c> to the directory that points the targeting pack root of every project below
   /// it at an empty folder. Those projects then resolve framework references only through a restore.
   /// </summary>
   /// <remarks>
   /// The SDK target ProcessFrameworkReferences looks for a targeting pack in <c>NetCoreTargetingPackRoot</c>. It
   /// leaves a pack that is missing there to the restore.
   /// </remarks>
   public static async Task RequireRestoreForTargetingPacksAsync(string directory, CancellationToken cancellationToken)
   {
      Directory.CreateDirectory(Path.Combine(directory, EmptyTargetingPackRoot));
      await File.WriteAllTextAsync(Path.Combine(directory, "Directory.Build.props"), $"""
         <Project>
           <PropertyGroup>
             <NetCoreTargetingPackRoot>$(MSBuildThisFileDirectory){EmptyTargetingPackRoot}</NetCoreTargetingPackRoot>
           </PropertyGroup>
         </Project>
         """, cancellationToken);
   }

   private static async Task RestoreAsync(string solutionPath)
   {
      var solutionDir = Path.GetDirectoryName(solutionPath)
         ?? throw new ArgumentException($"Solution path has no directory: {solutionPath}", nameof(solutionPath));

      // The working directory decides which global.json, and so which SDK, the restore uses.
      var startInfo = new ProcessStartInfo("dotnet")
      {
         WorkingDirectory = solutionDir,
         RedirectStandardOutput = true,
         RedirectStandardError = true,
         UseShellExecute = false,
      };
      startInfo.ArgumentList.Add("restore");
      startInfo.ArgumentList.Add(solutionPath);
      // Without this flag, the restore leaves MSBuild nodes running after the test run ends.
      startInfo.ArgumentList.Add("--disable-build-servers");

      using var process = Process.Start(startInfo)
         ?? throw new InvalidOperationException($"Could not start 'dotnet restore' for {solutionPath}");
      var outputTask = process.StandardOutput.ReadToEndAsync();
      var errorTask = process.StandardError.ReadToEndAsync();
      await process.WaitForExitAsync();
      var output = await outputTask;
      var error = await errorTask;

      if (process.ExitCode != 0)
      {
         throw new InvalidOperationException(
            $"'dotnet restore {solutionPath}' exited with {process.ExitCode}:{Environment.NewLine}{output}{error}");
      }
   }
}
