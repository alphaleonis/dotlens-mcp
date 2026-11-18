using System.Xml;
using Microsoft.CodeAnalysis;

namespace DotLens.Infrastructure;

/// <summary>
/// Writes the document additions and removals of a change to the project files and the disk.
/// </summary>
/// <remarks>
/// <see cref="Create"/> decides every write before the workspace applies the change, and refuses a change that the
/// project files cannot express. MSBuild's evaluation of each workspace project, which is one target framework of its
/// project file, decides how a file is included.
///
/// A removal takes the file out of the Compile item that the project file lists it in, and deletes the file when no
/// other project still compiles it. A file that the SDK's default glob includes is deleted with no edit to the
/// project file.
///
/// The workspace writes an added file and lists it in the project file. When the project already includes the file,
/// as the SDK's default glob does, <see cref="Apply"/> takes that item out again, because MSBuild fails the build on
/// a duplicate Compile item.
/// </remarks>
internal sealed class DocumentChangePlan
{
   private const string RemovalHint =
      "Remove the document from every project that compiles it, or edit the project file and call reload_solution.";

   private const string AdditionHint = "Write the files and call reload_solution, or add them to the project file first.";

   private readonly Dictionary<string, ProjectFileEdit> _projectFileEdits;
   private readonly List<string> _filesToDelete;
   private readonly Func<string?, string> _formatPath;

   private DocumentChangePlan(
      List<DocumentId> removedDocumentIds,
      Dictionary<string, ProjectFileEdit> projectFileEdits,
      List<string> filesToDelete,
      Func<string?, string> formatPath)
   {
      RemovedDocumentIds = removedDocumentIds;
      _projectFileEdits = projectFileEdits;
      _filesToDelete = filesToDelete;
      _formatPath = formatPath;
   }

   /// <summary>
   /// The documents that the change removes. The workspace must not apply their removal.
   /// </summary>
   public IReadOnlyList<DocumentId> RemovedDocumentIds { get; }

