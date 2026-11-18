using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace DotLens.Infrastructure;

/// <summary>
/// Reads the Compile items that MSBuild's evaluation of a workspace project yields, by running
/// <c>dotnet msbuild -getItem</c>.
/// </summary>
/// <remarks>
/// Each workspace project is one target framework of its project file, so the evaluation sets that target framework.
/// All evaluations of one instance share a time budget, so a write that evaluates before and after it applies its
/// changes still answers within the request timeout.
/// </remarks>
/// <param name="cancellationToken">Ends the evaluation in progress and fails every later one.</param>
internal sealed partial class EvaluatedCompileItems(TimeSpan budget, CancellationToken cancellationToken)
{
   private readonly Stopwatch _elapsed = Stopwatch.StartNew();
   private readonly Dictionary<ProjectId, IReadOnlyList<EvaluatedCompileItem>> _cache = [];

   /// <summary>
   /// Returns the evaluated Compile items of the project, each with its full path and the file that defines it.
   /// </summary>
   /// <param name="project">The workspace project, which names the project file and the target framework.</param>
   /// <param name="reuse">Whether an earlier evaluation of the same project may answer.</param>
   /// <exception cref="ProjectEvaluationException">MSBuild could not evaluate the project within the budget.</exception>
   /// <exception cref="OperationCanceledException">The cancellation token was canceled.</exception>
   public IReadOnlyList<EvaluatedCompileItem> Get(Project project, bool reuse)
   {
      if (reuse && _cache.TryGetValue(project.Id, out var cached))
         return cached;

      var projectPath = project.FilePath ?? throw new ProjectEvaluationException($"Project {project.Name} has no project file");
      var host = DotnetHost.Find(project);
      var targetFramework = TargetFrameworkFromName(project, projectPath);
      var evaluation = Run(projectPath, targetFramework, host.Path);

      // A project that lists one target framework in TargetFrameworks loads as that target framework, without a suffix.
      if (evaluation.TargetFramework.Length == 0 && targetFramework is null && evaluation.TargetFrameworks.Length == 1)
         evaluation = Run(projectPath, evaluation.TargetFrameworks[0], host.Path);

      if (evaluation.TargetFramework.Length == 0 && evaluation.TargetFrameworks.Length > 0)
         throw new ProjectEvaluationException($"Could not tell which target framework of {projectPath} project {project.Name} builds");

      if (host.SdkDirectory is { } sdkDirectory && !PathUtils.PathComparer.Equals(
         Path.TrimEndingDirectorySeparator(evaluation.SdkDirectory), Path.TrimEndingDirectorySeparator(sdkDirectory)))
      {
         throw new ProjectEvaluationException(
            $"'{host.Path} msbuild' used the SDK in {evaluation.SdkDirectory}, but the workspace loaded {projectPath} with the SDK in {sdkDirectory}");
      }

      _cache[project.Id] = evaluation.Items;
      return evaluation.Items;
   }

   /// <returns>
   /// The target framework that MSBuildWorkspace appends to the name of a project that it loads once per target
   /// framework, or null when the name has no such suffix.
   /// </returns>
   internal static string? TargetFrameworkFromName(Project project, string projectPath)
   {
      var prefix = Path.GetFileNameWithoutExtension(projectPath) + "(";
      return project.Name.Length > prefix.Length + 1 && project.Name.StartsWith(prefix, StringComparison.Ordinal) && project.Name.EndsWith(')')
         ? project.Name[prefix.Length..^1]
         : null;
   }

   private Evaluation Run(string projectPath, string? targetFramework, string hostPath)
   {
      cancellationToken.ThrowIfCancellationRequested();
      var remaining = budget - _elapsed.Elapsed;
      if (remaining <= TimeSpan.Zero)
         throw new ProjectEvaluationException($"No time was left to evaluate {projectPath} within {budget.TotalSeconds} seconds");

      var startInfo = new ProcessStartInfo(hostPath)
      {
         WorkingDirectory = Path.GetDirectoryName(projectPath) ?? "",
         RedirectStandardOutput = true,
         RedirectStandardError = true,
         UseShellExecute = false,
      };
      startInfo.ArgumentList.Add("msbuild");
      startInfo.ArgumentList.Add(projectPath);
      startInfo.ArgumentList.Add("-getItem:Compile");
      foreach (var property in EvaluatedProperties)
         startInfo.ArgumentList.Add("-getProperty:" + property);
      startInfo.ArgumentList.Add("-nologo");
      // Response files such as Directory.Build.rsp apply only to command-line builds, so the workspace never read them.
      // https://learn.microsoft.com/visualstudio/msbuild/msbuild-response-files
      startInfo.ArgumentList.Add("-noAutoResponse");
      if (targetFramework is not null)
         startInfo.ArgumentList.Add("-p:TargetFramework=" + targetFramework);

      var output = new StringBuilder();
      using var process = new Process { StartInfo = startInfo };
      process.OutputDataReceived += (_, e) => AppendLine(output, e.Data);
      process.ErrorDataReceived += (_, e) => AppendLine(output, e.Data);
      try
      {
         process.Start();
      }
      catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
      {
         throw new ProjectEvaluationException($"Could not start '{hostPath} msbuild': {ex.Message}");
      }

      try
      {
         using (cancellationToken.Register(() => Kill(process)))
         {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            var exited = process.WaitForExit(remaining);
            cancellationToken.ThrowIfCancellationRequested();
            if (!exited)
               throw new ProjectEvaluationException($"'{hostPath} msbuild' did not evaluate {projectPath} within {budget.TotalSeconds} seconds");

            // The parameterless overload waits until the redirected output is read to its end.
            // https://learn.microsoft.com/dotnet/api/system.diagnostics.process.waitforexit#system-diagnostics-process-waitforexit
            process.WaitForExit();
         }
      }
      finally
      {
         Kill(process);
      }

      string text;
      lock (output)
         text = output.ToString();

      if (process.ExitCode != 0)
         throw new ProjectEvaluationException($"'{hostPath} msbuild' exited with {process.ExitCode}:{Environment.NewLine}{text}");

      try
      {
         return Parse(text);
      }
      catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
      {
         throw new ProjectEvaluationException($"Could not read the output of 'dotnet msbuild': {ex.Message}{Environment.NewLine}{text}");
      }
   }

