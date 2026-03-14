using System.ComponentModel;
using DotLens.Infrastructure;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using ModelContextProtocol.Server;

namespace DotLens.Tools.Refactoring;

[McpServerToolType]
[DotLensWorkerTool]
public class AddUsingTool
{
   private static readonly string[] AlreadyExistsHints = ["organize_usings to sort and clean up usings"];
   private static readonly string[] PreviewHints = ["add_using with preview=false to apply the change"];
   private static readonly string[] AppliedHints = ["get_diagnostics to verify no issues after adding using"];

   private readonly IWorkspace _workspaceManager;

   public AddUsingTool(IWorkspace workspaceManager)
   {
      _workspaceManager = workspaceManager;
   }

   [McpServerTool(Name = "add_using", Destructive = true, Idempotent = true, OpenWorld = false)]
   [Description("Add a using directive to a file. Skips if already present. Automatically sorts usings (System.* first).")]
   public async Task<ToolResult<AddUsingResponse>> AddUsingAsync(
      [Description("Path to source file (absolute or relative to solution directory)")] string filePath,
      [Description("Namespace to add (e.g., 'System.Linq')")] string @namespace,
      [Description("Preview mode returns new content without saving")]
      bool preview = true,
      CancellationToken cancellationToken = default)
   {
      using var writeLock = await _workspaceManager.WriteLock.AcquireAsync(cancellationToken);

      var solution = _workspaceManager.EnsureSolutionLoaded();

      if (string.IsNullOrWhiteSpace(@namespace))
      {
         throw new ToolException(
            ErrorCodes.InvalidParameter,
            "Namespace is required",
            "Provide a namespace like 'System.Linq' or 'System.Collections.Generic'");
      }

      var document = await _workspaceManager.GetDocumentAsync(filePath, cancellationToken);

      var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
      if (syntaxTree == null)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            "Could not get syntax tree");
      }

      var root = await syntaxTree.GetRootAsync(cancellationToken);
      if (root is not CompilationUnitSyntax compilationUnit)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            "Not a valid C# file",
            "This tool only works with C# source files");
      }

      // Check if using already exists
      var existingUsings = compilationUnit.Usings;
      if (existingUsings.Any(u => u.Name?.ToString() == @namespace))
      {
         return ToolResult.Create(
            new AddUsingResponse
            {
               Namespace = @namespace,
               Added = false,
               AlreadyExists = true,
               Message = $"Using directive '{@namespace}' already exists"
            },
            AlreadyExistsHints);
      }

      // Create new using directive with proper spacing
      var newUsing = SyntaxFactory.UsingDirective(
            SyntaxFactory.ParseName(@namespace).WithLeadingTrivia(SyntaxFactory.Space))
         .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

      // Add to existing usings (sorted)
      var newUsings = existingUsings.Add(newUsing);
      var sortedUsings = newUsings
         .OrderBy(u => u.Name?.ToString()?.StartsWith("System", StringComparison.Ordinal) == true ? 0 : 1)
         .ThenBy(u => u.Name?.ToString())
         .ToList();

      var newRoot = compilationUnit.WithUsings(SyntaxFactory.List(sortedUsings));
      var newText = newRoot.ToFullString();

      if (preview)
      {
         return ToolResult.Create(
            new AddUsingResponse
            {
               Namespace = @namespace,
               Added = false,
               Preview = true,
               NewFileContent = newText
            },
            PreviewHints);
      }

      // Apply changes
      var newSolution = solution.WithDocumentSyntaxRoot(document.Id, newRoot);
      if (!_workspaceManager.ApplyChanges(newSolution))
      {
         throw new ToolException(
            ErrorCodes.ApplyChangesFailed,
            "Failed to apply changes to workspace");
      }

      return ToolResult.Create(
         new AddUsingResponse
         {
            Namespace = @namespace,
            Added = true,
            Preview = false,
            Applied = true
         },
         AppliedHints);
   }
}

/// <summary>
/// Result for AddUsingTool.
/// </summary>
public record AddUsingResponse
{
   [Description("The namespace that was added")]
   public required string Namespace { get; init; }

   [Description("True if the using directive was added")]
   public required bool Added { get; init; }

   [Description("True if the using directive already existed")]
   public bool? AlreadyExists { get; init; }

   [Description("True if this was a preview (changes not saved)")]
   public bool? Preview { get; init; }

   [Description("True if changes were applied to disk")]
   public bool? Applied { get; init; }

   [Description("Additional information about the operation")]
   public string? Message { get; init; }

   [Description("New file content (only in preview mode)")]
   public string? NewFileContent { get; init; }
}
