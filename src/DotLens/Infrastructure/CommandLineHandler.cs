using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace DotLens.Infrastructure;

/// <summary>
/// Action to take after processing command-line arguments.
/// </summary>
public enum StartupAction
{
   RunServer,
   RunWorker,
   Exit
}

/// <summary>
/// Processes command-line arguments for DotLens, providing help text and
/// warnings for unrecognized arguments.
/// </summary>
public static class CommandLineHandler
{
   private static readonly HashSet<string> KnownFlags = new(StringComparer.OrdinalIgnoreCase)
   {
      "--help", "-h",
      "--version", "-v",
      "--worker"
   };

   /// <summary>
   /// Processes command-line arguments and determines what action to take.
   /// Writes help/version to stdout and warnings to stderr as needed.
   /// </summary>
   public static StartupAction Process(string[] args, TextWriter? stdout = null, TextWriter? stderr = null)
   {
      stdout ??= Console.Out;
      stderr ??= Console.Error;

      if (args.Contains("--help", StringComparer.OrdinalIgnoreCase)
         || args.Contains("-h", StringComparer.OrdinalIgnoreCase))
      {
         stdout.Write(GetHelpText());
         return StartupAction.Exit;
      }

      if (args.Contains("--version", StringComparer.OrdinalIgnoreCase)
         || args.Contains("-v", StringComparer.OrdinalIgnoreCase))
      {
         stdout.WriteLine(Program.GetVersion());
         return StartupAction.Exit;
      }

      WarnUnrecognizedArguments(args, stderr);

      if (args.Contains("--worker", StringComparer.OrdinalIgnoreCase))
         return StartupAction.RunWorker;

      return StartupAction.RunServer;
   }

   /// <summary>
   /// Checks for unrecognized --flag and --key=value arguments and writes
   /// warnings to stderr. Bare positional arguments are ignored.
   /// </summary>
   internal static void WarnUnrecognizedArguments(string[] args, TextWriter stderr)
   {
      foreach (var arg in args)
      {
         // Skip positional args and bare '-' / '--' (end-of-options marker)
         if (!arg.StartsWith('-') || arg is "-" or "--")
            continue;

         // Extract the key portion (before '=' if present)
         var keyPart = arg;
         var eqIndex = arg.IndexOf('=');
         if (eqIndex >= 0)
            keyPart = arg[..eqIndex];

         if (KnownFlags.Contains(keyPart))
            continue;

         // Check against DotLensOptions property names (--PropertyName)
         if (keyPart.StartsWith("--", StringComparison.Ordinal) && OptionNames.Value.Contains(keyPart[2..]))
            continue;

         stderr.WriteLine($"warning: unrecognized argument '{arg}'");
      }
   }

   private static readonly Lazy<HashSet<string>> OptionNames = new(() =>
      new HashSet<string>(
         typeof(DotLensOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name),
         StringComparer.OrdinalIgnoreCase));

   /// <summary>
   /// Builds the full help text including flags, options, and examples.
   /// </summary>
   internal static string GetHelpText()
   {
      var sb = new StringBuilder();

      sb.AppendLine(CultureInfo.InvariantCulture, $"DotLens v{Program.GetVersion()}");
      sb.AppendLine("MCP server for .NET/C# semantic code analysis using Roslyn.");
      sb.AppendLine();
      sb.AppendLine("USAGE:");
      sb.AppendLine("  dotlens [options]");
      sb.AppendLine();
      sb.AppendLine("FLAGS:");
      sb.AppendLine("  -h, --help       Show this help and exit");
      sb.AppendLine("  -v, --version    Show version and exit");
      sb.AppendLine("      --worker     Run in worker mode (internal)");
      sb.AppendLine();
      sb.AppendLine("OPTIONS:");
      sb.AppendLine("  Pass as --Key=Value or set DOTLENS_Key environment variable.");
      sb.AppendLine();

      AppendOptionsTable(sb);

      sb.AppendLine();
      sb.AppendLine("EXAMPLES:");
      sb.AppendLine("  dotlens --SolutionPath=C:\\My.sln     Auto-load a solution");
      sb.AppendLine("  dotlens --LogLevel=Debug             Enable debug logging");

      return sb.ToString();
   }

   private static void AppendOptionsTable(StringBuilder sb)
   {
      var properties = typeof(DotLensOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance);
      var defaults = new DotLensOptions();
      var rows = new List<(string Name, string Default, string Description)>();

      foreach (var prop in properties)
      {
         var description = prop.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
         var defaultValue = GetDefaultValue(prop, defaults);
         rows.Add(($"--{prop.Name}", defaultValue, description));
      }

      // Calculate column widths
      var nameWidth = rows.Max(r => r.Name.Length);
      var defaultWidth = rows.Max(r => r.Default.Length);

      // Header
      sb.AppendLine(CultureInfo.InvariantCulture, $"  {"Name".PadRight(nameWidth)}   {"Default".PadRight(defaultWidth)}   Description");
      sb.AppendLine(CultureInfo.InvariantCulture, $"  {new string('-', nameWidth)}   {new string('-', defaultWidth)}   -----------");

      foreach (var (name, defaultVal, description) in rows)
      {
         sb.AppendLine(CultureInfo.InvariantCulture, $"  {name.PadRight(nameWidth)}   {defaultVal.PadRight(defaultWidth)}   {description}");
      }
   }

   private static string GetDefaultValue(PropertyInfo prop, DotLensOptions defaults)
   {
      var value = prop.GetValue(defaults);

      if (value is null)
         return "(none)";

      if (value is bool b)
         return b ? "true" : "false";

      return value.ToString() ?? "(none)";
   }
}
