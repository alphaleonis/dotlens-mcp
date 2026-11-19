// Times get_complexity_metrics with and without the Cognitive metric, to measure what Cognitive's recursion-cycle call
// graph costs on a real project.
// Run from the repository root: dotnet run build/measure-cognitive-cost.cs -- <solution> <project>...
// Run it with no other build or test on the machine, because they distort the timings.

#:project ../src/DotLens/DotLens.csproj
#:property Configuration=Release
#:property PublishAot=false

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Analysis;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

const int Repetitions = 5;
const string EditMarker = "// measure-cognitive-cost edit";

if (args.Length < 2)
{
   Console.Error.WriteLine("Usage: dotnet run build/measure-cognitive-cost.cs -- <solution> <project>...");
   return 2;
}

var solutionPath = Path.GetFullPath(args[0]);
var projectNames = args[1..];

List<MetricType> defaultMetrics = [MetricType.Cyclomatic, MetricType.Nesting, MetricType.Loc, MetricType.Parameters, MetricType.Cognitive];
List<MetricType> baselineMetrics = [.. defaultMetrics.Where(m => m != MetricType.Cognitive)];

var dotLensAssembly = typeof(WorkspaceManager).Assembly;
Console.WriteLine($"DotLens {dotLensAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}, " +
   $"{dotLensAssembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration} build");
Console.WriteLine($"{RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}, {RuntimeInformation.OSArchitecture}");
Console.WriteLine($"{Environment.ProcessorCount} logical processors, {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024 * 1024.0):F1} GB available to the GC, " +
   $"{(System.Runtime.GCSettings.IsServerGC ? "server" : "workstation")} GC");
Console.WriteLine($"Solution: {solutionPath}");
Console.WriteLine();

using var workspace = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance, Options.Create(new DotLensOptions()));
var tool = new GetComplexityMetricsTool(workspace, new SymbolLookupService(workspace));

// The file that an after-sync edit changes, with its original bytes, so an interrupted run still puts it back.
(string Path, byte[] Bytes)? pendingRestore = null;
void Restore()
{
   if (pendingRestore is var (path, bytes))
   {
      File.WriteAllBytes(path, bytes);
      pendingRestore = null;
   }
}
Console.CancelKeyPress += (_, _) => Restore();
AppDomain.CurrentDomain.ProcessExit += (_, _) => Restore();

var loadTime = await TimeAsync(() => workspace.LoadSolutionAsync(solutionPath));
Console.WriteLine($"First load: {loadTime:F0} ms, {workspace.ProjectCount} projects, {workspace.DocumentCount} documents");
foreach (var warning in workspace.LoadWarnings)
   Console.WriteLine($"Load warning: {warning.Message}");
Console.WriteLine();

var targets = new List<Target>();
foreach (var projectName in projectNames)
   targets.Add(await SelectTargetAsync(workspace.EnsureSolutionLoaded(), projectName));

// Run every call once before the measurements, so no cold timing includes the JIT compiling DotLens and Roslyn code.
foreach (var target in targets)
{
   foreach (var fileMode in new[] { false, true })
   {
      foreach (var metrics in new[] { defaultMetrics, baselineMetrics })
         await CallAsync(target, fileMode, metrics);
   }
}

foreach (var target in targets)
{
   Console.WriteLine($"## {target.ProjectName}");
   Console.WriteLine($"File:   {target.RelativePath} ({target.FileBytes / 1024.0:F0} KB, {target.MethodCount} callable methods)");
   Console.WriteLine($"Method: {target.MethodName} at line {target.Line}, {target.MethodLines} lines");

   var rows = new List<(string Mode, string Metrics, Cell Cell)>();
   foreach (var fileMode in new[] { false, true })
   {
      foreach (var withCognitive in new[] { true, false })
      {
         var cell = await MeasureAsync(target, fileMode, withCognitive ? defaultMetrics : baselineMetrics);
         rows.Add((fileMode ? "file" : "method", withCognitive ? "default" : "without Cognitive", cell));
      }
   }

   var (declarations, trees, documents) = await CountCallableDeclarationsAsync(target.ProjectName);
   Console.WriteLine($"Compilation: {declarations} callable declarations in {trees} syntax trees, of which {documents} are project documents");
   Console.WriteLine();
   Console.WriteLine("| Mode | Metrics | Cold (ms) | Warm, median of 5 (ms) | After sync, median of 5 (ms) | Cognitive |");
   Console.WriteLine("|---|---|---:|---:|---:|---:|");
   foreach (var (mode, metrics, cell) in rows)
      Console.WriteLine($"| {mode} | {metrics} | {cell.Cold:F1} | {cell.Warm:F1} | {cell.AfterSync:F1} | {cell.Cognitive?.ToString() ?? "-"} |");
   foreach (var mode in new[] { "method", "file" })
   {
      var withCognitive = rows.Single(r => r.Mode == mode && r.Metrics == "default").Cell;
      var without = rows.Single(r => r.Mode == mode && r.Metrics != "default").Cell;
      Console.WriteLine($"| {mode} | Cognitive's cost | {withCognitive.Cold - without.Cold:F1} | {withCognitive.Warm - without.Warm:F1} | " +
         $"{withCognitive.AfterSync - without.AfterSync:F1} | |");
   }
   Console.WriteLine();
}