   private static void Kill(Process process)
   {
      try
      {
         if (!process.HasExited)
            process.Kill(entireProcessTree: true);
      }
      catch (InvalidOperationException)
      {
         // The process exited after the check.
      }
   }

   private static readonly string[] EvaluatedProperties = ["TargetFramework", "TargetFrameworks", "MSBuildExtensionsPath"];

   private static void AppendLine(StringBuilder output, string? line)
   {
      if (line is null)
         return;
      lock (output)
         output.AppendLine(line);
   }

   private static Evaluation Parse(string json)
   {
      using var document = JsonDocument.Parse(json);
      var properties = document.RootElement.GetProperty("Properties");
      string Property(string name) => properties.GetProperty(name).GetString() ?? "";

      List<EvaluatedCompileItem> items = [];
      if (document.RootElement.GetProperty("Items").TryGetProperty("Compile", out var compileItems))
      {
         items = [.. compileItems.EnumerateArray().Select(item => new EvaluatedCompileItem(
            item.GetProperty("FullPath").GetString() ?? throw new InvalidOperationException("A Compile item has no FullPath"),
            item.GetProperty("DefiningProjectFullPath").GetString()
               ?? throw new InvalidOperationException("A Compile item has no DefiningProjectFullPath")))];
      }

      return new Evaluation(
         items,
         Property("TargetFramework"),
         Property("TargetFrameworks").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
         Property("MSBuildExtensionsPath"));
   }

   /// <param name="SdkDirectory">The directory of the SDK that the evaluation used, such as <c>~/.dotnet/sdk/10.0.100</c>.</param>
   private sealed record Evaluation(
      IReadOnlyList<EvaluatedCompileItem> Items, string TargetFramework, string[] TargetFrameworks, string SdkDirectory);
}

/// <summary>
/// The dotnet executable that evaluates a workspace project, and the SDK that the workspace loaded the project with.
/// </summary>
/// <param name="Path">The full path of the dotnet executable, or <c>dotnet</c> to run the one on the PATH.</param>
/// <param name="SdkDirectory">
/// The directory of the SDK that the project's SDK analyzers or analyzer config files come from, or null when the
/// project has none.
/// </param>
internal sealed partial record DotnetHost(string Path, string? SdkDirectory)
{
   private static readonly string Executable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dotnet.exe" : "dotnet";

   /// <summary>
   /// Returns the dotnet executable of the SDK that the workspace loaded the project with.
   /// </summary>
   /// <remarks>
   /// MSBuildWorkspace picks the SDK from every dotnet installation, so the dotnet on the PATH may lack it
   /// (https://github.com/dotnet/roslyn/blob/1324e1d34a0490454837a22370e75bbaeecffb87/src/Workspaces/MSBuild/Core/MSBuild/BuildHostProcessManager.cs#L156-L163). The SDK
   /// adds its own analyzers and analyzer config files to a project, and their paths name its directory. A project
   /// without them, or with them outside every known installation, falls back to the installation that runs DotLens.
   /// </remarks>
   public static DotnetHost Find(Project project) => Find(project, KnownInstallations());

   /// <param name="installations">The dotnet root directories that the project's SDK may come from.</param>
   internal static DotnetHost Find(Project project, IReadOnlyCollection<string> installations)
   {
      var sdkFiles = project.AnalyzerReferences.Select(r => r.FullPath)
         .Concat(project.AnalyzerConfigDocuments.Select(d => d.FilePath));
      foreach (var file in sdkFiles.OfType<string>())
      {
         var match = SdkDirectoryInPath().Match(file);
         if (!match.Success)
            continue;

         // A path in the project only names an installation that DotLens found on its own, so a project cannot pick
         // the executable that runs.
         var sdkDirectory = match.Groups[1].Value;
         var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(sdkDirectory, "..", ".."));
         var dotnet = System.IO.Path.Combine(root, Executable);
         if (installations.Contains(System.IO.Path.TrimEndingDirectorySeparator(root), PathUtils.PathComparer) && File.Exists(dotnet))
            return new DotnetHost(dotnet, sdkDirectory);
      }

