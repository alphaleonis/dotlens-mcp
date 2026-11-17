using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
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

   private readonly AsyncLock _writeLock = new();
   private MSBuildWorkspace? _workspace;
   private Solution? _solution;
   private readonly MemoryCache _documentCache;
   private readonly ILogger<WorkspaceManager> _logger;
   private readonly DotLensOptions _options;
   private DateTime? _solutionLoadedAt;

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

         // Dispose existing workspace
         _workspace?.Dispose();
         ResetDocumentCache();

         _workspace = MSBuildWorkspace.Create();
         _workspace.RegisterWorkspaceFailedHandler(args =>
         {
            _logger.LogWarning("Workspace warning: {Message}", args.Diagnostic.Message);
         });

         _solution = await _workspace.OpenSolutionAsync(solutionPath, cancellationToken: cancellationToken);
         _solutionLoadedAt = DateTime.UtcNow;

         var projectCount = _solution.ProjectIds.Count;
         var documentCount = _solution.Projects.Sum(p => p.DocumentIds.Count);

         _logger.LogInformation("Solution loaded: {Projects} projects, {Documents} documents",
             projectCount, documentCount);

         return ToolResult.Create(
             new WorkerLoadSolutionResult
             {
                SolutionPath = solutionPath,
                ProjectCount = projectCount,
                DocumentCount = documentCount
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
         var pathComparer = PathUtils.PathComparison == StringComparison.OrdinalIgnoreCase
             ? StringComparer.OrdinalIgnoreCase
             : StringComparer.Ordinal;
         var documentsByPath = new Dictionary<string, List<Document>>(pathComparer);
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
               Message = "No solution loaded. Call roslyn:load_solution first or set DOTNET_SOLUTION_PATH environment variable.",
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
               Warnings = warningCount
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
         throw new InvalidOperationException("No solution loaded. Call load_solution first or set DOTNET_SOLUTION_PATH environment variable.");
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

   public bool ApplyChanges(Solution newSolution)
   {
      Debug.Assert(_writeLock.IsHeld, "WriteLock must be held when calling ApplyChanges");

      if (_workspace == null)
         throw new InvalidOperationException("No workspace loaded. Call LoadSolutionAsync first.");

      bool success = _workspace.TryApplyChanges(newSolution);
      if (success)
      {
         // TryApplyChanges updates workspace.CurrentSolution, so use that
         _solution = _workspace.CurrentSolution;
         ResetDocumentCache();
      }
      else
      {
         // Log diagnostic information when TryApplyChanges fails
         // Note: Roslyn's TryApplyChanges doesn't provide failure details, so we log what was attempted
         var changes = newSolution.GetChanges(_workspace.CurrentSolution);
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
