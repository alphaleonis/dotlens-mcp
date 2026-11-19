using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotLens.Infrastructure;

/// <summary>
/// Manages the MSBuild workspace and solution lifecycle.
/// </summary>
public sealed class WorkspaceManager : IWorkspace, IDisposable
{
   private static readonly string[] LoadSolutionSuggestedTools = ["get_project_structure to see solution structure", "search_symbols to find code", "get_diagnostics to check for issues"];
   private static readonly string[] SyncDocumentsSuggestedTools = ["get_diagnostics to check for errors after sync", "search_symbols to find updated code"];

   // Workspace.SetCurrentSolution is protected, and MSBuildWorkspace is sealed.
   private static readonly Lazy<Func<Workspace, Solution, Solution>> SetWorkspaceCurrentSolution = new(() =>
      typeof(Workspace).GetMethod("SetCurrentSolution", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(Solution)])
         ?.CreateDelegate<Func<Workspace, Solution, Solution>>()
      ?? throw new InvalidOperationException("Workspace.SetCurrentSolution(Solution) was not found"));

   // Workspace.SetCurrentSolution raises the content version, and TryApplyChanges refuses a solution with an older one,
   // so the field is set directly to keep the version of the solutions that tools fork.
   // https://github.com/dotnet/roslyn/blob/1324e1d34a0490454837a22370e75bbaeecffb87/src/Workspaces/Core/Portable/Workspace/Solution/SolutionState.cs#L240-L247
   // https://github.com/dotnet/roslyn/blob/1324e1d34a0490454837a22370e75bbaeecffb87/src/Workspaces/Core/Portable/Workspace/Workspace.cs#L1535
   private static readonly Lazy<FieldInfo> WorkspaceLatestSolution = new(() =>
      typeof(Workspace).GetField("_latestSolution", BindingFlags.Instance | BindingFlags.NonPublic) is { } field
         && field.FieldType == typeof(Solution)
         ? field
         : throw new InvalidOperationException("Workspace._latestSolution was not found"));

   private const string WorkspaceWriteFailed = "The workspace could not apply the changes";
   private const string PlanWriteFailed = "DotLens could not write every change";

   private readonly AsyncLock _writeLock = new();
   private MSBuildWorkspace? _workspace;
   private Solution? _solution;
   private readonly MemoryCache _documentCache;
   private readonly ILogger<WorkspaceManager> _logger;
   private readonly DotLensOptions _options;
   private DateTime? _solutionLoadedAt;
   private IReadOnlyList<LoadWarning> _loadWarnings = [];
   private readonly Lock _workspaceDiagnosticsLock = new();
   private List<WorkspaceDiagnostic>? _workspaceDiagnostics;

   public WorkspaceManager(ILogger<WorkspaceManager> logger, IOptions<DotLensOptions> options)
   {
      _logger = logger;
      _options = options.Value;
      _documentCache = new MemoryCache(new MemoryCacheOptions
      {
         SizeLimit = _options.MaxCacheSize
      });
   }

   public Solution? Solution => _solution;
   public string? SolutionPath => _solution?.FilePath;
   public string? SolutionDirectory
   {
      get
      {
         var filePath = _solution?.FilePath;
         return filePath is not null ? Path.GetDirectoryName(filePath) : null;
      }
   }
   public int ProjectCount => _solution?.ProjectIds.Count ?? 0;
   public int DocumentCount => _solution?.Projects.Sum(p => p.DocumentIds.Count) ?? 0;
   public bool IsSolutionLoaded => _solution != null;
   public bool UseAbsolutePaths => _options.UseAbsolutePaths;
   public int MaxDiagnostics => _options.MaxDiagnostics;
   public int TimeoutSeconds => _options.TimeoutSeconds;
   public int MaxSortableResults => _options.MaxSortableResults;
   public AsyncLock WriteLock => _writeLock;
   public IReadOnlyList<LoadWarning> LoadWarnings => _loadWarnings;

   /// <summary>
   /// Gets the MSBuildWorkspace's current solution, which every write leaves the same as <see cref="Solution"/>.
   /// </summary>
   internal Solution? WorkspaceSolution => _workspace?.CurrentSolution;

   public async Task<ToolResult<WorkerLoadSolutionResult>> LoadSolutionAsync(string solutionPath, CancellationToken cancellationToken = default)
   {
      if (!File.Exists(solutionPath))
      {
         throw new ToolException(
             ErrorCodes.FileNotFound,
             $"Solution file not found: {solutionPath}",
             hint: "Verify the path is correct and the file exists"
         );
      }

      using (await _writeLock.AcquireAsync(cancellationToken))
      {
         _logger.LogInformation("Loading solution: {SolutionPath}", solutionPath);

         // The new workspace replaces the current one only after its load succeeds, so a failed load leaves the
         // previous solution with the workspace that holds it.
         var workspace = MSBuildWorkspace.Create();
         workspace.RegisterWorkspaceFailedHandler(args =>
         {
            _logger.LogWarning("Workspace warning: {Message}", args.Diagnostic.Message);
            lock (_workspaceDiagnosticsLock)
               _workspaceDiagnostics?.Add(args.Diagnostic);
         });

         Solution solution;
         List<LoadWarning> loadWarnings;
         List<WorkspaceDiagnostic>? workspaceDiagnostics = null;
         var loaded = false;
         CollectWorkspaceDiagnostics();
         try
         {
            try
            {
               solution = await workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
            }
            finally
            {
               workspaceDiagnostics = TakeWorkspaceDiagnostics();
            }

            solution = SweepPlaceholders(workspace, solution);
            var frameworkWarnings = await FindProjectsWithoutFrameworkReferencesAsync(solution, cancellationToken);
            loadWarnings = [.. workspaceDiagnostics.Select(d => new LoadWarning($"{d.Kind}: {d.Message}", null)), .. frameworkWarnings];
            loaded = true;
         }
         catch (Exception ex) when (ex is not OperationCanceledException)
         {
            _logger.LogError(ex, "Failed to load solution: {SolutionPath}", solutionPath);
            var messages = (workspaceDiagnostics ?? TakeWorkspaceDiagnostics()).Select(d => $"{d.Kind}: {d.Message}").ToList();
            throw new ToolException(
               ErrorCodes.AnalysisFailed,
               $"Failed to load solution {solutionPath}: {ex.Message}",
               _solution?.FilePath is { } previousPath
                  ? $"Fix the cause, then load the solution again. {previousPath} stays loaded."
                  : "Fix the cause, then load the solution again.",
               messages.Count > 0 ? messages : null);
         }
         finally
         {
            if (!loaded)
               workspace.Dispose();
         }

         _workspace?.Dispose();
         _workspace = workspace;
         _solution = solution;
         _loadWarnings = loadWarnings;
         _solutionLoadedAt = DateTime.UtcNow;
         ResetDocumentCache();

         var projectCount = _solution.ProjectIds.Count;
         var documentCount = _solution.Projects.Sum(p => p.DocumentIds.Count);

         _logger.LogInformation("Solution loaded: {Projects} projects, {Documents} documents",
             projectCount, documentCount);
         foreach (var warning in _loadWarnings)
            _logger.LogWarning("Load warning: {Message}", warning.Message);

         return ToolResult.Create(
             new WorkerLoadSolutionResult
             {
                SolutionPath = solutionPath,
                ProjectCount = projectCount,
                DocumentCount = documentCount,
                Warnings = [.. _loadWarnings.Select(w => w.Message)]
             },
             suggestedNextTools: LoadSolutionSuggestedTools
         );
      }
   }

   public async Task<ToolResult<SyncDocumentsResult>> SyncDocumentsAsync(List<string>? filePaths, CancellationToken cancellationToken = default)
   {
      if (_solution == null || _workspace == null)
      {
         throw new ToolException(
             ErrorCodes.NoSolutionLoaded,
             "No solution loaded. Call load_solution first.",
             hint: "Use load_solution before syncing documents"
         );
      }

      using (await _writeLock.AcquireAsync(cancellationToken))
      {
         // Work with a local variable to avoid exposing partially-synced state.
         // Only assign back to _solution once all updates are complete.
         var solution = _solution;
         var solutionDir = Path.GetDirectoryName(solution.FilePath);
         var updated = new List<string>();
         var added = new List<string>();
         var removed = new List<string>();
         var errors = new List<SyncError>();

         // A path maps to several documents when more than one project compiles the file: a multi-targeted project,
         // a linked file, or a source file that a NuGet package adds to several projects.
         var documentsByPath = new Dictionary<string, List<Document>>(PathUtils.PathComparer);
         foreach (var document in solution.Projects.SelectMany(p => p.Documents))
         {
            if (document.FilePath is not { } filePath)
               continue;

            var fullPath = Path.GetFullPath(filePath);
            if (!documentsByPath.TryGetValue(fullPath, out var copies))
               documentsByPath[fullPath] = copies = [];
            copies.Add(document);
         }

         // Determine which files to sync
         IEnumerable<string> pathsToSync;
         if (filePaths != null && filePaths.Count > 0)
         {
            // Sync specific files with path traversal protection
            var validatedPaths = new List<string>();
            foreach (var p in filePaths)
            {
               try
               {
                  var validatedPath = PathUtils.ResolvePathSafely(p, solutionDir ?? "");
                  validatedPaths.Add(validatedPath);
               }
               catch (System.Security.SecurityException ex)
               {
                  _logger.LogWarning("Path traversal attempt blocked: {Path} - {Message}", p, ex.Message);
                  errors.Add(new SyncError { Path = p, Error = ex.Message });
               }
            }
            pathsToSync = validatedPaths;
         }
         else
         {
            // Sync all documents - check each for changes
            pathsToSync = documentsByPath.Keys.ToList();
         }

         foreach (var fullPath in pathsToSync)
         {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
               var fileExists = File.Exists(fullPath);
               documentsByPath.TryGetValue(fullPath, out var copies);

               if (fileExists && copies is not null)
               {
                  // Solution.WithDocumentText changes only the documents it is given (pinned by SyncDocuments_LinkedCopies tests).
                  var existingText = await copies[0].GetTextAsync(cancellationToken);
                  var encoding = existingText.Encoding ?? Encoding.UTF8;
                  var newText = await File.ReadAllTextAsync(fullPath, cancellationToken);
                  var sourceText = SourceText.From(newText, encoding);
                  solution = solution.WithDocumentText(copies.Select(d => d.Id), sourceText);
                  updated.Add(FormatPath(fullPath));
               }
               else if (fileExists && copies is null)
               {
                  // File is not a tracked Document in the solution
                  // Don't auto-add - this prevents adding .csproj or other non-source files
                  errors.Add(new SyncError
                  {
                     Path = FormatPath(fullPath),
                     Error = "File is not a tracked source document in the solution. Use reload_solution to pick up new files."
                  });
               }
               else if (!fileExists && copies is not null)
               {
                  solution = solution.RemoveDocuments([.. copies.Select(d => d.Id)]);
                  removed.Add(FormatPath(fullPath));
               }
               // else: file doesn't exist and not in solution - nothing to do
            }
            catch (Exception ex)
            {
               errors.Add(new SyncError { Path = FormatPath(fullPath), Error = ex.Message });
            }
         }

         // Commit all changes atomically
         if (updated.Count > 0 || added.Count > 0 || removed.Count > 0)
            UpdateSolution(solution);

         _logger.LogDebug("Sync complete: {Updated} updated, {Added} added, {Removed} removed",
             updated.Count, added.Count, removed.Count);

         return ToolResult.Create(
             new SyncDocumentsResult
             {
                Updated = updated.Count,
                Added = added.Count,
                Removed = removed.Count,
                TotalSynced = updated.Count + added.Count + removed.Count,
                UpdatedFiles = updated,
                AddedFiles = added,
                RemovedFiles = removed,
                Errors = errors.Count > 0 ? errors : null
             },
             suggestedNextTools: SyncDocumentsSuggestedTools
         );
      }
   }

   public async Task<ToolResult<IWorkerHealthCheckResult>> GetHealthCheckAsync()
   {
      if (_solution == null || _workspace == null)
      {
         return ToolResult.Create<IWorkerHealthCheckResult>(
            new WorkerHealthCheckNotReadyResult
            {
               Message = "No solution loaded. Call load_solution first or set DOTLENS_SolutionPath environment variable.",
               Solution = null,
               Workspace = null
            });
      }

      // Get diagnostic summary
      var errorCount = 0;
      var warningCount = 0;

      try
      {
         foreach (var project in _solution.Projects.Take(5)) // Sample first 5 projects for quick health check
         {
            var compilation = await project.GetCompilationAsync();
            if (compilation != null)
            {
               var diagnostics = compilation.GetDiagnostics();
               errorCount += diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
               warningCount += diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
            }
         }
      }
      catch
      {
         // Expected: Projects commonly fail to compile (missing refs, SDK issues).
         // This is a diagnostic sample - we just report 0 errors/warnings on failure.
         // No logging: would create noise for normal scenarios.
      }

      var projectCount = _solution.ProjectIds.Count;
      var documentCount = _solution.Projects.Sum(p => p.DocumentIds.Count);

      return ToolResult.Create<IWorkerHealthCheckResult>(
         new WorkerHealthCheckReadyResult
         {
            Message = "Roslyn MCP Server is operational",
            Solution = new WorkerSolutionInfo
            {
               Loaded = true,
               Path = _solution.FilePath,
               Projects = projectCount,
               Documents = documentCount,
               LoadedAt = _solutionLoadedAt?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
               Errors = errorCount,
               Warnings = warningCount,
               LoadWarnings = [.. _loadWarnings.Select(w => w.Message)]
            },
            Workspace = new WorkspaceInfo
            {
               Indexed = true,
               CacheSize = GetCacheCount()
            },
            Capabilities = new CapabilitiesInfo
            {
               FindReferences = true,
               FindImplementations = true,
               CodeFixProvider = true,
               SymbolSearch = true,
               Diagnostics = true
            },
            Configuration = new ConfigurationInfo
            {
               MaxDiagnostics = _options.MaxDiagnostics,
               TimeoutSeconds = _options.TimeoutSeconds,
               SemanticCacheEnabled = _options.EnableSemanticCache
            }
         });
   }

   public async Task<Document> GetDocumentAsync(string filePath, CancellationToken cancellationToken = default)
   {
      Document? document;
      try
      {
         document = await TryGetDocumentAsync(filePath, cancellationToken);
      }
      catch (System.Security.SecurityException ex)
      {
         throw new ToolException(
            ErrorCodes.SecurityError,
            ex.Message,
            "File path may be outside the solution directory");
      }

      if (document == null)
         throw new ToolException(
            ErrorCodes.FileNotInSolution,
            $"File not found in solution: {filePath}",
            "Check the file path or reload the solution");
      return document;
   }

   public Task<Document?> TryGetDocumentAsync(string filePath, CancellationToken cancellationToken = default)
   {
      cancellationToken.ThrowIfCancellationRequested();
      EnsureSolutionLoaded();

      // SolutionDirectory is guaranteed non-null because we only load solutions via
      // MSBuildWorkspace.OpenSolutionAsync(path), which always sets Solution.FilePath.
      Debug.Assert(SolutionDirectory != null, "SolutionDirectory should never be null after loading via MSBuildWorkspace");

      // Resolve path safely - validates path is within solution directory (prevents traversal attacks)
      var normalizedPath = PathUtils.ResolvePathSafely(filePath, SolutionDirectory);

      // Check cache first
      if (_documentCache.TryGetValue(normalizedPath, out Document? cached) && cached is not null)
         return Task.FromResult<Document?>(cached);

      // Find document in solution
      var document = _solution!.Projects
          .SelectMany(p => p.Documents)
          .FirstOrDefault(d => d.FilePath != null &&
              string.Equals(Path.GetFullPath(d.FilePath), normalizedPath, PathUtils.PathComparison));

      if (document == null)
         return Task.FromResult<Document?>(null);

      // Cache with sliding expiration
      if (_options.EnableSemanticCache)
      {
         var cacheEntryOptions = new MemoryCacheEntryOptions
         {
            Size = 1,
            SlidingExpiration = TimeSpan.FromMinutes(_options.CacheExpirationMinutes)
         };
         _documentCache.Set(normalizedPath, document, cacheEntryOptions);
      }

      return Task.FromResult<Document?>(document);
   }

   public Solution EnsureSolutionLoaded()
   {
      if (_solution == null)
      {
         throw new InvalidOperationException("No solution loaded. Call load_solution first or set DOTLENS_SolutionPath environment variable.");
      }
      return _solution;
   }

   public string FormatPath(string? absolutePath)
   {
      return PathUtils.FormatPath(absolutePath, _solution?.FilePath, _options.UseAbsolutePaths);
   }

   public async Task<INamedTypeSymbol?> FindTypeByNameAsync(string typeName, CancellationToken cancellationToken = default)
   {
      EnsureSolutionLoaded();
      return await RoslynHelpers.FindTypeByNameAsync(_solution!, typeName, cancellationToken);
   }

   public async Task<TypeLookupResult?> FindTypeWithCompilationAsync(string typeName, bool requireFullyQualifiedName = false, string? projectName = null, CancellationToken cancellationToken = default)
   {
      EnsureSolutionLoaded();
      return await RoslynHelpers.FindTypeWithCompilationAsync(_solution!, typeName, requireFullyQualifiedName, projectName, cancellationToken);
   }

   public bool ApplyChanges(Solution newSolution, CancellationToken cancellationToken)
   {
      Debug.Assert(_writeLock.IsHeld, "WriteLock must be held when calling ApplyChanges");

      if (_workspace == null)
         throw new InvalidOperationException("No workspace loaded. Call LoadSolutionAsync first.");

      cancellationToken.ThrowIfCancellationRequested();

      // MSBuildWorkspace deletes a removed file that another project still compiles, its project file edit throws on
      // an item that an SDK glob adds, and it lists an added file that the glob already includes. So the workspace
      // applies every change except the removals, and DocumentChangePlan writes the rest.
      // https://github.com/dotnet/roslyn/blob/1324e1d34a0490454837a22370e75bbaeecffb87/src/Workspaces/MSBuild/Core/MSBuild/MSBuildWorkspace.cs#L476-L547
      var currentSolution = _workspace.CurrentSolution;
      // The server ends a forwarded request after TimeoutSeconds (ForwardingMcpServerToolExtensions). Evaluations get
      // half of it, so the rest of the write has time left; every evaluation runs before anything is written.
      var evaluationBudget = TimeSpan.FromSeconds(_options.TimeoutSeconds / 2.0);
      var plan = DocumentChangePlan.Create(currentSolution, newSolution, FormatPath, evaluationBudget, cancellationToken);
      var stagedSolution = plan.RemovedDocumentIds.Count > 0
         ? currentSolution.RemoveDocuments([.. plan.RemovedDocumentIds])
         : currentSolution;
      FileSnapshot snapshot;
      try
      {
         snapshot = FileSnapshot.Take(FilesTheWorkspaceWrites(stagedSolution, newSolution).Concat(plan.FilesItWrites));
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
      {
         throw new ToolException(
            ErrorCodes.ApplyChangesFailed,
            $"DotLens could not apply the changes, because a file that the change writes or deletes could not be read first: {ex.Message}",
            "Fix the cause, then try again.");
      }

      // Past this point the write runs to its end, so the disk and both solutions agree when it returns.
      cancellationToken.ThrowIfCancellationRequested();

      if (stagedSolution != currentSolution)
         WorkspaceLatestSolution.Value.SetValue(_workspace, stagedSolution);

      // MSBuildWorkspace reports some failed writes as WorkspaceFailed events and continues, and others it throws.
      // https://github.com/dotnet/roslyn/blob/1324e1d34a0490454837a22370e75bbaeecffb87/src/Workspaces/MSBuild/Core/MSBuild/MSBuildWorkspace.cs#L514-L534
      bool success;
      Exception? exception = null;
      CollectWorkspaceDiagnostics();
      try
      {
         success = _workspace.TryApplyChanges(newSolution);
      }
      catch (Exception ex)
      {
         success = false;
         exception = ex;
      }

      var failures = TakeWorkspaceDiagnostics().Where(d => d.Kind == WorkspaceDiagnosticKind.Failure).ToList();
      if (exception is not null || failures.Count > 0)
      {
         var cause = exception?.Message ?? string.Join(" ", failures.Select(d => d.Message));
         RollBack(_workspace, currentSolution, snapshot, WorkspaceWriteFailed, cause, exception);
      }

      if (success)
      {
         List<string> planFailures;
         Exception? planException = null;
         try
         {
            planFailures = plan.Apply();
         }
         catch (Exception ex)
         {
            planException = ex;
            planFailures = [ex.Message];
         }

         if (planFailures.Count > 0)
            RollBack(_workspace, currentSolution, snapshot, PlanWriteFailed, string.Join("; ", planFailures), planException);

         // TryApplyChanges updates workspace.CurrentSolution, so use that
         _solution = _workspace.CurrentSolution;
         ResetDocumentCache();
      }
      else
      {
         // TryApplyChanges returns false only before it writes anything.
         // https://github.com/dotnet/roslyn/blob/1324e1d34a0490454837a22370e75bbaeecffb87/src/Workspaces/Core/Portable/Workspace/Workspace.cs#L1525-L1548
         WorkspaceLatestSolution.Value.SetValue(_workspace, currentSolution);

         // Log diagnostic information when TryApplyChanges fails
         // Note: Roslyn's TryApplyChanges doesn't provide failure details, so we log what was attempted
         var changes = newSolution.GetChanges(currentSolution);
         var addedDocs = changes.GetProjectChanges().SelectMany(pc => pc.GetAddedDocuments()).Count();
         var removedDocs = changes.GetProjectChanges().SelectMany(pc => pc.GetRemovedDocuments()).Count();
         var changedDocs = changes.GetProjectChanges().SelectMany(pc => pc.GetChangedDocuments()).Count();

         _logger.LogWarning(
            "TryApplyChanges failed. Attempted changes: {AddedDocs} added, {RemovedDocs} removed, {ChangedDocs} changed documents. " +
            "Common causes: file locked by another process, permission denied, or workspace doesn't support the change type.",
            addedDocs, removedDocs, changedDocs);
      }

      return success;
   }

   /// <summary>
   /// Returns both solutions to the one before a failed write, and writes back the files that the write changed.
   /// </summary>
   /// <param name="failure">The clause that opens the message and says which part of the write failed.</param>
   /// <exception cref="ToolException">Always, with the cause and each file that could not be restored.</exception>
   [DoesNotReturn]
   private void RollBack(
      MSBuildWorkspace workspace, Solution solution, FileSnapshot snapshot, string failure, string cause, Exception? exception)
   {
      WorkspaceLatestSolution.Value.SetValue(workspace, solution);
      _solution = solution;
      ResetDocumentCache();

      var (restored, restoreFailures) = snapshot.Restore(FormatPath);
      _logger.LogWarning(exception, "{Failure}: {Cause}", failure, cause);
      if (restoreFailures.Count > 0)
      {
         throw new ToolException(
            ErrorCodes.ApplyChangesFailed,
            $"{failure}, and these files could not be restored: {string.Join("; ", restoreFailures)}. The cause: {cause}",
            "Restore the files on disk, then call reload_solution.");
      }

      throw new ToolException(
         ErrorCodes.ApplyChangesFailed,
         restored > 0
            ? $"{failure}, so DotLens restored the files it had written: {cause}"
            : $"{failure} and left every file unchanged: {cause}",
         "Fix the cause, then try again.");
   }

   /// <summary>
   /// Returns the files that MSBuildWorkspace.TryApplyChanges can write when it applies the change.
   /// </summary>
   /// <remarks>
   /// They are the changed documents and additional documents, the files of added documents, and the project file of
   /// each project with a change that MSBuildWorkspace writes to it.
   /// https://github.com/dotnet/roslyn/blob/1324e1d34a0490454837a22370e75bbaeecffb87/src/Workspaces/MSBuild/Core/MSBuild/MSBuildWorkspace.cs#L293-L303
   /// </remarks>
   internal static IEnumerable<string> FilesTheWorkspaceWrites(Solution oldSolution, Solution newSolution)
   {
      foreach (var changes in newSolution.GetChanges(oldSolution).GetProjectChanges())
      {
         var oldProject = changes.OldProject;
         foreach (var id in changes.GetChangedDocuments())
         {
            if (oldProject.GetDocument(id)?.FilePath is { } path)
               yield return path;
         }

         foreach (var id in changes.GetChangedAdditionalDocuments())
         {
            if (oldProject.GetAdditionalDocument(id)?.FilePath is { } path)
               yield return path;
         }

         if (oldProject.FilePath is not { } projectPath)
            continue;

         foreach (var id in changes.GetAddedDocuments())
         {
            if (changes.NewProject.GetDocument(id) is { } document
               && DocumentChangePlan.WorkspaceAddedPath(document, projectPath) is { } path)
            {
               yield return path;
            }
         }

         if (changes.GetAddedDocuments().Any() || changes.GetRemovedDocuments().Any()
            || changes.GetAddedMetadataReferences().Any() || changes.GetRemovedMetadataReferences().Any()
            || changes.GetAddedProjectReferences().Any() || changes.GetRemovedProjectReferences().Any()
            || changes.GetAddedAnalyzerReferences().Any() || changes.GetRemovedAnalyzerReferences().Any())
         {
            yield return projectPath;
         }
      }
   }

   public async Task<Document> AddDocumentAsync(string filePath, string content, CancellationToken cancellationToken = default)
   {
      var solution = EnsureSolutionLoaded();

      // SolutionDirectory is guaranteed non-null after EnsureSolutionLoaded
      Debug.Assert(SolutionDirectory != null, "SolutionDirectory should never be null after loading via MSBuildWorkspace");

      // Resolve and validate path is within solution directory
      var resolvedPath = PathUtils.ResolvePathSafely(filePath, SolutionDirectory);

      // Find which project this file belongs to
      var project = FindContainingProject(resolvedPath, solution);
      if (project is null)
      {
         throw new ToolException(
            ErrorCodes.FileNotInAnyProject,
            $"File is not under any project directory: {filePath}",
            "The file path must be within a project directory in the solution");
      }

      var projectDir = Path.GetDirectoryName(project.FilePath)
         ?? throw new ToolException(ErrorCodes.InternalError, "Project file path has no parent directory");

      // Compute folder hierarchy (relative path segments between project dir and file's parent)
      var fileDir = Path.GetDirectoryName(resolvedPath)
         ?? throw new ToolException(ErrorCodes.InternalError, "File path has no parent directory");
      var relativePath = Path.GetRelativePath(projectDir, fileDir);
      var folders = relativePath == "."
         ? []
         : relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

      // Build new solution first (pure, non-destructive — validates before touching disk)
      var fileName = Path.GetFileName(resolvedPath);
      var documentId = DocumentId.CreateNewId(project.Id);
      var sourceText = SourceText.From(content, Encoding.UTF8);
      var newSolution = solution.AddDocument(documentId, fileName, sourceText, folders, resolvedPath);

      // Write file to disk (creates parent directories as needed)
      // Path already validated by ResolvePathSafely above — skip redundant validation in WriteFileSafelyAsync
      var parentDir = Path.GetDirectoryName(resolvedPath);
      if (!string.IsNullOrEmpty(parentDir))
         Directory.CreateDirectory(parentDir);
      await File.WriteAllTextAsync(resolvedPath, content, cancellationToken);

      // Commit the in-memory update
      UpdateSolution(newSolution);

      var document = _solution!.GetDocument(documentId);
      if (document is null)
      {
         throw new ToolException(
            ErrorCodes.InternalError,
            "Document not found in solution after adding");
      }

      return document;
   }

   private static Project? FindContainingProject(string absoluteFilePath, Solution solution)
   {
      Project? bestMatch = null;
      int bestMatchLength = -1;

      foreach (var project in solution.Projects)
      {
         var projectDir = Path.GetDirectoryName(project.FilePath);
         if (string.IsNullOrEmpty(projectDir))
            continue;

         // Ensure directory ends with separator for accurate prefix check
         if (!projectDir.EndsWith(Path.DirectorySeparatorChar))
            projectDir += Path.DirectorySeparatorChar;

         if (absoluteFilePath.StartsWith(projectDir, PathUtils.PathComparison) && projectDir.Length > bestMatchLength)
         {
            bestMatch = project;
            bestMatchLength = projectDir.Length;
         }
      }

      return bestMatch;
   }

   public void UpdateSolution(Solution newSolution)
   {
      Debug.Assert(_writeLock.IsHeld, "WriteLock must be held when calling UpdateSolution");
      if (_workspace == null)
         throw new InvalidOperationException("No workspace loaded. Call LoadSolutionAsync first.");

      // TryApplyChanges writes every difference from the workspace's current solution, so the workspace holds this one.
      _solution = SetWorkspaceCurrentSolution.Value(_workspace, newSolution);
      ResetDocumentCache();
   }

   private void CollectWorkspaceDiagnostics()
   {
      lock (_workspaceDiagnosticsLock)
         _workspaceDiagnostics = [];
   }

   /// <returns>The diagnostics that the workspace reported since <see cref="CollectWorkspaceDiagnostics"/>.</returns>
   private List<WorkspaceDiagnostic> TakeWorkspaceDiagnostics()
   {
      lock (_workspaceDiagnosticsLock)
      {
         var diagnostics = _workspaceDiagnostics ?? [];
         _workspaceDiagnostics = null;
         return diagnostics;
      }
   }

   /// <summary>
   /// Deletes the placeholders that an interrupted write left in the solution's projects, and their documents.
   /// </summary>
   /// <returns>The solution without the documents of the deleted placeholders.</returns>
   private Solution SweepPlaceholders(MSBuildWorkspace workspace, Solution solution)
   {
      if (Path.GetDirectoryName(solution.FilePath) is not { } solutionDirectory)
         return solution;

      var projectDirectories = solution.Projects
         .Select(p => Path.GetDirectoryName(p.FilePath))
         .OfType<string>()
         .Distinct(PathUtils.PathComparer);
      var deleted = projectDirectories.SelectMany(d => EvaluationPlaceholders.Sweep(d, solutionDirectory)).ToList();
      if (deleted.Count == 0)
         return solution;

      _logger.LogInformation("Deleted placeholder files that an interrupted write left: {Files}", string.Join(", ", deleted));
      var documentIds = deleted.SelectMany(path => solution.GetDocumentIdsWithFilePath(path)).ToImmutableArray();
      return documentIds.IsEmpty
         ? solution
         : SetWorkspaceCurrentSolution.Value(workspace, solution.RemoveDocuments(documentIds));
   }

   /// <returns>A warning for each unrestored project whose compilation does not resolve System.Object.</returns>
   /// <remarks>
   /// A restored project without System.Object opts out of framework references, as Microsoft.Build.NoTargets projects
   /// do, so it gets no warning.
   /// </remarks>
   private static async Task<List<LoadWarning>> FindProjectsWithoutFrameworkReferencesAsync(
      Solution solution, CancellationToken cancellationToken)
   {
      var warnings = new List<LoadWarning>();
      foreach (var project in solution.Projects.Where(p => p.SupportsCompilation))
      {
         // A compilation of the metadata references alone has no source to parse or generate, so it costs little.
         var referencesOnly = project
            .WithProjectReferences([])
            .WithAnalyzerReferences([])
            .RemoveDocuments([.. project.DocumentIds]);
         if (await ResolvesSystemObjectAsync(referencesOnly, cancellationToken)
            || IsRestored(project)
            // A core library defines System.Object in its own source, or gets it from a project it references.
            || await ResolvesSystemObjectAsync(project, cancellationToken))
         {
            continue;
         }

         warnings.Add(new LoadWarning(
            $"Project {project.Name} resolved no framework references (System.Object is missing), so its diagnostics are "
               + "incomplete. Run dotnet restore and call reload_solution.",
            project.Id));
      }

      return warnings;
   }

   /// <summary>
   /// Returns whether the restore wrote the project's project.assets.json.
   /// </summary>
   /// <remarks>
   /// The restore writes it to the base intermediate directory, an ancestor of the intermediate assembly's directory.
   /// The search stops at an ancestor that also holds the project directory, because that one is shared with others.
   /// Projects can also share the base intermediate directory, so an assets file counts only when it names this project.
   /// </remarks>
   private static bool IsRestored(Project project)
   {
      const string assetsFile = "project.assets.json";
      if (project.FilePath is not { } projectPath || Path.GetDirectoryName(projectPath) is not { } projectDirectory)
         return false;
      if (IsAssetsFileOf(Path.Combine(projectDirectory, "obj", assetsFile), projectPath))
         return true;

      for (var directory = Path.GetDirectoryName(project.CompilationOutputInfo.AssemblyPath);
         directory is not null && !PathUtils.IsSameOrUnder(projectDirectory, directory);
         directory = Path.GetDirectoryName(directory))
      {
         if (IsAssetsFileOf(Path.Combine(directory, assetsFile), projectPath))
            return true;
      }

      return false;
   }

   /// <returns>Whether the file exists and its project.restore.projectPath names the project file.</returns>
   private static bool IsAssetsFileOf(string assetsPath, string projectPath)
   {
      if (!File.Exists(assetsPath))
         return false;

      try
      {
         using var stream = File.OpenRead(assetsPath);
         using var document = JsonDocument.Parse(stream);
         return document.RootElement.TryGetProperty("project", out var assetsProject)
            && assetsProject.TryGetProperty("restore", out var restore)
            && restore.TryGetProperty("projectPath", out var restoredPath)
            && restoredPath.GetString() is { } restoredProjectPath
            && PathUtils.PathComparer.Equals(Path.GetFullPath(restoredProjectPath), Path.GetFullPath(projectPath));
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException
         or ArgumentException)
      {
         return false;
      }
   }

   private static async Task<bool> ResolvesSystemObjectAsync(Project project, CancellationToken cancellationToken) =>
      await project.GetCompilationAsync(cancellationToken) is not { } compilation
         || compilation.GetSpecialType(SpecialType.System_Object).TypeKind != TypeKind.Error;

   public void ClearCache()
   {
      ResetDocumentCache();
   }

   private int GetCacheCount() => _documentCache.Count;

   private void ResetDocumentCache() => _documentCache.Clear();

   public void Dispose()
   {
      _workspace?.Dispose();
      _workspace = null;
      _solution = null;
      _documentCache.Dispose();
      _writeLock.Dispose();
   }
}