      var runtimeRoot = RuntimeRoot();
      var runtimeDotnet = System.IO.Path.Combine(runtimeRoot, Executable);
      return File.Exists(runtimeDotnet) ? new DotnetHost(runtimeDotnet, null) : new DotnetHost("dotnet", null);
   }

   /// <summary>
   /// Returns the dotnet root directories found without the project, each also with symbolic links resolved.
   /// </summary>
   /// <remarks>
   /// They are the root that runs DotLens and every root that MSBuildLocator searches when the workspace's build host
   /// picks a project's SDK
   /// (https://github.com/dotnet/roslyn/blob/1324e1d34a0490454837a22370e75bbaeecffb87/src/Workspaces/MSBuild/BuildHost/NetCoreBuildHost.cs#L23-L34):
   /// DOTNET_ROOT, the directory of DOTNET_HOST_PATH, DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR, and the installation of each
   /// dotnet on the PATH.
   /// https://github.com/microsoft/MSBuildLocator/blob/ff0b9004f2548ba616d4d1a0f901c842b1d7f993/src/MSBuildLocator/DotNetSdkLocationHelper.cs#L240-L281
   /// </remarks>
   internal static List<string> KnownInstallations() => KnownInstallations(Environment.GetEnvironmentVariable);

   /// <param name="environment">Returns the value of an environment variable, or null when it is not set.</param>
   internal static List<string> KnownInstallations(Func<string, string?> environment)
   {
      var roots = new List<string> { RuntimeRoot() };
      if (environment(Environment.Is64BitProcess ? "DOTNET_ROOT" : "DOTNET_ROOT(x86)") is { Length: > 0 } dotnetRoot)
         AddRoot(dotnetRoot);
      if (environment("DOTNET_HOST_PATH") is { Length: > 0 } hostPath && File.Exists(hostPath)
         && System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(hostPath)) is { } hostDirectory)
      {
         roots.Add(hostDirectory);
         if (System.IO.Path.GetDirectoryName(ResolveLinks(hostPath)) is { } resolvedHostDirectory)
            roots.Add(resolvedHostDirectory);
      }

      if (environment("DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR") is { Length: > 0 } cliDirectory)
         AddRoot(cliDirectory);

      var pathDirectories = (environment("PATH") ?? "")
         .Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
      foreach (var directory in pathDirectories.Where(d => File.Exists(System.IO.Path.Combine(d, Executable))))
         AddRoot(directory);

      return [.. roots
         .SelectMany(root => new[] { root, ResolveLinks(root) })
         .Select(root => System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(root)))
         .Distinct(PathUtils.PathComparer)];

      // MSBuildLocator uses the directory of the dotnet in a root after it resolves the dotnet's link.
      void AddRoot(string root)
      {
         roots.Add(root);
         var dotnet = System.IO.Path.Combine(root, Executable);
         if (File.Exists(dotnet) && System.IO.Path.GetDirectoryName(ResolveLinks(dotnet)) is { } resolvedDirectory)
            roots.Add(resolvedDirectory);
      }
   }

   // The runtime directory is <dotnet root>/shared/Microsoft.NETCore.App/<version>/.
   private static string RuntimeRoot() =>
      System.IO.Path.GetFullPath(System.IO.Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));

   private static string ResolveLinks(string path)
   {
      try
      {
         var target = File.Exists(path) ? File.ResolveLinkTarget(path, returnFinalTarget: true) : Directory.ResolveLinkTarget(path, returnFinalTarget: true);
         return target?.FullName ?? path;
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
         return path;
      }
   }

   // <dotnet root>/sdk/<version>/Sdks/<SDK name>/...
   [GeneratedRegex(@"^(.+[/\\]sdk[/\\][^/\\]+)[/\\]Sdks[/\\]")]
   private static partial Regex SdkDirectoryInPath();
}

/// <summary>
/// A Compile item from MSBuild's evaluation of a project.
/// </summary>
/// <param name="FullPath">The full path of the source file.</param>
/// <param name="DefiningProjectFullPath">The full path of the project file or imported file that defines the item.</param>
internal sealed record EvaluatedCompileItem(string FullPath, string DefiningProjectFullPath)
{
   /// <summary>
   /// Whether the item comes from the .NET SDK's default Compile glob, which this SDK file defines.
   /// </summary>
   public bool IsFromSdkDefaultGlob =>
      string.Equals(Path.GetFileName(DefiningProjectFullPath), "Microsoft.NET.Sdk.DefaultItems.props", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The failure of an MSBuild evaluation of a project.
/// </summary>
internal sealed class ProjectEvaluationException(string message) : Exception(message);
