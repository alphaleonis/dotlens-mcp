namespace DotLens.Infrastructure;

/// <summary>
/// The contents of a set of files before a write, which <see cref="Restore"/> writes back.
/// </summary>
internal sealed class FileSnapshot
{
   // A null value means that the file did not exist.
   private readonly Dictionary<string, byte[]?> _files;
   private readonly List<string> _missingDirectories;

   private FileSnapshot(Dictionary<string, byte[]?> files, List<string> missingDirectories)
   {
      _files = files;
      _missingDirectories = missingDirectories;
   }

   /// <summary>
   /// Reads each file, and notes each file and parent directory that does not exist.
   /// </summary>
   /// <exception cref="IOException">A file exists but could not be read.</exception>
   /// <exception cref="UnauthorizedAccessException">A file exists but could not be read.</exception>
   public static FileSnapshot Take(IEnumerable<string> filePaths)
   {
      var files = new Dictionary<string, byte[]?>(PathUtils.PathComparer);
      var missingDirectories = new List<string>();
      foreach (var filePath in filePaths.Select(Path.GetFullPath))
      {
         if (files.ContainsKey(filePath))
            continue;

         if (File.Exists(filePath))
         {
            files[filePath] = File.ReadAllBytes(filePath);
            continue;
         }

         files[filePath] = null;
         var directory = Path.GetDirectoryName(filePath);
         while (directory is not null && !Directory.Exists(directory) && !missingDirectories.Contains(directory, PathUtils.PathComparer))
         {
            missingDirectories.Add(directory);
            directory = Path.GetDirectoryName(directory);
         }
      }

      return new FileSnapshot(files, missingDirectories);
   }

   /// <summary>
   /// Writes back each file that differs from the snapshot, and deletes each file and empty directory that did not exist.
   /// </summary>
   /// <remarks>
   /// Every restore runs even when an earlier one fails. A file that still holds its snapshot is not written.
   /// </remarks>
   public RestoreResult Restore(Func<string?, string> formatPath)
   {
      var restored = 0;
      var failures = new List<string>();
      foreach (var (filePath, bytes) in _files)
      {
         Try(filePath, () =>
         {
            if (bytes is null && File.Exists(filePath))
            {
               File.Delete(filePath);
               restored++;
            }
            else if (bytes is not null && !HasBytes(filePath, bytes))
            {
               File.WriteAllBytes(filePath, bytes);
               restored++;
            }
         });
      }

      foreach (var directory in _missingDirectories.OrderByDescending(d => d.Length))
      {
         Try(directory, () =>
         {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
               Directory.Delete(directory);
               restored++;
            }
         });
      }

      return new RestoreResult(restored, failures);

      void Try(string path, Action restore)
      {
         try
         {
            restore();
         }
         catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
         {
            failures.Add($"{formatPath(path)} ({ex.Message})");
         }
      }
   }

   /// <summary>
   /// What <see cref="Restore"/> did.
   /// </summary>
   /// <param name="Restored">The number of files and directories that differed from the snapshot and were restored.</param>
   /// <param name="Failures">
   /// Each file or directory that could not be restored, as the caller's formatPath formats it, with the reason.
   /// </param>
   public sealed record RestoreResult(int Restored, List<string> Failures);

   private static bool HasBytes(string filePath, byte[] bytes)
   {
      try
      {
         return File.Exists(filePath) && File.ReadAllBytes(filePath).AsSpan().SequenceEqual(bytes);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
         return false;
      }
   }
}
