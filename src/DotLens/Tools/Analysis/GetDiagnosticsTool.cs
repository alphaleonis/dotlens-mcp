using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using DotLens.Infrastructure;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Analysis;

#region Response Types

/// <summary>
/// A single diagnostic entry with location.
/// </summary>
public record DiagnosticEntry
{
   [Description("Diagnostic code (e.g., CS0103, CA2007).")]
   public required string Id { get; init; }

   [Description("Severity level: error, warning, info, or hidden.")]
   public required DiagnosticSeverityDto Severity { get; init; }

   [Description("Human-readable diagnostic message.")]
   public required string Message { get; init; }

   [Description("Source location of the diagnostic.")]
   public required SourceSpan Location { get; init; }
}

/// <summary>
/// Result from GetDiagnostics tool.
/// </summary>
public record GetDiagnosticsResponse
{
   [Description("Number of error-level diagnostics.")]
   public required int ErrorCount { get; init; }

   [Description("Number of warning-level diagnostics.")]
   public required int WarningCount { get; init; }

   [Description("List of diagnostics found.")]
   public required IReadOnlyList<DiagnosticEntry> Diagnostics { get; init; }
}

#endregion

/// <summary>
/// Get compiler errors, warnings, and info messages.
/// </summary>
[McpServerToolType]
[DotLensWorkerTool]
public class GetDiagnosticsTool
{
   private static readonly string[] SuggestedNextToolsWithDiagnostics = ["get_code_actions for a diagnostic to see available fixes", "apply_code_action to apply a fix automatically"];
   private static readonly string[] SuggestedNextToolsNoDiagnostics = ["No diagnostics found - solution is healthy"];
   private static readonly string[] SuggestedNextToolsWithLoadWarning =
      ["reload_solution after dotnet restore, so every project resolves its framework references"];

   private readonly IReadOnlyWorkspace _workspaceManager;

   public GetDiagnosticsTool(IReadOnlyWorkspace workspaceManager)
   {
      _workspaceManager = workspaceManager;
   }

   [McpServerTool(Name = "get_diagnostics", ReadOnly = true, OpenWorld = false)]
   [Description("Get compiler errors, warnings, and info messages. Set includeAnalyzers=true for code analysis rules (CA* codes).")]
   public async Task<ToolResult<GetDiagnosticsResponse>> GetDiagnosticsAsync(
      [Description("Path to specific file. Omit for all files.")]
      string? filePath = null,
      [Description("Path to specific project.")]
      string? projectPath = null,
      [Description("Filter by severity level.")]
      DiagnosticSeverityDto? severity = null,
      [Description("Filter by diagnostic ID pattern. Supports wildcards: * (any chars), ? (single char). Examples: CA*, CS0*, IDE????")]
      string? idPattern = null,
      [Description("Include hidden diagnostics.")]
      bool includeHidden = false,
      [Description("Include analyzer diagnostics (CA* codes). Slower but more thorough.")]
      bool includeAnalyzers = false,
      CancellationToken cancellationToken = default)
   {
      var solution = _workspaceManager.EnsureSolutionLoaded();

      // Collect diagnostics based on scope
      List<Diagnostic> allDiagnostics;
      var projectsInScope = new HashSet<ProjectId>();
      if (!string.IsNullOrEmpty(filePath))
         allDiagnostics = await CollectFileDiagnosticsAsync(filePath, includeAnalyzers, projectsInScope, cancellationToken);
      else if (!string.IsNullOrEmpty(projectPath))
         allDiagnostics = await CollectProjectDiagnosticsAsync(
            solution, projectPath, includeAnalyzers, projectsInScope, cancellationToken);
      else
         allDiagnostics = await CollectSolutionDiagnosticsAsync(solution, includeAnalyzers, projectsInScope, cancellationToken);

      // Apply filters and build result
      return BuildResult(allDiagnostics, severity, idPattern, includeHidden, LoadWarningFor(projectsInScope));
   }

   /// <returns>The load warnings of the projects in scope, joined, or null when they have none.</returns>
   /// <remarks>
   /// The filters do not apply to them, because each says that diagnostics of its project are missing.
   /// </remarks>
   private string? LoadWarningFor(HashSet<ProjectId> projectsInScope)
   {
      var messages = _workspaceManager.LoadWarnings
         .Where(w => w.ProjectId is { } projectId && projectsInScope.Contains(projectId))
         .Select(w => w.Message)
         .ToList();
      return messages.Count > 0 ? string.Join(" ", messages) : null;
   }

   private async Task<List<Diagnostic>> CollectFileDiagnosticsAsync(
      string filePath,
      bool includeAnalyzers,
      HashSet<ProjectId> projectsInScope,
      CancellationToken cancellationToken)
   {
      var diagnostics = new List<Diagnostic>();

      var document = await _workspaceManager.TryGetDocumentAsync(filePath, cancellationToken);
      if (document == null)
      {
         throw new ToolException(
            ErrorCodes.FileNotInSolution,
            $"File not found in solution: {filePath}",
            "Check the file path or reload the solution");
      }

      projectsInScope.Add(document.Project.Id);
      var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
      if (semanticModel != null)
      {
         diagnostics.AddRange(semanticModel.GetDiagnostics(cancellationToken: cancellationToken));

         if (includeAnalyzers)
         {
            await AddAnalyzerDiagnosticsAsync(document.Project, semanticModel, diagnostics, cancellationToken);
         }
      }

      return diagnostics;
   }

