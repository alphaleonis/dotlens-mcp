using System.Collections.Immutable;
using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;

using ModelContextProtocol.Server;

namespace DotLens.Tools.Refactoring;

#region Response Types

/// <summary>
/// Result for AddNullChecksTool.
/// Supports verbosity levels: summary (count only), compact (names/types), full (includes guard code).
/// </summary>
public record AddNullChecksResponse
{
   [Description("Name of the method that received null checks")]
   public required string MethodName { get; init; }

   [Description("Number of parameters that received null checks")]
   public required int ParameterCount { get; init; }

   [Description("Parameter details (null in summary verbosity mode)")]
   public IReadOnlyList<NullCheckParameterInfo>? Parameters { get; init; }

   [Description("The generated guard code (only in full verbosity mode)")]
   public string? GuardCode { get; init; }

   [Description("True if this was a preview (changes not saved)")]
   public required bool Preview { get; init; }

   [Description("True if changes were applied to disk")]
   public required bool Applied { get; init; }

   [Description("Path to the modified file")]
   public string? FilePath { get; init; }

   [Description("Additional information about the operation")]
   public string? Note { get; init; }
}

/// <summary>
/// Parameter info for null checks.
/// </summary>
public record NullCheckParameterInfo
{
   [Description("Parameter name")]
   public required string Name { get; init; }

   [Description("Parameter type")]
   public required string Type { get; init; }
}

#endregion

[McpServerToolType]
[DotLensWorkerTool]
public class AddNullChecksTool
{
   private static readonly string[] PreviewHints = ["add_null_checks with preview=false to apply changes"];
   private static readonly string[] AppliedHints = ["get_diagnostics to verify no issues"];

   private readonly IWorkspace _workspaceManager;
   private readonly SymbolLookupService _symbolLookupService;

   public AddNullChecksTool(IWorkspace workspaceManager, SymbolLookupService symbolLookupService)
   {
      _workspaceManager = workspaceManager;
      _symbolLookupService = symbolLookupService;
   }

