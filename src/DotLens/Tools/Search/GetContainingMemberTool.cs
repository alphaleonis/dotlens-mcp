using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Search;

/// <summary>
/// Get information about the containing method/property/class at a position.
/// </summary>
[McpServerToolType]
[DotLensWorkerTool]
public class GetContainingMemberTool
{
   private readonly IReadOnlyWorkspace _workspace;

   public GetContainingMemberTool(IReadOnlyWorkspace workspace)
   {
      _workspace = workspace;
   }

   [McpServerTool(Name = "get_containing_member", ReadOnly = true, OpenWorld = false)]
   [Description("Get the enclosing method, property, or type at a position. Useful for understanding code context.")]
   public async Task<ToolResult<GetContainingMemberResponse>> GetContainingMemberAsync(
      [Description("Path to source file (absolute or relative to solution directory)")] string filePath,
      [Description("Line number")]
      int line,
      [Description("Column number")]
      int column,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      [Description("Include all enclosing scopes (member → type → namespace) instead of just the immediate container.")]
      bool includeHierarchy = false,
      CancellationToken cancellationToken = default)
   {
      _workspace.EnsureSolutionLoaded();

      var document = await _workspace.GetDocumentAsync(filePath, cancellationToken);

      var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);
      var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

      if (semanticModel == null || syntaxTree == null)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            "Could not get semantic model or syntax tree");
      }

      var position = RoslynHelpers.GetPosition(syntaxTree, line, column);
      var root = await syntaxTree.GetRootAsync(cancellationToken);
      var token = root.FindToken(position);
      var node = token.Parent;

      // Collect enclosing symbols
      var hierarchy = new List<ISymbol>();
      ISymbol? immediateMember = null;

      if (node != null)
      {
         foreach (var n in node.AncestorsAndSelf())
         {
            var declared = semanticModel.GetDeclaredSymbol(n, cancellationToken);
            if (declared == null)
               continue;

            // For immediate member, only accept method/property/type
            if (immediateMember == null && declared is IMethodSymbol or IPropertySymbol or INamedTypeSymbol)
            {
               immediateMember = declared;
            }

            // For hierarchy, collect all meaningful symbols
            if (includeHierarchy && declared is IMethodSymbol or IPropertySymbol or IFieldSymbol or IEventSymbol or INamedTypeSymbol or INamespaceSymbol)
            {
               // Skip global namespace
               if (declared is INamespaceSymbol ns && ns.IsGlobalNamespace)
                  continue;

               hierarchy.Add(declared);
            }
         }
      }

      if (immediateMember == null)
      {
         throw new ToolException(
            ErrorCodes.SymbolNotFound,
            $"No containing member found at {filePath}:{line}:{column}");
      }

      var solution = document.Project.Solution;
      var memberResponse = await CreateSymbolResponseAsync(immediateMember, solution, verbosity, cancellationToken);

      // Build hierarchy responses if requested
      IReadOnlyList<SymbolResponse>? hierarchyResponses = null;
      string? contextPath = null;

      if (includeHierarchy && hierarchy.Count > 0)
      {
         var responses = new List<SymbolResponse>();
         foreach (var s in hierarchy)
            responses.Add(await CreateSymbolResponseAsync(s, solution, verbosity, cancellationToken));
         hierarchyResponses = responses;

         // Build context path like "Namespace.Class.Method"
         var pathParts = hierarchy
            .AsEnumerable()
            .Reverse()
            .Select(s => s.Name)
            .Where(n => !string.IsNullOrEmpty(n));
         contextPath = string.Join(".", pathParts);
      }

      // Build context-aware suggestions
      var suggestions = immediateMember switch
      {
         IMethodSymbol => new[] { "find_callers to see what calls this method", "get_method_source to see the implementation" },
         IPropertySymbol => new[] { "find_references to see usages", "get_type_members to see sibling members" },
         INamedTypeSymbol => new[] { "get_type_members to see all members", "get_type_hierarchy to see inheritance" },
         _ => new[] { "get_symbol_info for more details" }
      };

      return ToolResult.Create(
         new GetContainingMemberResponse
         {
            Member = memberResponse,
            Hierarchy = hierarchyResponses,
            ContextPath = contextPath
         },
         suggestedNextTools: suggestions);
   }

   private Task<SymbolResponse> CreateSymbolResponseAsync(
      ISymbol symbol, Solution solution, ToolResultVerbosity verbosity, CancellationToken cancellationToken)
   {
      return RoslynHelpers.CreateSymbolResponseAsync(
         symbol, verbosity, solution, _workspace.SolutionPath, _workspace.UseAbsolutePaths, cancellationToken);
   }
}

/// <summary>
/// Response for get_containing_member tool.
/// </summary>
public record GetContainingMemberResponse
{
   [Description("The immediate containing member (method, property, or type).")]
   public required SymbolResponse Member { get; init; }

   [Description("Full hierarchy from innermost to outermost scope. Only present when includeHierarchy=true.")]
   public IReadOnlyList<SymbolResponse>? Hierarchy { get; init; }

   [Description("Dot-separated path like 'Namespace.Class.Method'. Only present when includeHierarchy=true.")]
   public string? ContextPath { get; init; }
}
