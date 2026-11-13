// Generates THIRD-PARTY-NOTICES.txt from the packages that the DotLens tool package redistributes.
// Run from the repository root after `dotnet tool restore`.

using System.Diagnostics;
using System.Text;
using System.Text.Json;

const string Project = "src/DotLens/DotLens.csproj";
const string ProjectAssets = "src/DotLens/obj/project.assets.json";
const string NoticesDir = "build/notices";
const string AllowedLicenses = "MIT;Apache-2.0";
const string BuildHostPackage = "Microsoft.CodeAnalysis.Workspaces.MSBuild";

var root = Environment.CurrentDirectory;
if (!File.Exists(Path.Combine(root, Project)))
   return Fail($"Run this from the repository root: {Project} not found.");

if (Run("dotnet", "restore", Project) != 0)
   return Fail("dotnet restore failed.");

var scanFile = Path.GetTempFileName();
try
{
   if (Run("dotnet", "nuget-license", "-i", Project, "-t",
          "-a", AllowedLicenses,
          "-ignore", Path.Combine(NoticesDir, "ignored-packages.json"),
          "-o", "Json", "-fo", scanFile) != 0)
      return Fail($"nuget-license failed. A dependency outside {AllowedLicenses} must be reviewed before it ships.");

   var scanned = ReadPackages(scanFile);
   var bundled = ReadPackages(Path.Combine(NoticesDir, "bundled.json"));

   var packageFolder = GetPackageFolder(ProjectAssets);
   var buildHostErrors = CheckBuildHostCoverage(packageFolder, scanned, bundled);
   if (buildHostErrors.Count > 0)
      return Fail(string.Join(Environment.NewLine, buildHostErrors));

   var packages = scanned.Concat(bundled)
      .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
      .ToList();

   var duplicates = packages.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
   if (duplicates.Count > 0)
      return Fail($"Listed twice (scanned and in bundled.json): {string.Join(", ", duplicates)}");

   File.WriteAllText(Path.Combine(root, "THIRD-PARTY-NOTICES.txt"), Render(packages), new UTF8Encoding(false));
   Console.WriteLine($"Generated THIRD-PARTY-NOTICES.txt with {packages.Count} packages");
   return 0;
}
finally
{
   File.Delete(scanFile);
}

static string Render(List<Package> packages)
{
   var sb = new StringBuilder();
   sb.AppendLine("THIRD-PARTY SOFTWARE NOTICES AND INFORMATION");
   sb.AppendLine("=============================================");
   sb.AppendLine();
   sb.AppendLine("DotLens includes the third-party components listed below. Each component is");
   sb.AppendLine("distributed under the license named in its entry. The full text of each");
   sb.AppendLine("license follows the list.");
   sb.AppendLine();

   foreach (var package in packages)
   {
      sb.AppendLine("-------------------------------------------------------------");
      sb.AppendLine($"{package.Id} {package.Version}");
      sb.AppendLine($"License: {package.License}");
      if (!string.IsNullOrWhiteSpace(package.ProjectUrl))
         sb.AppendLine($"Project URL: {package.ProjectUrl}");
      if (!string.IsNullOrWhiteSpace(package.Copyright))
         sb.AppendLine($"Copyright: {package.Copyright.Trim()}");
      if (package.IsBundled)
         sb.AppendLine($"Redistributed inside {BuildHostPackage}.");
      sb.AppendLine();
   }

   foreach (var license in packages.Select(p => p.License).Distinct().Order(StringComparer.Ordinal))
   {
      var path = Path.Combine(NoticesDir, $"{license}.txt");
      if (!File.Exists(path))
         throw new InvalidOperationException($"No license text for '{license}': add {path}.");

      sb.AppendLine("=============================================================");
      sb.AppendLine($"{license}");
      sb.AppendLine("=============================================================");
      sb.AppendLine();
      sb.AppendLine(File.ReadAllText(path).Trim());
      sb.AppendLine();
   }

   return sb.ToString().TrimEnd() + "\n";
}