   /// <summary>
   /// Plans the document changes that <paramref name="newSolution"/> makes to <paramref name="oldSolution"/>.
   /// </summary>
   /// <param name="evaluationBudget">The time that all MSBuild evaluations of the plan share.</param>
   /// <exception cref="ToolException">A change cannot be written to the project files. Nothing was written then.</exception>
   public static DocumentChangePlan Create(
      Solution oldSolution, Solution newSolution, Func<string?, string> formatPath, TimeSpan evaluationBudget)
   {
      var evaluatedItems = new EvaluatedCompileItems(evaluationBudget);
      var removedDocumentIds = new List<DocumentId>();
      var projectFileEdits = new Dictionary<string, ProjectFileEdit>(PathUtils.PathComparer);
      var filesToDelete = new List<string>();

      ProjectFileEdit EditOf(string projectPath, Func<string, ToolException> refuse)
      {
         if (projectFileEdits.TryGetValue(projectPath, out var edit))
            return edit;

         try
         {
            edit = new ProjectFileEdit(projectPath, ProjectFileCompileItems.Read(projectPath));
         }
         catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
         {
            throw refuse($"the project file could not be read: {ex.Message}");
         }

         projectFileEdits[projectPath] = edit;
         return edit;
      }

      var projectChanges = newSolution.GetChanges(oldSolution).GetProjectChanges().ToList();
      foreach (var documentId in projectChanges.SelectMany(pc => pc.GetRemovedDocuments()))
      {
         removedDocumentIds.Add(documentId);
         var document = oldSolution.GetDocument(documentId)
            ?? throw new InvalidOperationException($"Removed document {documentId} is not in the old solution");
         if (document.FilePath is not { } documentPath)
            continue;

         var filePath = Path.GetFullPath(documentPath);
         var project = document.Project;
         ToolException Refuse(string reason) => new(
            ErrorCodes.ApplyChangesFailed,
            $"Cannot remove {formatPath(filePath)} from project {project.Name}: {reason}.",
            RemovalHint);

         if (project.FilePath is not { } projectPath)
            throw Refuse("the project has no project file");

         var remainingProjects = newSolution.GetDocumentIdsWithFilePath(documentPath)
            .Select(id => newSolution.GetProject(id.ProjectId))
            .OfType<Project>()
            .ToList();
         if (remainingProjects.FirstOrDefault(p => PathUtils.PathComparer.Equals(p.FilePath, projectPath)) is { } sameProjectFile)
         {
            throw Refuse(sameProjectFile.Id == project.Id
               ? "the change adds it to the project again"
               : $"project {sameProjectFile.Name} has the same project file and still compiles it");
         }

         var edit = EditOf(projectPath, Refuse);
         var itemsOfFile = Evaluate(evaluatedItems, project, reuse: true, Refuse)
            .Where(item => PathUtils.PathComparer.Equals(item.FullPath, filePath))
            .ToList();

         if (itemsOfFile.Count == 0)
            throw Refuse("MSBuild's evaluation of the project does not include it");
         if (itemsOfFile.Any(item => !item.IsFromSdkDefaultGlob && !PathUtils.PathComparer.Equals(item.DefiningProjectFullPath, projectPath)))
            throw Refuse("a file that the project imports includes it");

         // An item that the project file defines but does not list literally comes from a wildcard or a property.
         var definedByProjectFile = itemsOfFile.Any(item => PathUtils.PathComparer.Equals(item.DefiningProjectFullPath, projectPath));
         var listing = edit.Original.Find(filePath);
         bool includedByWildcard;
         switch (listing)
         {
            case CompileItemListing.Listed:
               edit.RemovedFiles.Add(filePath);
               includedByWildcard = itemsOfFile.Any(item => item.IsFromSdkDefaultGlob);
               break;
            case CompileItemListing.ListedWithOtherFiles:
               throw Refuse("the project file lists it in one Compile item with other files");
            case CompileItemListing.NotListed when definedByProjectFile && edit.Original.HasUnresolvedIncludes:
               throw Refuse("the project file has a Compile item that DotLens cannot evaluate");
            case CompileItemListing.NotListed:
               includedByWildcard = true;
               break;
            default:
               throw new InvalidOperationException($"Unexpected value: {listing}");
         }

         if (includedByWildcard && remainingProjects.Count > 0)
            throw Refuse($"the project includes it by a wildcard, and project {remainingProjects[0].Name} still compiles it");

         if (remainingProjects.Count == 0 && !filesToDelete.Contains(filePath, PathUtils.PathComparer))
            filesToDelete.Add(filePath);
      }

      var additionsByProjectPath = projectChanges
         .Where(pc => pc.NewProject.FilePath is not null && pc.GetAddedDocuments().Any())
         .GroupBy(pc => pc.NewProject.FilePath ?? "", PathUtils.PathComparer);
      foreach (var additions in additionsByProjectPath)
      {
         var projectName = additions.First().NewProject.Name;
         ToolException Refuse(string reason) => new(
            ErrorCodes.ApplyChangesFailed, $"Cannot add documents to project {projectName}: {reason}.", AdditionHint);

         var edit = EditOf(additions.Key, Refuse);
         PlanAdditions(newSolution, additions.Key, [.. additions], edit, evaluatedItems, formatPath, Refuse);
      }

      return new DocumentChangePlan(removedDocumentIds, projectFileEdits, filesToDelete, formatPath);
   }

   /// <summary>
   /// Writes the planned changes after the workspace applied the rest of the change.
   /// </summary>
   /// <remarks>
   /// Every write runs even when an earlier one fails, so the disk matches the applied solution as far as it can.
   /// </remarks>
   /// <exception cref="ToolException">A project file or a file could not be written. The others were written.</exception>
   public void Apply()
   {
      var failures = new List<string>();
      foreach (var edit in _projectFileEdits.Values)
         Try(edit.ProjectPath, edit.Write);
      foreach (var filePath in _filesToDelete)
         Try(filePath, () => File.Delete(filePath));

      if (failures.Count > 0)
      {
         throw new ToolException(
            ErrorCodes.ApplyChangesFailed,
            $"The changes were applied to the solution, but these files could not be updated: {string.Join("; ", failures)}",
            "Fix the files on disk to match the solution, or call reload_solution to read them again.");
      }

      void Try(string path, Action write)
      {
         try
         {
            write();
         }
         catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
         {
            failures.Add($"{_formatPath(path)} ({ex.Message})");
         }
      }
   }

