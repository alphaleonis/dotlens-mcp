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
   [Description("""
      Add a using directive to a file. Skips if the namespace is already imported, global usings included.
      Adds it where the file keeps its usings, inside the namespace declaration when they are there.
      Sorts the usings (System.* first).
      """)]
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

      var semanticModel = await document.GetSemanticModelAsync(cancellationToken)
         ?? throw new ToolException(ErrorCodes.AnalysisFailed, "Could not get semantic model");
      var requested = NormalizeName(@namespace);
      var endOfFile = compilationUnit.EndOfFileToken.SpanStart;
      var bound = semanticModel.GetSpeculativeSymbolInfo(
         endOfFile, SyntaxFactory.ParseName("global::" + requested), SpeculativeBindingOption.BindAsTypeOrNamespace);
      if (bound.Symbol is ITypeSymbol)
      {
         throw new ToolException(
            ErrorCodes.InvalidParameter,
            $"'{@namespace}' is a type, not a namespace",
            $"Add 'using static {requested};' with edit_file to import its members");
      }

      if (UsingDirectiveLayout.GetUsingsInScopeChain(compilationUnit)
          .Any(u => ImportsNamespace(u, requested, semanticModel, cancellationToken)))
      {
         return AlreadyExists(@namespace, $"Using directive '{@namespace}' already exists");
      }

      // The import scopes at the end of the file hold the global usings of the whole project, implicit ones included.
      if (semanticModel.GetImportScopes(endOfFile, cancellationToken)
          .SelectMany(scope => scope.Imports)
          .Any(import => import.NamespaceOrType is INamespaceSymbol imported
             && imported.ToDisplayString() == requested
             && import.DeclaringSyntaxReference?.SyntaxTree != syntaxTree))
      {
         return AlreadyExists(@namespace, $"Namespace '{@namespace}' is already imported by a global using");
      }

      var newLine = await UsingDirectiveLayout.GetNewLineAsync(document, cancellationToken);
      // A using inside a namespace declaration looks its name up in that namespace first.
      var name = UsingDirectiveLayout.GetNamespaceListPosition(compilationUnit) is { } listPosition
         && !BindsAsAtFileLevel(semanticModel, listPosition, requested)
            ? "global::" + requested
            : requested;
      var newUsing = SyntaxFactory.UsingDirective(
            SyntaxFactory.ParseName(name).WithLeadingTrivia(SyntaxFactory.Space))
         .WithTrailingTrivia(SyntaxFactory.EndOfLine(newLine));

      var newRoot = UsingDirectiveLayout.Add(compilationUnit, newUsing, newLine);
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

   private static ToolResult<AddUsingResponse> AlreadyExists(string @namespace, string message) =>
      ToolResult.Create(
         new AddUsingResponse
         {
            Namespace = @namespace,
            Added = false,
            AlreadyExists = true,
            Message = message
         },
         AlreadyExistsHints);

   /// <summary>
   /// Returns whether the using imports the namespace with the name <paramref name="requested"/>.
   /// </summary>
   /// <remarks>
   /// A using whose name does not bind, such as one for a package not yet referenced, matches by its text.
   /// </remarks>
   private static bool ImportsNamespace(
      UsingDirectiveSyntax usingDirective,
      string requested,
      SemanticModel semanticModel,
      CancellationToken cancellationToken)
   {
      if (!usingDirective.StaticKeyword.IsKind(SyntaxKind.None) || usingDirective.Alias is not null
          || usingDirective.Name is not { } name)
      {
         return false;
      }

      return semanticModel.GetSymbolInfo(name, cancellationToken).Symbol is INamespaceSymbol imported
         ? imported.ToDisplayString() == requested
         : NormalizeName(name.ToString()) == requested;
   }

   /// <summary>
   /// Returns whether the name binds at the position as it does at file level.
   /// </summary>
   /// <remarks>
   /// It compares the first identifier only, since the rest of the name binds as members of what that one binds to.
   /// </remarks>
   private static bool BindsAsAtFileLevel(SemanticModel semanticModel, int position, string name)
   {
      var leftmost = SyntaxFactory.ParseName(name);
      while (leftmost is QualifiedNameSyntax qualified)
         leftmost = qualified.Left;

      // An extern alias qualifies a name that binds the same everywhere.
      if (leftmost is not IdentifierNameSyntax identifier)
         return true;

      var here = semanticModel.GetSpeculativeSymbolInfo(
         position, identifier, SpeculativeBindingOption.BindAsTypeOrNamespace).Symbol;
      var atFileLevel = semanticModel.GetSpeculativeSymbolInfo(
         position,
         SyntaxFactory.ParseName("global::" + identifier.Identifier.ValueText),
         SpeculativeBindingOption.BindAsTypeOrNamespace).Symbol;
      return SymbolEqualityComparer.Default.Equals(here, atFileLevel);
   }

   /// <summary>
   /// Returns the name without whitespace and without a leading <c>global::</c>.
   /// </summary>
   private static string NormalizeName(string name)
   {
      var compact = string.Concat(name.Where(c => !char.IsWhiteSpace(c)));
      return compact.StartsWith("global::", StringComparison.Ordinal) ? compact["global::".Length..] : compact;
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
