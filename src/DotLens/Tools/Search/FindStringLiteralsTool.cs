using System.ComponentModel;
using DotLens.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelContextProtocol.Server;
namespace DotLens.Tools.Search;

/// <summary>
/// Find string literals across the solution.
/// </summary>
[McpServerToolType]
[DotLensWorkerTool]
public class FindStringLiteralsTool
{
   private static readonly string[] SuggestedNextTools =
   [
      "find_references to see usages of a value",
      "search_symbols to find related constants"
   ];

   private readonly IReadOnlyWorkspace _workspace;

   public FindStringLiteralsTool(IReadOnlyWorkspace workspace)
   {
      _workspace = workspace;
   }

   /// <summary>
   /// Find string literals across the solution.
   /// </summary>
   [McpServerTool(Name = "find_string_literals", ReadOnly = true, OpenWorld = false)]
   [Description("Find string literals across the solution. Only matches regular string literals, not interpolated ($\"...\") or verbatim interpolated strings.")]
   public async Task<ToolResult<IReadOnlyList<StringLiteralInfo>>> FindStringLiteralsAsync(
      [Description("Filter to strings containing this text")]
      string? searchText = null,
      [Description("Require exact match (default: false)")]
      bool exactMatch = false,
      [Description("Limit to specific project")]
      string? projectName = null,
      [Description("Maximum results to return.")]
      [PositiveInteger]
      int maxResults = 100,
      CancellationToken cancellationToken = default)
   {
      var solution = _workspace.EnsureSolutionLoaded();
      var results = new List<StringLiteralInfo>();

      IEnumerable<Project> projects = solution.Projects;
      if (!string.IsNullOrEmpty(projectName))
      {
         projects = projects.Where(p =>
            p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase));
      }

      foreach (var project in projects)
      {
         cancellationToken.ThrowIfCancellationRequested();
         var compilation = await project.GetCompilationAsync(cancellationToken);
         if (compilation == null)
            continue;

         foreach (var syntaxTree in compilation.SyntaxTrees)
         {
            var root = await syntaxTree.GetRootAsync(cancellationToken);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);

            // Find all string literal expressions
            var stringLiterals = root.DescendantNodes()
               .OfType<LiteralExpressionSyntax>()
               .Where(l => l.IsKind(SyntaxKind.StringLiteralExpression));

            foreach (var literal in stringLiterals)
            {
               var value = literal.Token.ValueText;

               // Apply search filter
               if (!string.IsNullOrEmpty(searchText))
               {
                  if (exactMatch)
                  {
                     if (!value.Equals(searchText, StringComparison.OrdinalIgnoreCase))
                        continue;
                  }
                  else
                  {
                     if (!value.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                        continue;
                  }
               }

               // Get containing member
               var containingNode = literal.Parent;
               string? containingMember = null;
               while (containingNode != null)
               {
                  var symbol = semanticModel.GetDeclaredSymbol(containingNode, cancellationToken);
                  if (symbol is IMethodSymbol or IPropertySymbol or IFieldSymbol)
                  {
                     containingMember = symbol.Name;
                     break;
                  }
                  containingNode = containingNode.Parent;
               }

               var lineSpan = literal.GetLocation().GetLineSpan();
               results.Add(new StringLiteralInfo
               {
                  Value = value,
                  FilePath = _workspace.FormatPath(lineSpan.Path),
                  Line = lineSpan.StartLinePosition.Line + 1,
                  Column = lineSpan.StartLinePosition.Character + 1,
                  ContainingMember = containingMember
               });

               if (results.Count >= maxResults)
                  break;
            }

            if (results.Count >= maxResults)
               break;
         }

         if (results.Count >= maxResults)
            break;
      }

      return ToolResult.Create(
         (IReadOnlyList<StringLiteralInfo>)results,
         suggestedNextTools: SuggestedNextTools);
   }
}

/// <summary>
/// Information about a string literal match.
/// </summary>
public record StringLiteralInfo
{
   [Description("The string literal value (unescaped).")]
   public required string Value { get; init; }

   [Description("File path containing the string literal.")]
   public required string FilePath { get; init; }

   [Description("Line number where the literal starts.")]
   [PositiveInteger]
   public required int Line { get; init; }

   [Description("Column number where the literal starts.")]
   [PositiveInteger]
   public required int Column { get; init; }

   [Description("Name of the containing method, property, or field (if any).")]
   public string? ContainingMember { get; init; }
}