   /// <summary>
   /// Decides which Compile items that the workspace adds for the documents are duplicates to take out again.
   /// </summary>
   /// <remarks>
   /// MSBuild includes only files that exist, so each added file that is missing gets an empty placeholder while the
   /// projects are evaluated. The placeholders are deleted before the method returns.
   /// </remarks>
   private static void PlanAdditions(
      Solution newSolution,
      string projectPath,
      List<ProjectChanges> additions,
      ProjectFileEdit edit,
      EvaluatedCompileItems evaluatedItems,
      Func<string?, string> formatPath,
      Func<string, ToolException> refuse)
   {
      var addersByFilePath = new Dictionary<string, List<ProjectId>>(PathUtils.PathComparer);
      foreach (var changes in additions)
      {
         foreach (var documentId in changes.GetAddedDocuments())
         {
            if (newSolution.GetDocument(documentId) is not { } document || WorkspaceAddedPath(document, projectPath) is not { } filePath)
               continue;
            if (edit.Original.Find(filePath) != CompileItemListing.NotListed)
               throw refuse($"the project file already lists {formatPath(filePath)}");

            if (!addersByFilePath.TryGetValue(filePath, out var adders))
               addersByFilePath[filePath] = adders = [];
            adders.Add(changes.ProjectId);
         }
      }

      if (addersByFilePath.Count == 0)
         return;

      var siblings = newSolution.Projects.Where(p => PathUtils.PathComparer.Equals(p.FilePath, projectPath)).ToList();
      var includedBefore = new Dictionary<ProjectId, HashSet<string>>();
      using (var placeholders = Placeholders.Create(addersByFilePath.Keys, refuse))
      {
         foreach (var sibling in siblings)
         {
            includedBefore[sibling.Id] = Evaluate(evaluatedItems, sibling, reuse: false, refuse)
               .Select(item => item.FullPath)
               .ToHashSet(PathUtils.PathComparer);
         }

         placeholders.Delete();
      }

      foreach (var (filePath, adders) in addersByFilePath)
      {
         if (adders.All(id => includedBefore[id].Contains(filePath)))
         {
            edit.DuplicateFiles.Add(filePath);
            continue;
         }

         // The workspace's item has no condition, so it reaches every target framework of the project file.
         if (siblings.FirstOrDefault(s => includedBefore[s.Id].Contains(filePath)) is { } includingSibling)
         {
            throw refuse($"{formatPath(filePath)} needs a Compile item, but project {includingSibling.Name} already includes it, "
               + "so its build would report NETSDK1022");
         }

         edit.ListedOnceFiles.Add(filePath);
      }
   }

   /// <summary>
   /// Returns the full path where MSBuildWorkspace writes an added document, or null for a language that it does not
   /// add files for. MSBuildWorkspace builds the path from the document's folders and name, and ignores its file path.
   /// </summary>
   /// <remarks>
   /// https://github.com/dotnet/roslyn/blob/1324e1d34a0490454837a22370e75bbaeecffb87/src/Workspaces/MSBuild/Core/MSBuild/MSBuildWorkspace.cs#L476-L495
   /// </remarks>
   internal static string? WorkspaceAddedPath(Document document, string projectPath)
   {
      var extension = document.Project.Language switch
      {
         LanguageNames.CSharp => ".cs",
         LanguageNames.VisualBasic => ".vb",
         _ => null,
      };
      if (extension is null || Path.GetDirectoryName(projectPath) is not { } projectDirectory)
         return null;

      var fileName = Path.ChangeExtension(document.Name, extension);
      var relativePath = document.Folders.Count > 0 ? Path.Combine(Path.Combine([.. document.Folders]), fileName) : fileName;
      return Path.GetFullPath(Path.Combine(projectDirectory, relativePath));
   }

