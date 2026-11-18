using System.Security;

namespace DotLens.Infrastructure;

/// <summary>
/// Utility methods for path formatting and normalization.
/// </summary>
public static class PathUtils
{
   /// <summary>
   /// Platform-aware path comparison: case-insensitive on Windows, case-sensitive on Linux/macOS.
   /// Matches file system behavior on each platform.
   /// </summary>
   public static StringComparison PathComparison => OperatingSystem.IsWindows()
       ? StringComparison.OrdinalIgnoreCase
       : StringComparison.Ordinal;

   /// <summary>
   /// The string comparer for <see cref="PathComparison"/>.
   /// </summary>
   public static StringComparer PathComparer => OperatingSystem.IsWindows()
       ? StringComparer.OrdinalIgnoreCase
       : StringComparer.Ordinal;

   /// <summary>
   /// Returns whether the full path is the directory or lies under it.
   /// </summary>
   public static bool IsSameOrUnder(string path, string directory)
   {
      var trimmedPath = Path.TrimEndingDirectorySeparator(path);
      var trimmedDirectory = Path.TrimEndingDirectorySeparator(directory);
      return string.Equals(trimmedPath, trimmedDirectory, PathComparison)
         || trimmedPath.StartsWith(trimmedDirectory + Path.DirectorySeparatorChar, PathComparison);
   }

   /// <summary>
   /// Normalizes path separators to forward slashes for cross-platform consistency.
   /// </summary>
   public static string NormalizePath(string path)
   {
      return path.Replace('\\', '/');
   }

   /// <summary>
   /// Formats a file path as relative or absolute based on configuration.
   /// Default is relative paths to save tokens. Set SHARPLENS_ABSOLUTE_PATHS=true for absolute.
   /// Normalizes path separators to forward slashes for cross-platform consistency.
   /// </summary>
   public static string FormatPath(string? absolutePath, string? solutionFilePath, bool useAbsolutePaths)
   {
      if (string.IsNullOrEmpty(absolutePath))
         return absolutePath ?? "";

      if (useAbsolutePaths || string.IsNullOrEmpty(solutionFilePath))
         return NormalizePath(absolutePath);

      var solutionDir = Path.GetDirectoryName(solutionFilePath);
      if (string.IsNullOrEmpty(solutionDir))
         return NormalizePath(absolutePath);

      try
      {
         var relativePath = Path.GetRelativePath(solutionDir, absolutePath);
         // If path escapes solution directory (contains ..), use absolute path instead
         // This prevents information disclosure about paths outside the solution
         if (relativePath.StartsWith("..", StringComparison.Ordinal))
            return NormalizePath(absolutePath);
         return NormalizePath(relativePath);
      }
      catch
      {
         // Expected: GetRelativePath throws on Windows for paths on different drives.
         // No logging: this is a best-effort display conversion with sensible fallback.
         return NormalizePath(absolutePath);
      }
   }



   /// <summary>
   /// Validates that a path is within the specified base directory.
   /// Prevents path traversal attacks (e.g., ../../etc/passwd).
   /// Also rejects UNC paths and paths containing junctions/symlinks on Windows.
   /// </summary>
   /// <param name="path">The path to validate (absolute or relative).</param>
   /// <param name="baseDirectory">The base directory that the path must be within.</param>
   /// <returns>The resolved absolute path if valid.</returns>
   /// <exception cref="SecurityException">Thrown if the path is outside the base directory or uses disallowed path types.</exception>
   public static string ValidatePathWithinDirectory(string path, string baseDirectory)
   {
      ArgumentNullException.ThrowIfNullOrEmpty(path);
      ArgumentNullException.ThrowIfNullOrEmpty(baseDirectory);

      // Reject UNC paths (\\server\share or //server/share)
      if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
         throw new SecurityException("UNC paths are not allowed");

      var resolvedPath = Path.GetFullPath(path);
      var resolvedBase = Path.GetFullPath(baseDirectory);

      // Ensure base directory ends with separator for accurate StartsWith check
      if (!resolvedBase.EndsWith(Path.DirectorySeparatorChar))
         resolvedBase += Path.DirectorySeparatorChar;

      if (!resolvedPath.StartsWith(resolvedBase, PathComparison))
      {
         throw new SecurityException(
             $"Path traversal detected: '{path}' resolves outside the allowed directory '{baseDirectory}'");
      }

      // On Windows, check for junctions/symlinks that could bypass path validation
      if (OperatingSystem.IsWindows())
         RejectPathWithReparsePoints(resolvedPath, resolvedBase);

      return resolvedPath;
   }

   /// <summary>
   /// Checks if any existing directory component in the path is a reparse point (junction/symlink).
   /// This prevents bypass attacks where a junction inside the solution points outside.
   /// </summary>
   private static void RejectPathWithReparsePoints(string resolvedPath, string resolvedBase)
   {
      // Check each existing directory component between base and target
      var directory = Path.GetDirectoryName(resolvedPath);

      while (!string.IsNullOrEmpty(directory) &&
             directory.Length >= resolvedBase.Length &&
             !string.Equals(directory, resolvedBase.TrimEnd(Path.DirectorySeparatorChar), PathComparison))
      {
         if (Directory.Exists(directory))
         {
            var dirInfo = new DirectoryInfo(directory);
            if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
               throw new SecurityException(
                   $"Path contains a junction or symbolic link at '{directory}', which is not allowed for security reasons");
            }
         }

         directory = Path.GetDirectoryName(directory);
      }
   }

   /// <summary>
   /// Resolves a path relative to a base directory with path traversal protection.
   /// Also rejects UNC paths and paths containing junctions/symlinks on Windows.
   /// </summary>
   /// <param name="path">The path to resolve (absolute or relative).</param>
   /// <param name="baseDirectory">The base directory for relative paths and security validation.</param>
   /// <returns>The resolved absolute path if valid.</returns>
   /// <exception cref="SecurityException">Thrown if the resolved path is outside the base directory or uses disallowed path types.</exception>
   public static string ResolvePathSafely(string path, string baseDirectory)
   {
      ArgumentNullException.ThrowIfNullOrEmpty(path);
      ArgumentNullException.ThrowIfNullOrEmpty(baseDirectory);

      // Early rejection of UNC paths before any path operations
      if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
         throw new SecurityException("UNC paths are not allowed");

      string fullPath;
      if (Path.IsPathRooted(path))
         fullPath = Path.GetFullPath(path);
      else
         fullPath = Path.GetFullPath(Path.Combine(baseDirectory, path));

      return ValidatePathWithinDirectory(fullPath, baseDirectory);
   }

   /// <summary>
   /// Writes content to a file after validating the path is within the solution directory.
   /// Use this for all file writes in tool implementations to prevent arbitrary file writes.
   /// </summary>
   /// <param name="filePath">The file path to write to.</param>
   /// <param name="content">The content to write.</param>
   /// <param name="solutionDirectory">The solution directory that the file must be within.</param>
   /// <param name="cancellationToken">Cancellation token.</param>
   /// <exception cref="SecurityException">Thrown if the file path is outside the solution directory.</exception>
   public static async Task WriteFileSafelyAsync(string filePath, string content, string solutionDirectory, CancellationToken cancellationToken = default)
   {
      var validatedPath = ValidatePathWithinDirectory(filePath, solutionDirectory);
      var parentDir = Path.GetDirectoryName(validatedPath);
      if (!string.IsNullOrEmpty(parentDir))
         Directory.CreateDirectory(parentDir);
      await File.WriteAllTextAsync(validatedPath, content, cancellationToken);
   }
}