   private static async Task<List<Diagnostic>> CollectProjectDiagnosticsAsync(
      Solution solution,
      string projectPath,
      bool includeAnalyzers,
      HashSet<ProjectId> projectsInScope,
      CancellationToken cancellationToken)
   {
      var diagnostics = new List<Diagnostic>();

      var project = solution.Projects.FirstOrDefault(p => p.FilePath == projectPath);
      if (project == null)
      {
         throw new ToolException(
            ErrorCodes.ProjectNotFound,
            $"Project not found: {projectPath}",
            "Check the project path or use get_project_structure to list available projects");
      }

      projectsInScope.Add(project.Id);
      var compilation = await project.GetCompilationAsync(cancellationToken);
      if (compilation != null)
      {
         diagnostics.AddRange(compilation.GetDiagnostics(cancellationToken));

         if (includeAnalyzers)
         {
            await AddAnalyzerDiagnosticsAsync(project, compilation, diagnostics, cancellationToken);
         }
      }

      return diagnostics;
   }

   private static async Task<List<Diagnostic>> CollectSolutionDiagnosticsAsync(
      Solution solution,
      bool includeAnalyzers,
      HashSet<ProjectId> projectsInScope,
      CancellationToken cancellationToken)
   {
      var diagnostics = new List<Diagnostic>();

      foreach (var project in solution.Projects)
      {
         cancellationToken.ThrowIfCancellationRequested();
         projectsInScope.Add(project.Id);

         var compilation = await project.GetCompilationAsync(cancellationToken);
         if (compilation != null)
         {
            diagnostics.AddRange(compilation.GetDiagnostics(cancellationToken));

            if (includeAnalyzers)
            {
               await AddAnalyzerDiagnosticsAsync(project, compilation, diagnostics, cancellationToken);
            }
         }
      }

      return diagnostics;
   }

   private ToolResult<GetDiagnosticsResponse> BuildResult(
      List<Diagnostic> allDiagnostics,
      DiagnosticSeverityDto? severity,
      string? idPattern,
      bool includeHidden,
      string? loadWarning)
   {
      var idMatcher = new GlobMatcher(idPattern);

      var filteredDiagnostics = allDiagnostics
         .Where(d => severity == null || d.Severity == ToRoslynSeverity(severity.Value))
         .Where(d => includeHidden || d.Severity != DiagnosticSeverity.Hidden)
         .Where(d => idMatcher.IsMatch(d.Id))
         .Take(_workspaceManager.MaxDiagnostics);

      var diagnosticList = new List<DiagnosticEntry>();
      int errorCount = 0;
      int warningCount = 0;

      foreach (var d in filteredDiagnostics)
      {
         var lineSpan = d.Location.GetLineSpan();
         var dtoSeverity = d.Severity.ToDtoSeverity();

         if (dtoSeverity == DiagnosticSeverityDto.Error)
            errorCount++;
         else if (dtoSeverity == DiagnosticSeverityDto.Warning)
            warningCount++;

         diagnosticList.Add(new DiagnosticEntry
         {
            Id = d.Id,
            Severity = dtoSeverity,
            Message = d.GetMessage(CultureInfo.InvariantCulture),
            Location = lineSpan.ToSourceSpan(_workspaceManager.SolutionPath, _workspaceManager.UseAbsolutePaths)
         });
      }

      return ToolResult.Create(
         new GetDiagnosticsResponse
         {
            ErrorCount = errorCount,
            WarningCount = warningCount,
            Diagnostics = diagnosticList
         },
         loadWarning is not null ? SuggestedNextToolsWithLoadWarning
            : errorCount > 0 || warningCount > 0 ? SuggestedNextToolsWithDiagnostics
            : SuggestedNextToolsNoDiagnostics,
         diagnosticList.Count,
         diagnosticList.Count,
         warning: loadWarning);
   }

   private static ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(Project project) =>
      project.AnalyzerReferences
         .SelectMany(r => r.GetAnalyzers(LanguageNames.CSharp))
         .ToImmutableArray();

   private static async Task AddAnalyzerDiagnosticsAsync(
      Project project,
      SemanticModel semanticModel,
      List<Diagnostic> diagnostics,
      CancellationToken cancellationToken)
   {
      var analyzers = GetAnalyzers(project);
      if (analyzers.Length > 0)
      {
         var compilation = semanticModel.Compilation;
         var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers, options: null);
         var analyzerDiagnostics = await compilationWithAnalyzers
            .GetAnalyzerSemanticDiagnosticsAsync(semanticModel, filterSpan: null, cancellationToken);
         diagnostics.AddRange(analyzerDiagnostics);
      }
   }

   private static async Task AddAnalyzerDiagnosticsAsync(
      Project project,
      Compilation compilation,
      List<Diagnostic> diagnostics,
      CancellationToken cancellationToken)
   {
      var analyzers = GetAnalyzers(project);
      if (analyzers.Length > 0)
      {
         var compilationWithAnalyzers = compilation.WithAnalyzers(analyzers, options: null);
         var analyzerDiagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync(cancellationToken);
         diagnostics.AddRange(analyzerDiagnostics);
      }
   }

   private static DiagnosticSeverity ToRoslynSeverity(DiagnosticSeverityDto dto) => dto switch
   {
      DiagnosticSeverityDto.Hidden => DiagnosticSeverity.Hidden,
      DiagnosticSeverityDto.Info => DiagnosticSeverity.Info,
      DiagnosticSeverityDto.Warning => DiagnosticSeverity.Warning,
      DiagnosticSeverityDto.Error => DiagnosticSeverity.Error,
      _ => DiagnosticSeverity.Warning
   };

}