   private static IReadOnlyList<EvaluatedCompileItem> Evaluate(
      EvaluatedCompileItems evaluatedItems, Project project, bool reuse, Func<string, ToolException> refuse)
   {
      try
      {
         return evaluatedItems.Get(project, reuse);
      }
      catch (ProjectEvaluationException ex)
      {
         var refusal = refuse("MSBuild could not evaluate the project");
         throw new ToolException(refusal.Code, refusal.Message, refusal.Hint, ex.Message);
      }
   }

   /// <summary>
   /// The planned edit of one project file: the items that removed documents leave, and the items that the workspace
   /// adds for files the project already includes.
   /// </summary>
   private sealed class ProjectFileEdit(string projectPath, ProjectFileCompileItems original)
   {
      public string ProjectPath { get; } = projectPath;

      /// <summary>
      /// The project file before the workspace applied the change.
      /// </summary>
      public ProjectFileCompileItems Original { get; } = original;

      public List<string> RemovedFiles { get; } = [];

      public List<string> DuplicateFiles { get; } = [];

      /// <summary>
      /// The added files that the workspace lists once per target framework that adds them, and that need one item.
      /// </summary>
      public List<string> ListedOnceFiles { get; } = [];

      /// <summary>
      /// Takes the planned items out of the project file that the workspace wrote.
      /// </summary>
      /// <remarks>
      /// When the workspace's only edits were the duplicate items, the result is the original text, which is then
      /// written in the original bytes, because the workspace also adds a byte order mark when it saves.
      /// </remarks>
      public void Write()
      {
         var written = ProjectFileCompileItems.Read(ProjectPath);
         var text = written.TextWithout([.. RemovedFiles, .. DuplicateFiles], ListedOnceFiles);
         var originalText = Original.TextWithout(RemovedFiles);
         var bytes = text == originalText ? Original.Encode(originalText) : written.Encode(text);
         if (!bytes.AsSpan().SequenceEqual(written.OriginalBytes))
            File.WriteAllBytes(ProjectPath, bytes);
      }
   }

   /// <summary>
   /// Empty files at the paths of added documents that do not exist yet, with the directories created for them.
   /// </summary>
   private sealed class Placeholders : IDisposable
   {
      private readonly List<string> _files = [];
      private readonly List<string> _directories = [];
      private readonly Func<string, ToolException> _refuse;

      private Placeholders(Func<string, ToolException> refuse)
      {
         _refuse = refuse;
      }

      public static Placeholders Create(IEnumerable<string> filePaths, Func<string, ToolException> refuse)
      {
         var placeholders = new Placeholders(refuse);
         try
         {
            foreach (var filePath in filePaths.Where(p => !File.Exists(p)))
            {
               placeholders.CreateDirectory(Path.GetDirectoryName(filePath));
               using (new FileStream(filePath, FileMode.CreateNew))
                  placeholders._files.Add(filePath);
            }
         }
         catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
         {
            placeholders.Dispose();
            throw refuse($"an empty file could not be created to evaluate the project: {ex.Message}");
         }

         return placeholders;
      }

      /// <exception cref="ToolException">A placeholder could not be deleted.</exception>
      public void Delete()
      {
         try
         {
            foreach (var file in _files)
               File.Delete(file);
            _files.Clear();
            foreach (var directory in Enumerable.Reverse(_directories))
               Directory.Delete(directory);
            _directories.Clear();
         }
         catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
         {
            throw _refuse($"the empty file created to evaluate the project could not be deleted: {ex.Message}");
         }
      }

      /// <summary>
      /// Deletes what is left when an evaluation fails, without hiding its exception.
      /// </summary>
      public void Dispose()
      {
         foreach (var file in _files)
            TryDelete(() => File.Delete(file));
         foreach (var directory in Enumerable.Reverse(_directories))
            TryDelete(() => Directory.Delete(directory));
      }

      private static void TryDelete(Action delete)
      {
         try
         {
            delete();
         }
         catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
         {
            // The refusal reports the evaluation's error, which caused this cleanup.
         }
      }

      private void CreateDirectory(string? directory)
      {
         if (directory is null || Directory.Exists(directory))
            return;
         CreateDirectory(Path.GetDirectoryName(directory));
         Directory.CreateDirectory(directory);
         _directories.Add(directory);
      }
   }
}
