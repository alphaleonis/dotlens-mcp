using System.Diagnostics;
using Xunit;

namespace DotLens.Tests.Infrastructure;

/// <summary>
/// Covers the temp copy of the test fixtures that <see cref="IsolatedProjectTestBase"/> creates.
/// </summary>
public class IsolatedProjectTestBaseTests : IsolatedProjectTestBase
{
   /// <summary>
   /// The two SDKs can only differ on a machine with an SDK newer than the one global.json selects.
   /// </summary>
   [Fact]
   public async Task TempCopy_ResolvesSameSdkAsRepo()
   {
      var repoSdk = await GetSdkVersionAsync(FindRepoRoot());
      var tempCopySdk = await GetSdkVersionAsync(TempProjectDir);

      Assert.Equal(repoSdk, tempCopySdk);
   }

   private async Task<string> GetSdkVersionAsync(string workingDirectory)
   {
      var startInfo = new ProcessStartInfo("dotnet", "--version")
      {
         WorkingDirectory = workingDirectory,
         RedirectStandardOutput = true,
         RedirectStandardError = true,
         UseShellExecute = false,
      };

      using var process = Process.Start(startInfo)
         ?? throw new InvalidOperationException("Could not start 'dotnet --version'");
      var outputTask = process.StandardOutput.ReadToEndAsync(CT);
      var errorTask = process.StandardError.ReadToEndAsync(CT);
      await process.WaitForExitAsync(CT);
      var output = await outputTask;
      var error = await errorTask;

      Assert.True(process.ExitCode == 0, $"'dotnet --version' in {workingDirectory} exited with {process.ExitCode}: {error}");
      return output.Trim();
   }
}