// Every DLL in the BuildHost folders must be described by a scanned package or by an entry in
// bundled.json whose version matches the DLL, so a Roslyn upgrade cannot leave the notices stale.
static List<string> CheckBuildHostCoverage(string packageFolder, List<Package> scanned, List<Package> bundled)
{
   var errors = new List<string>();
   var host = scanned.SingleOrDefault(p => p.Id.Equals(BuildHostPackage, StringComparison.OrdinalIgnoreCase));
   if (host is null)
      return [$"{BuildHostPackage} is not among the scanned packages."];

   var contentDir = Path.Combine(packageFolder, host.Id.ToLowerInvariant(), host.Version, "contentFiles", "any", "any");
   var buildHostDlls = Directory.Exists(contentDir)
      ? Directory.GetDirectories(contentDir, "BuildHost-*")
         .SelectMany(d => Directory.GetFiles(d, "*.dll"))
         .Where(f => !Path.GetFileName(f).StartsWith("Microsoft.CodeAnalysis.", StringComparison.Ordinal))
         .ToList()
      : [];
   if (buildHostDlls.Count == 0)
      return [$"No BuildHost DLLs found under {contentDir}."];

   var scannedDllNames = scanned
      .Where(p => !p.Id.Equals(BuildHostPackage, StringComparison.OrdinalIgnoreCase))
      .SelectMany(p => EnumerateDllNames(Path.Combine(packageFolder, p.Id.ToLowerInvariant(), p.Version)))
      .ToHashSet(StringComparer.OrdinalIgnoreCase);
   var bundledById = bundled.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
   var usedBundled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

   foreach (var dll in buildHostDlls)
   {
      var name = Path.GetFileNameWithoutExtension(dll);
      if (scannedDllNames.Contains(Path.GetFileName(dll)))
         continue;

      if (!bundledById.TryGetValue(name, out var entry))
      {
         errors.Add($"{Path.GetFileName(dll)} ships in the BuildHost but has no entry in bundled.json.");
         continue;
      }

      usedBundled.Add(name);
      var productVersion = FileVersionInfo.GetVersionInfo(dll).ProductVersion?.Split('+')[0];
      if (productVersion != entry.Version)
         errors.Add($"bundled.json lists {name} {entry.Version}, but the BuildHost ships {productVersion}.");
   }

   errors.AddRange(bundled.Where(p => !usedBundled.Contains(p.Id))
      .Select(p => $"bundled.json lists {p.Id}, which the BuildHost no longer ships."));
   return errors;
}

static IEnumerable<string> EnumerateDllNames(string packageDir) =>
   Directory.Exists(packageDir)
      ? Directory.EnumerateFiles(packageDir, "*.dll", SearchOption.AllDirectories).Select(Path.GetFileName).OfType<string>()
      : [];

static string GetPackageFolder(string assetsFile)
{
   using var assets = JsonDocument.Parse(File.ReadAllText(assetsFile));
   return assets.RootElement.GetProperty("packageFolders").EnumerateObject().First().Name;
}

static List<Package> ReadPackages(string path)
{
   using var document = JsonDocument.Parse(File.ReadAllText(path));
   var isBundled = Path.GetFileName(path) == "bundled.json";
   return document.RootElement.EnumerateArray()
      .Select(e => new Package(
         Required(e, "PackageId"),
         Required(e, "PackageVersion"),
         Required(e, "License"),
         Optional(e, "PackageProjectUrl"),
         Optional(e, "Copyright"),
         isBundled))
      .ToList();
}

static string Required(JsonElement element, string name) =>
   Optional(element, name) ?? throw new InvalidOperationException($"Package entry has no {name}: {element}");

static string? Optional(JsonElement element, string name) =>
   element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

static int Run(string fileName, params string[] arguments)
{
   var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false };
   foreach (var argument in arguments)
      startInfo.ArgumentList.Add(argument);

   using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}");
   process.WaitForExit();
   return process.ExitCode;
}

static int Fail(string message)
{
   Console.Error.WriteLine(message);
   return 1;
}

record Package(string Id, string Version, string License, string? ProjectUrl, string? Copyright, bool IsBundled);