return 0;

async Task<Target> SelectTargetAsync(Solution solution, string projectName)
{
   // A multi-targeted project loads as one project per target framework, named "Name(tfm)".
   var project = solution.Projects.FirstOrDefault(p => p.Name == projectName)
      ?? solution.Projects.FirstOrDefault(p => p.Name.StartsWith(projectName + "(", StringComparison.Ordinal))
      ?? throw new InvalidOperationException($"No project named {projectName} in {solutionPath}");
   var objDirectory = Path.Combine(Path.GetDirectoryName(project.FilePath) ?? "", "obj") + Path.DirectorySeparatorChar;

   var candidates = project.Documents
      .Where(d => d.FilePath is { } path && File.Exists(path) && !path.StartsWith(objDirectory, StringComparison.Ordinal))
      .OrderByDescending(d => new FileInfo(d.FilePath!).Length);
   foreach (var document in candidates)
   {
      var root = await document.GetSyntaxRootAsync() ?? throw new InvalidOperationException($"No syntax root for {document.FilePath}");
      var methods = root.DescendantNodes().Where(n => n.IsCallableMethodSyntax()).ToList();
      if (methods.Count == 0)
         continue;

      var longest = methods.MaxBy(LineCount)!;
      var nameToken = GetNameToken(longest);
      var position = nameToken.GetLocation().GetLineSpan().StartLinePosition;
      var path = document.FilePath!;
      var bytes = File.ReadAllBytes(path);
      if (bytes.Length >= 2 && (bytes[0], bytes[1]) is (0xFF, 0xFE) or (0xFE, 0xFF))
         throw new InvalidOperationException($"{path} is UTF-16, and the after-sync edit appends UTF-8 text");
      if (Encoding.UTF8.GetString(bytes).Contains(EditMarker, StringComparison.Ordinal))
         throw new InvalidOperationException($"{path} holds an edit from an earlier run that did not finish. Remove the line with '{EditMarker}'.");

      return new Target(
         project.Name,
         path,
         Path.GetRelativePath(Path.GetDirectoryName(solutionPath)!, path),
         bytes.Length,
         methods.Count,
         nameToken.ValueText,
         position.Line + 1,
         position.Character + 1,
         LineCount(longest));
   }

   throw new InvalidOperationException($"No .cs file of {projectName} declares a callable method");
}

static int LineCount(SyntaxNode node)
{
   var span = node.GetLocation().GetLineSpan();
   return span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
}

static SyntaxToken GetNameToken(SyntaxNode node) => node switch
{
   MethodDeclarationSyntax m => m.Identifier,
   ConstructorDeclarationSyntax c => c.Identifier,
   DestructorDeclarationSyntax d => d.Identifier,
   OperatorDeclarationSyntax o => o.OperatorToken,
   ConversionOperatorDeclarationSyntax c => c.Type.GetFirstToken(),
   LocalFunctionStatementSyntax l => l.Identifier,
   _ => throw new InvalidOperationException($"Unexpected callable declaration: {node.Kind()}")
};

