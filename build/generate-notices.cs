using System.Text;
using System.Text.Json;

var jsonFile = Path.Combine(Environment.CurrentDirectory, ".notices-tmp.json");
var outFile = Path.Combine(Environment.CurrentDirectory, "THIRD-PARTY-NOTICES.txt");

if (!File.Exists(jsonFile))
{
   Console.Error.WriteLine($"Expected {jsonFile} - run 'dotnet dotnet-project-licenses' first");
   return 1;
}

var json = File.ReadAllText(jsonFile, Encoding.UTF8);
var packages = JsonDocument.Parse(json).RootElement;

var sb = new StringBuilder();
sb.AppendLine("THIRD-PARTY SOFTWARE NOTICES AND INFORMATION");
sb.AppendLine("=============================================");
sb.AppendLine();
sb.AppendLine("DotLens incorporates third-party software components listed below.");
sb.AppendLine("The original copyright notices and license terms are provided for");
sb.AppendLine("informational purposes.");
sb.AppendLine();

var sorted = packages.EnumerateArray()
   .OrderBy(p => p.GetProperty("PackageName").GetString())
   .ToList();

foreach (var pkg in sorted)
{
   sb.AppendLine("-------------------------------------------------------------");
   sb.AppendLine($"{pkg.GetProperty("PackageName").GetString()} {pkg.GetProperty("PackageVersion").GetString()}");
   sb.AppendLine($"License: {pkg.GetProperty("LicenseType").GetString()}");

   if (pkg.TryGetProperty("LicenseUrl", out var licenseUrl) && licenseUrl.ValueKind == JsonValueKind.String)
      sb.AppendLine($"License URL: {licenseUrl.GetString()}");

   if (pkg.TryGetProperty("PackageUrl", out var packageUrl) && packageUrl.ValueKind == JsonValueKind.String)
      sb.AppendLine($"Project URL: {packageUrl.GetString()}");

   if (pkg.TryGetProperty("Copyright", out var copyright) && copyright.ValueKind == JsonValueKind.String)
   {
      var copyrightText = copyright.GetString();
      if (!string.IsNullOrWhiteSpace(copyrightText))
         sb.AppendLine($"Copyright: {copyrightText}");
   }

   sb.AppendLine();
}

File.WriteAllText(outFile, sb.ToString().TrimEnd() + "\n", new UTF8Encoding(false));
File.Delete(jsonFile);
Console.WriteLine($"Generated {outFile} with {sorted.Count} packages");
return 0;
