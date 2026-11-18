using System.Text;
using System.Text.Json;

namespace DotLens.Infrastructure;

/// <summary>
/// Placeholder files at the paths of added documents that do not exist yet, with the directories created for them, so
/// that MSBuild's evaluation includes them.
/// </summary>
/// <remarks>
/// Before it creates anything, <see cref="Create"/> writes a journal to the project's obj directory that lists the
/// placeholders and directories, and it keeps the journal open with an exclusive lock until they are deleted. If the
/// process ends first, the operating system releases the lock, and <see cref="Sweep"/> deletes what the journal lists.
/// </remarks>
internal sealed class EvaluationPlaceholders : IDisposable
{
   /// <summary>
   /// The text of every placeholder, which tells a placeholder left behind from a file a user wrote.
   /// </summary>
   internal const string Content = "// DotLens created this file while it evaluated the project. Delete it if it remains.\n";

   private const string JournalPattern = "DotLens.placeholders.*.json";

   private static readonly byte[] ContentBytes = Encoding.UTF8.GetBytes(Content);

   private readonly List<string> _files = [];
   private readonly List<string> _directories;
   private readonly List<string> _journalDirectories;
   private readonly Func<string, ToolException> _refuse;
   private FileStream? _journal;
   private string? _journalPath;

   private EvaluationPlaceholders(
      List<string> directories, List<string> journalDirectories, Func<string, ToolException> refuse)
   {
      _directories = directories;
      _journalDirectories = journalDirectories;
      _refuse = refuse;
   }