   [McpServerTool(Name = "add_null_checks", Destructive = true, OpenWorld = false)]
   [Description("Add ArgumentNullException.ThrowIfNull guards for nullable parameters. Supports methods, constructors, operators, setters, local functions.")]
   public async Task<ToolResult<AddNullChecksResponse>> AddNullChecksAsync(
      [Description("Target method to add null checks to.")]
      SymbolLookup target,
      [Description("Preview mode (default: true). Set to false to apply.")]
      bool preview = true,
      [Description("Include non-nullable reference types (default: false). When true, generates null checks for ALL reference type parameters.")]
      bool includeNonNullable = false,
      [Description("Parameter names to exclude from null check generation.")]
      List<string>? excludeParams = null,
      [Description("Output verbosity: Summary (count only), Compact (names and types), Full (includes guard code). Default: Compact.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      CancellationToken cancellationToken = default)
   {
      using var writeLock = await _workspaceManager.WriteLock.AcquireAsync(cancellationToken);

      IReadOnlySet<string> excludedNames = excludeParams != null ? new HashSet<string>(excludeParams, StringComparer.OrdinalIgnoreCase) : ImmutableHashSet<string>.Empty;

      // Find the method using the polymorphic target parameter
      var lookupResult = await _symbolLookupService.FindSingleSourceSymbolAsync<IMethodSymbol>(target, cancellationToken);
      var document = lookupResult.Document;
      var methodSymbol = lookupResult.Symbol;
      var syntax = lookupResult.Syntax
         ?? throw new ToolException(ErrorCodes.AnalysisFailed, "Could not get method syntax");

      // Extract the body from various syntax types (GetBody throws for unsupported types)
      var body = GetBody(syntax)
         ?? throw new ToolException(ErrorCodes.AnalysisFailed, "Method has no body (expression-bodied or abstract)", "This tool requires a method with a block body");

      // Filter parameters based on includeNonNullable flag and exclusions
      var targetParams = methodSymbol.Parameters
         .Where(p => p.Type.IsReferenceType &&
                     (includeNonNullable || !p.Type.NullableAnnotation.Equals(NullableAnnotation.NotAnnotated)) &&
                     !excludedNames.Contains(p.Name))
         .ToList();

      if (targetParams.Count == 0)
      {
         var message = includeNonNullable ? "No reference type parameters found" : "No nullable reference type parameters found";
         var hint = excludedNames.Count > 0
            ? $"All eligible parameters were excluded. Excluded: {string.Join(", ", excludedNames)}"
            : includeNonNullable
               ? "Method has no reference type parameters"
               : "Method has no reference type parameters that need null checks. Use includeNonNullable=true to include non-nullable reference types.";
         throw new ToolException(ErrorCodes.AnalysisFailed, message, hint);
      }

      var semanticModel = await document.GetSemanticModelAsync(cancellationToken)
         ?? throw new ToolException(ErrorCodes.AnalysisFailed, "Could not get semantic model");

      // Check if ArgumentNullException.ThrowIfNull is available (.NET 6+)
      var useThrowIfNull = HasThrowIfNullMethod(semanticModel.Compilation);

      // Generate guard clause statements using SyntaxFactory
      var guardStatements = targetParams
         .Select(p => useThrowIfNull
            ? CreateThrowIfNullStatement(p.Name).WithAdditionalAnnotations(Simplifier.Annotation)
            : CreateLegacyNullCheckStatement(p.Name).WithAdditionalAnnotations(Simplifier.Annotation))
         .ToList();

      // Create new body with guard clauses at the start
      var newStatements = guardStatements.Concat(body.Statements);
      var newBody = body.WithStatements(SyntaxFactory.List(newStatements));
      var newSyntax = WithBody(syntax, newBody);

      // Replace in the syntax tree
      var root = await document.GetSyntaxRootAsync(cancellationToken);
      var newRoot = root!.ReplaceNode(syntax, newSyntax);

      // Apply simplification and formatting
      var newDocument = document.WithSyntaxRoot(newRoot);
      newDocument = await Simplifier.ReduceAsync(newDocument, cancellationToken: cancellationToken);
      var simplifiedRoot = await newDocument.GetSyntaxRootAsync(cancellationToken);
      var formattedRoot = Formatter.Format(simplifiedRoot!, document.Project.Solution.Workspace, cancellationToken: cancellationToken);

      var newText = formattedRoot.ToFullString();

      var guardCode = string.Join("\n", guardStatements.Select(s => s.NormalizeWhitespace().ToFullString()));

      // Build result based on verbosity
      var result = BuildResult(methodSymbol, targetParams, guardCode, preview, document, verbosity);

      if (preview)
      {
         return ToolResult.Create(
            result,
            PreviewHints,
            targetParams.Count,
            targetParams.Count);
      }

      // Apply changes
      var solution = _workspaceManager.Solution
         ?? throw new InvalidOperationException("No solution loaded");
      var newSolution = solution.WithDocumentSyntaxRoot(document.Id, formattedRoot);
      if (!_workspaceManager.ApplyChanges(newSolution, cancellationToken))
      {
         throw new ToolException(
            ErrorCodes.ApplyChangesFailed,
            "Failed to apply changes to workspace");
      }

      result = result with { Applied = true, Note = "Guard clauses inserted at the start of the method body" };

      return ToolResult.Create(
         result,
         AppliedHints,
         targetParams.Count,
         targetParams.Count);
   }

   private static readonly string[] SupportedSyntaxTypes = ["method declarations", "accessor declarations", "local functions"];

   /// <summary>
   /// Extracts the block body from various syntax node types.
   /// Throws if the syntax type is not supported.
   /// </summary>
   private static BlockSyntax? GetBody(SyntaxNode syntax) => syntax switch
   {
      BaseMethodDeclarationSyntax baseMethod => baseMethod.Body,
      AccessorDeclarationSyntax accessor => accessor.Body,
      LocalFunctionStatementSyntax localFunc => localFunc.Body,
      _ => throw new ToolException(
         ErrorCodes.NotSupported,
         $"Unsupported syntax type: {syntax.GetType().Name}",
         $"Supported types: {string.Join(", ", SupportedSyntaxTypes)}")
   };

   /// <summary>
   /// Creates a new syntax node with the updated body.
   /// </summary>
   private static SyntaxNode WithBody(SyntaxNode syntax, BlockSyntax newBody) => syntax switch
   {
      BaseMethodDeclarationSyntax baseMethod => baseMethod.WithBody(newBody),
      AccessorDeclarationSyntax accessor => accessor.WithBody(newBody),
      LocalFunctionStatementSyntax localFunc => localFunc.WithBody(newBody),
      _ => throw new ToolException(
         ErrorCodes.NotSupported,
         $"Unsupported syntax type: {syntax.GetType().Name}",
         $"Supported types: {string.Join(", ", SupportedSyntaxTypes)}")
   };

   private AddNullChecksResponse BuildResult(
      IMethodSymbol methodSymbol,
      List<IParameterSymbol> targetParams,
      string guardCode,
      bool preview,
      Document document,
      ToolResultVerbosity verbosity)
   {
      return new AddNullChecksResponse
      {
         MethodName = methodSymbol.Name,
         ParameterCount = targetParams.Count,
         Parameters = verbosity == ToolResultVerbosity.Summary
            ? null  // Don't include parameters in summary mode
            : targetParams.Select(p => new NullCheckParameterInfo { Name = p.Name, Type = p.Type.ToDisplayString() }).ToList(),
         GuardCode = verbosity == ToolResultVerbosity.Full ? guardCode : null,  // Only include guard code in full mode
         Preview = preview,
         Applied = false,
         FilePath = _workspaceManager.FormatPath(document.FilePath),
         Note = preview ? "Guard clauses will be inserted at the start of the method body" : null
      };
   }

   private static bool HasThrowIfNullMethod(Compilation compilation)
   {
      var argumentNullExceptionType = compilation.GetTypeByMetadataName("System.ArgumentNullException");
      if (argumentNullExceptionType == null)
         return false;

      return argumentNullExceptionType.GetMembers("ThrowIfNull")
         .OfType<IMethodSymbol>()
         .Any(m => m.IsStatic && m.Parameters.Length >= 1);
   }

#pragma warning disable CA1859 // Use concrete types when possible for improved performance - intentionally returns base type for polymorphic use
   private static StatementSyntax CreateThrowIfNullStatement(string parameterName)
   {
      return SyntaxFactory.ExpressionStatement(
         SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
               SyntaxKind.SimpleMemberAccessExpression,
               SyntaxFactory.MemberAccessExpression(
                  SyntaxKind.SimpleMemberAccessExpression,
                  SyntaxFactory.IdentifierName("System"),
                  SyntaxFactory.IdentifierName("ArgumentNullException")),
               SyntaxFactory.IdentifierName("ThrowIfNull")))
         .WithArgumentList(
            SyntaxFactory.ArgumentList(
               SyntaxFactory.SingletonSeparatedList(
                  SyntaxFactory.Argument(
                     SyntaxFactory.IdentifierName(parameterName))))));
   }

   private static StatementSyntax CreateLegacyNullCheckStatement(string parameterName)
   {
      return SyntaxFactory.IfStatement(
         SyntaxFactory.BinaryExpression(
            SyntaxKind.EqualsExpression,
            SyntaxFactory.IdentifierName(parameterName),
            SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)),
         SyntaxFactory.ThrowStatement(
            SyntaxFactory.ObjectCreationExpression(
               SyntaxFactory.QualifiedName(
                  SyntaxFactory.IdentifierName("System"),
                  SyntaxFactory.IdentifierName("ArgumentNullException")))
            .WithArgumentList(
               SyntaxFactory.ArgumentList(
                  SyntaxFactory.SingletonSeparatedList(
                     SyntaxFactory.Argument(
                        SyntaxFactory.InvocationExpression(
                           SyntaxFactory.IdentifierName("nameof"))
                        .WithArgumentList(
                           SyntaxFactory.ArgumentList(
                              SyntaxFactory.SingletonSeparatedList(
                                 SyntaxFactory.Argument(
                                    SyntaxFactory.IdentifierName(parameterName)))))))))));
   }
#pragma warning restore CA1859
}
