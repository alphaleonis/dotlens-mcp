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

      if (!IsNamespaceName(@namespace))
      {
         throw new ToolException(
            ErrorCodes.InvalidParameter,
            $"'{@namespace}' is not a valid namespace name",
            "Provide identifiers joined by dots, like 'System.Collections.Generic'");
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
      var requested = NormalizeName(SyntaxFactory.ParseName(@namespace));
      var fileLevel = BindAtFileLevel(semanticModel, compilationUnit, SyntaxFactory.ParseName(requested));
      if (fileLevel.Symbol is ITypeSymbol)
      {
         throw new ToolException(
            ErrorCodes.InvalidParameter,
            $"'{@namespace}' is a type, not a namespace",
            $"Add 'using static {requested};' with edit_file to import its members");
      }

      if (UsingDirectiveLayout.GetUsingsInScopeChain(compilationUnit)
          .Any(u => ImportsNamespace(u, requested, fileLevel.Symbol, semanticModel, cancellationToken)))
      {
         return AlreadyExists(@namespace, $"Using directive '{@namespace}' already exists");
      }

      // The import scopes at the end of the file hold the global usings of the whole project, implicit ones included.
      if (semanticModel.GetImportScopes(compilationUnit.EndOfFileToken.SpanStart, cancellationToken)
          .SelectMany(scope => scope.Imports)
          .Any(import => import.NamespaceOrType is INamespaceSymbol imported
             && SymbolEqualityComparer.Default.Equals(imported, fileLevel.Symbol)
             && import.DeclaringSyntaxReference?.SyntaxTree != syntaxTree))
      {
         return AlreadyExists(@namespace, $"Namespace '{@namespace}' is already imported by a global using");
      }

      var newLine = await UsingDirectiveLayout.GetNewLineAsync(document, cancellationToken);
      var name = requested;

      // A using inside a namespace declaration looks its name up in that namespace first.
      if (UsingDirectiveLayout.GetNamespaceListPosition(compilationUnit) is { } listPosition
          && fileLevel.First is { } first
          && !SymbolEqualityComparer.Default.Equals(
             semanticModel.GetSpeculativeSymbolInfo(listPosition, first, SpeculativeBindingOption.BindAsTypeOrNamespace).Symbol,
             fileLevel.FirstSymbol))
      {
         name = fileLevel.Qualified?.ToString() ?? throw new ToolException(
            ErrorCodes.NotSupported,
            $"Cannot add the using: a name in the namespace declaration hides the extern alias '{requested}'",
            "Add the using with edit_file at the place it belongs");
      }

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
      if (!_workspaceManager.ApplyChanges(newSolution, cancellationToken))
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
   /// Returns whether the using imports <paramref name="target"/>, the namespace that <paramref name="requested"/> binds to.
   /// </summary>
   /// <remarks>
   /// A using whose name does not bind, such as one for a package not yet referenced, matches by its text.
   /// </remarks>
   private static bool ImportsNamespace(
      UsingDirectiveSyntax usingDirective,
      string requested,
      ISymbol? target,
      SemanticModel semanticModel,
      CancellationToken cancellationToken)
   {
      if (!usingDirective.StaticKeyword.IsKind(SyntaxKind.None) || usingDirective.Alias is not null
          || usingDirective.Name is not { } name)
      {
         return false;
      }

      return semanticModel.GetSymbolInfo(name, cancellationToken).Symbol is INamespaceSymbol imported
         ? SymbolEqualityComparer.Default.Equals(imported, target)
         : NormalizeName(name) == requested;
   }

   /// <summary>
   /// Returns whether the text is a name that a using of a namespace can hold, with only whitespace between its tokens.
   /// </summary>
   private static bool IsNamespaceName(string text)
   {
      var name = SyntaxFactory.ParseName(text);
      return !name.ContainsDiagnostics
         && name.DescendantNodesAndSelf().All(n => n is IdentifierNameSyntax or QualifiedNameSyntax or AliasQualifiedNameSyntax)
         && name.DescendantTrivia().All(t => t.IsKind(SyntaxKind.WhitespaceTrivia) || t.IsKind(SyntaxKind.EndOfLineTrivia));
   }

   /// <summary>
   /// Binds the name as a using of the compilation unit binds it.
   /// </summary>
   /// <remarks>
   /// There, a namespace or type of the global namespace hides an extern alias of the same name.
   /// </remarks>
   private static FileLevelBinding BindAtFileLevel(SemanticModel semanticModel, CompilationUnitSyntax unit, NameSyntax name)
   {
      var endOfFile = unit.EndOfFileToken.SpanStart;
      ISymbol? Bind(NameSyntax toBind) => semanticModel
         .GetSpeculativeSymbolInfo(endOfFile, toBind, SpeculativeBindingOption.BindAsTypeOrNamespace).Symbol;

      var leftmost = name;
      while (leftmost is QualifiedNameSyntax qualified)
         leftmost = qualified.Left;

      // An alias-qualified name binds the same everywhere.
      if (leftmost is not IdentifierNameSyntax first)
         return new(name, Bind(name), null, null);

      var inGlobal = Bind(SyntaxFactory.ParseName("global::" + first));
      if (inGlobal is null
          && unit.Externs.FirstOrDefault(e => e.Identifier.ValueText == first.Identifier.ValueText) is { } externAlias
          && semanticModel.GetDeclaredSymbol(externAlias) is { } alias)
      {
         if (first.Parent is not QualifiedNameSyntax innermost)
            return new(null, alias.Target, first, alias.Target);

         var aliasQualified = name.ReplaceNode(innermost, SyntaxFactory.AliasQualifiedName(first, innermost.Right));
         return new(aliasQualified, Bind(aliasQualified), first, alias.Target);
      }

      var globalQualified = SyntaxFactory.ParseName("global::" + name);
      return new(globalQualified, Bind(globalQualified), first, inGlobal);
   }

   /// <summary>
   /// A name as a using of the compilation unit binds it.
   /// </summary>
   /// <param name="Qualified">
   /// The name qualified so it binds the same anywhere in the file, or null when the name is an extern alias alone.
   /// </param>
   /// <param name="Symbol">What the name binds to, or null when it binds to nothing.</param>
   /// <param name="First">The first identifier of the name, or null when the name starts with an alias qualifier.</param>
   /// <param name="FirstSymbol">What <paramref name="First"/> binds to, or null when it binds to nothing.</param>
   private sealed record FileLevelBinding(NameSyntax? Qualified, ISymbol? Symbol, IdentifierNameSyntax? First, ISymbol? FirstSymbol);

   /// <summary>
   /// Returns the text of the name's tokens, which leaves out its trivia and a leading <c>global::</c>.
   /// </summary>
   private static string NormalizeName(NameSyntax name)
   {
      var compact = string.Concat(name.DescendantTokens().Select(t => t.Text));
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
