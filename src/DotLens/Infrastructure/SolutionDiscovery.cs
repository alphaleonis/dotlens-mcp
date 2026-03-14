namespace DotLens.Infrastructure;

/// <summary>
/// Result of solution auto-discovery.
/// </summary>
public abstract record SolutionDiscoveryResult
{
   /// <summary>A single solution was found and should be loaded.</summary>
   public sealed record Load(string SolutionPath) : SolutionDiscoveryResult;

   /// <summary>Multiple solutions found; user must call load_solution to choose.</summary>
   public sealed record MultipleSolutions(string[] Solutions) : SolutionDiscoveryResult;

   /// <summary>No solutions found in the searched directory.</summary>
   public sealed record None : SolutionDiscoveryResult;

   /// <summary>Auto-load is disabled via configuration.</summary>
   public sealed record Disabled : SolutionDiscoveryResult;

   /// <summary>The configured SolutionPath does not exist as a file or directory.</summary>
   public sealed record ConfiguredPathNotFound(string Path) : SolutionDiscoveryResult;
}

/// <summary>
/// Discovers which solution to auto-load based on configuration and working directory.
/// </summary>
public static class SolutionDiscovery
{
   /// <summary>
   /// Determines which solution (if any) should be auto-loaded.
   /// </summary>
   /// <param name="solutionPath">Configured solution path (file or directory), or null.</param>
   /// <param name="autoLoadSolution">Whether auto-discovery from CWD is enabled.</param>
   /// <param name="workingDirectory">The current working directory to search.</param>
   public static SolutionDiscoveryResult Discover(string? solutionPath, bool autoLoadSolution, string workingDirectory)
   {
      // When SolutionPath is explicitly configured, use it
      if (!string.IsNullOrEmpty(solutionPath))
         return ResolveConfiguredPath(solutionPath, workingDirectory);

      // No explicit path — auto-discover from CWD if enabled
      if (!autoLoadSolution)
         return new SolutionDiscoveryResult.Disabled();

      return DiscoverFromDirectory(workingDirectory);
   }

   private static SolutionDiscoveryResult ResolveConfiguredPath(string solutionPath, string workingDirectory)
   {
      var resolved = Path.IsPathRooted(solutionPath)
         ? solutionPath
         : Path.GetFullPath(solutionPath, workingDirectory);

      if (File.Exists(resolved))
         return new SolutionDiscoveryResult.Load(resolved);

      if (Directory.Exists(resolved))
         return DiscoverFromDirectory(resolved);

      return new SolutionDiscoveryResult.ConfiguredPathNotFound(resolved);
   }

   private static SolutionDiscoveryResult DiscoverFromDirectory(string directory)
   {
      var solutions = FindSolutions(directory);
      return solutions.Length switch
      {
         1 => new SolutionDiscoveryResult.Load(solutions[0]),
         > 1 => new SolutionDiscoveryResult.MultipleSolutions(solutions),
         _ => new SolutionDiscoveryResult.None()
      };
   }

   internal static string[] FindSolutions(string directory)
   {
      return Directory.GetFiles(directory, "*.sln", SearchOption.TopDirectoryOnly)
         .Concat(Directory.GetFiles(directory, "*.slnx", SearchOption.TopDirectoryOnly))
         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
         .ToArray();
   }
}
