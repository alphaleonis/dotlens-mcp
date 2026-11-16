using System.Collections.Concurrent;
using System.Diagnostics;

namespace DotLens.Tests;

/// <summary>
/// Runs <c>dotnet restore</c> on a test fixture solution before a workspace loads it.
/// </summary>
/// <remarks>
/// MSBuildWorkspace does not restore. An unrestored project resolves no framework references when the SDK does not
/// bundle the targeting pack that its target framework needs, as SDK 11 does not for <c>net10.0</c>.
/// </remarks>
internal static class TestFixtureRestore
{
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