/// <summary>
/// Calls get_complexity_metrics on the target's method or file.
/// </summary>
/// <returns>The method's Cognitive value, or the file's largest one, or null when the metrics leave out Cognitive.</returns>
async Task<int?> CallAsync(Target target, bool fileMode, List<MetricType> metrics)
{
   ComplexityTarget complexityTarget = fileMode
      ? new ComplexityTargetFile { FilePath = target.Path }
      : new ComplexityTargetMethod { Method = new LookupByPosition { FilePath = target.Path, Line = target.Line, Column = target.Column } };
   var result = await tool.GetComplexityMetricsAsync(complexityTarget, metrics, ToolResultVerbosity.Summary);
   return result.Data switch
   {
      MethodComplexityResponse method when method.Name == target.MethodName => method.Metrics.Cognitive,
      MethodComplexityResponse method => throw new InvalidOperationException($"Measured {method.Name}, expected {target.MethodName}"),
      FileComplexityResponse file => file.FileTotals.MaxCognitive,
      var other => throw new InvalidOperationException($"Unexpected response: {other.GetType().Name}")
   };
}

async Task<Cell> MeasureAsync(Target target, bool fileMode, List<MetricType> metrics)
{
   Task<int?> CallTargetAsync() => CallAsync(target, fileMode, metrics);

   // Every cold call needs a compilation that nothing has built yet, so each one gets its own load.
   await workspace.LoadSolutionAsync(solutionPath);
   int? cognitive = null;
   var cold = await TimeAsync(async () => cognitive = await CallTargetAsync());

   var warm = new List<double>();
   for (var i = 0; i < Repetitions; i++)
      warm.Add(await TimeAsync(CallTargetAsync));

   var original = File.ReadAllBytes(target.Path);
   var text = Encoding.UTF8.GetString(original);
   var newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
   var separator = text.EndsWith('\n') ? "" : newLine;
   var afterSync = new List<double>();
   pendingRestore = (target.Path, original);
   try
   {
      for (var i = 0; i < Repetitions; i++)
      {
         // Each edit differs from the one before, so every sync gives the project a new compilation.
         File.WriteAllBytes(target.Path, [.. original, .. Encoding.UTF8.GetBytes($"{separator}{EditMarker} {i + 1}{newLine}")]);
         await workspace.SyncDocumentsAsync([target.Path]);
         afterSync.Add(await TimeAsync(CallTargetAsync));
      }
   }
   finally
   {
      Restore();
      await workspace.SyncDocumentsAsync([target.Path]);
   }

   return new Cell(cold, Median(warm), Median(afterSync), cognitive);
}

async Task<(int Declarations, int Trees, int Documents)> CountCallableDeclarationsAsync(string projectName)
{
   // A load gives every project a new id, so the measured project is found again by name.
   var project = workspace.EnsureSolutionLoaded().Projects.Single(p => p.Name == projectName);
   var compilation = await project.GetCompilationAsync() ?? throw new InvalidOperationException($"No compilation for {project.Name}");

   // The declarations that become nodes of Cognitive's call graph, as AnalysisHelpers.GetDeclaredCallGraphMethod finds them.
   var declarations = compilation.SyntaxTrees
      .SelectMany(tree => tree.GetRoot().DescendantNodes())
      .Count(node => node.IsCallableMethodSyntax()
         || node is AccessorDeclarationSyntax
         || node is ArrowExpressionClauseSyntax { Parent: PropertyDeclarationSyntax or IndexerDeclarationSyntax }
         || node is TypeDeclarationSyntax { ParameterList: not null });
   return (declarations, compilation.SyntaxTrees.Count(), project.DocumentIds.Count);
}

static async Task<double> TimeAsync<T>(Func<Task<T>> action)
{
   GC.Collect();
   GC.WaitForPendingFinalizers();
   GC.Collect();
   var stopwatch = Stopwatch.StartNew();
   await action();
   return stopwatch.Elapsed.TotalMilliseconds;
}

static double Median(List<double> values)
{
   var sorted = values.Order().ToList();
   return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
}

/// <summary>
/// The method and file that one project's measurements analyze: the longest callable method in the project's largest .cs file.
/// </summary>
record Target(
   string ProjectName,
   string Path,
   string RelativePath,
   long FileBytes,
   int MethodCount,
   string MethodName,
   int Line,
   int Column,
   int MethodLines);

/// <summary>
/// The timings of one mode and metric set, in milliseconds, and the Cognitive value the first call reported.
/// </summary>
record Cell(double Cold, double Warm, double AfterSync, int? Cognitive);