   /// <summary>
   /// Creates a placeholder at each path that does not exist yet.
   /// </summary>
   /// <param name="projectDirectory">The directory of the evaluated project, whose obj directory holds the journal.</param>
   /// <param name="formatPath">Formats a path for the refusal's message.</param>
   /// <exception cref="ToolException">
   /// A placeholder or the journal could not be created. Nothing is left on disk then.
   /// </exception>
   public static EvaluationPlaceholders Create(
      string projectDirectory,
      IEnumerable<string> filePaths,
      Func<string, ToolException> refuse,
      Func<string?, string> formatPath)
   {
      var files = filePaths.Where(p => !File.Exists(p)).Distinct(PathUtils.PathComparer).ToList();
      var journalDirectories = new List<string>();
      var directories = new List<string>();
      if (files.Count > 0)
      {
         AddMissing(Path.Combine(projectDirectory, "obj"), journalDirectories);
         foreach (var file in files)
            AddMissing(Path.GetDirectoryName(file), directories);
      }

      var placeholders = new EvaluationPlaceholders(directories, journalDirectories, refuse);
      if (files.Count == 0)
         return placeholders;

      var objDirectory = Path.Combine(projectDirectory, "obj");
      try
      {
         foreach (var directory in journalDirectories)
            Directory.CreateDirectory(directory);
         var journalPath = Path.Combine(objDirectory, $"DotLens.placeholders.{Guid.NewGuid():N}.json");
         placeholders._journalPath = journalPath;
         placeholders._journal = new FileStream(journalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
         WriteJournal(placeholders._journal, files, [.. directories, .. journalDirectories]);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
         placeholders.Dispose();
         throw refuse($"the journal of its placeholder files could not be written to {formatPath(objDirectory)} ({ex.Message})");
      }

      try
      {
         foreach (var directory in directories)
            Directory.CreateDirectory(directory);
         foreach (var file in files)
         {
            using (var stream = new FileStream(file, FileMode.CreateNew))
            {
               placeholders._files.Add(file);
               stream.Write(ContentBytes);
            }
         }
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
         placeholders.Dispose();
         throw refuse($"a placeholder file could not be created to evaluate the project: {ex.Message}");
      }

      return placeholders;
   }

   /// <summary>
   /// Deletes the placeholders, the directories created for them, and the journal.
   /// </summary>
   /// <exception cref="ToolException">A placeholder or a directory could not be deleted.</exception>
   public void Delete()
   {
      try
      {
         foreach (var file in _files)
            File.Delete(file);
         _files.Clear();
         foreach (var directory in Enumerable.Reverse(_directories))
         {
            if (Directory.Exists(directory))
               Directory.Delete(directory);
         }

         _directories.Clear();
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
         throw _refuse($"the placeholder file created to evaluate the project could not be deleted: {ex.Message}");
      }

      DeleteJournal();
   }

   /// <summary>
   /// Deletes what is left when an evaluation fails, without hiding its exception.
   /// </summary>
   /// <remarks>
   /// The journal stays when a placeholder could not be deleted, so a later <see cref="Sweep"/> deletes it.
   /// </remarks>
   public void Dispose()
   {
      var deleted = true;
      foreach (var file in _files)
         deleted &= TryDelete(() => File.Delete(file));
      foreach (var directory in Enumerable.Reverse(_directories))
         deleted &= TryDelete(() => DeleteIfEmpty(directory));
      _files.Clear();
      _directories.Clear();

      if (deleted)
         DeleteJournal();
      else
         _journal?.Dispose();
   }

   /// <summary>
   /// Deletes the placeholders that an evaluation left in the project when its process ended before it deleted them.
   /// </summary>
   /// <remarks>
   /// It skips a journal that another evaluation holds open. It deletes only a listed file that still holds
   /// <see cref="Content"/>, and only a listed directory that is empty. A repository can ship a journal, so it ignores
   /// a listed path outside the project and solution directories.
   /// </remarks>
   /// <param name="solutionDirectory">The directory of the loaded solution, which can hold a project's added files.</param>
   /// <returns>The full paths of the placeholders it deleted.</returns>
   public static List<string> Sweep(string projectDirectory, string solutionDirectory)
   {
      bool MaySweep(string path) => Path.IsPathFullyQualified(path)
         && (PathUtils.IsSameOrUnder(Path.GetFullPath(path), projectDirectory)
            || PathUtils.IsSameOrUnder(Path.GetFullPath(path), solutionDirectory));

      var deleted = new List<string>();
      var objDirectory = Path.Combine(projectDirectory, "obj");
      if (!Directory.Exists(objDirectory))
         return deleted;

      foreach (var journalPath in Directory.EnumerateFiles(objDirectory, JournalPattern))
      {
         Journal journal;
         try
         {
            using var stream = new FileStream(journalPath, FileMode.Open, FileAccess.Read, FileShare.None);
            journal = ReadJournal(stream);
         }
         catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
         {
            continue;
         }

         var swept = true;
         foreach (var file in journal.Files.Where(MaySweep))
         {
            if (!IsPlaceholder(file))
               continue;
            if (TryDelete(() => File.Delete(file)))
               deleted.Add(file);
            else
               swept = false;
         }

         if (!swept || !TryDelete(() => File.Delete(journalPath)))
            continue;
         foreach (var directory in journal.Directories.Where(MaySweep).OrderByDescending(d => d.Length))
            TryDelete(() => DeleteIfEmpty(directory));
      }

      return deleted;
   }

   private static bool IsPlaceholder(string file)
   {
      try
      {
         return new FileInfo(file) is { Exists: true } info && info.Length == ContentBytes.Length
            && File.ReadAllBytes(file).AsSpan().SequenceEqual(ContentBytes);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
         return false;
      }
   }

   private void DeleteJournal()
   {
      _journal?.Dispose();
      _journal = null;
      if (_journalPath is not { } journalPath)
         return;

      TryDelete(() => File.Delete(journalPath));
      _journalPath = null;
      foreach (var directory in Enumerable.Reverse(_journalDirectories))
         TryDelete(() => DeleteIfEmpty(directory));
   }

   private static void AddMissing(string? directory, List<string> missing)
   {
      if (directory is null || Directory.Exists(directory) || missing.Contains(directory, PathUtils.PathComparer))
         return;
      AddMissing(Path.GetDirectoryName(directory), missing);
      missing.Add(directory);
   }

   private static void DeleteIfEmpty(string directory)
   {
      if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
         Directory.Delete(directory);
   }

   private static bool TryDelete(Action delete)
   {
      try
      {
         delete();
         return true;
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
         return false;
      }
   }

   private static void WriteJournal(FileStream stream, List<string> files, List<string> directories)
   {
      using (var writer = new Utf8JsonWriter(stream))
      {
         writer.WriteStartObject();
         writer.WriteStartArray("files");
         foreach (var file in files)
            writer.WriteStringValue(file);
         writer.WriteEndArray();
         writer.WriteStartArray("directories");
         foreach (var directory in directories)
            writer.WriteStringValue(directory);
         writer.WriteEndArray();
         writer.WriteEndObject();
      }

      stream.Flush(flushToDisk: true);
   }

   private static Journal ReadJournal(FileStream stream)
   {
      try
      {
         using var document = JsonDocument.Parse(stream);
         static List<string> Paths(JsonElement root, string name) =>
            root.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
               ? [.. array.EnumerateArray().Select(e => e.GetString()).OfType<string>()]
               : [];
         return new Journal(Paths(document.RootElement, "files"), Paths(document.RootElement, "directories"));
      }
      catch (JsonException)
      {
         // The journal is complete on disk before any placeholder exists, so a journal that does not parse lists none.
         return new Journal([], []);
      }
   }

   private sealed record Journal(List<string> Files, List<string> Directories);
}
