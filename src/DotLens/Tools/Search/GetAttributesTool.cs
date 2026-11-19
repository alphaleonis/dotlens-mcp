using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
namespace DotLens.Tools.Search;

/// <summary>
/// Find all symbols with specific attributes.
/// </summary>
[McpServerToolType]
[DotLensWorkerTool]
public class GetAttributesTool
{
   private static readonly string[] SuggestedNextTools =
   [
      "get_symbol_info for detailed info about a found symbol",
      "find_references to see all usages"
   ];

   private readonly IReadOnlyWorkspace _workspace;

   public GetAttributesTool(IReadOnlyWorkspace workspace)
   {
      _workspace = workspace;
   }

   /// <summary>
   /// Find all symbols with specific attributes.
   /// </summary>
   [McpServerTool(Name = "get_attributes", ReadOnly = true, OpenWorld = false)]
   [Description("Find all symbols decorated with a specific attribute.")]
   public async Task<ToolResult<IReadOnlyList<SymbolResponse>>> GetAttributesAsync(
      [Description("Attribute name (e.g., 'Obsolete', 'Serializable', 'JsonProperty')")] string attributeName,
      [Description("Search scope. Defaults to entire solution.")]
      SearchScope? scope = null,
      [Description("Maximum results to return.")]
      [PositiveInteger]
      int maxResults = 100,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      CancellationToken cancellationToken = default)
   {
      var solution = _workspace.EnsureSolutionLoaded();

      if (string.IsNullOrWhiteSpace(attributeName))
      {
         throw new ToolException(
            ErrorCodes.InvalidParameter,
            "attributeName is required",
            "Provide an attribute name like 'Obsolete', 'Serializable', or 'JsonProperty'");
      }

      // Default to solution scope
      scope ??= new SolutionScope();

      var results = new List<SymbolResponse>();
      var projects = solution.Projects.ToList();

      // Apply project scope filter
      if (scope is ProjectScope projectScope)
      {
         var matchingProjects = projects.Where(p =>
            p.Name.Equals(projectScope.Name, StringComparison.OrdinalIgnoreCase)).ToList();

         if (matchingProjects.Count == 0)
         {
            throw new ToolException(
               ErrorCodes.InvalidParameter,
               $"Project not found: {projectScope.Name}",
               $"Available projects: {string.Join(", ", solution.Projects.Select(p => p.Name))}");
         }

         projects = matchingProjects;
      }

      foreach (var project in projects)
      {
         cancellationToken.ThrowIfCancellationRequested();
         var compilation = await project.GetCompilationAsync(cancellationToken);
         if (compilation == null)
            continue;

         foreach (var syntaxTree in compilation.SyntaxTrees)
         {
            // Apply file scope filter
            if (scope is FileScope fileScope)
            {
               if (!syntaxTree.FilePath.EndsWith(fileScope.Path, StringComparison.OrdinalIgnoreCase))
                  continue;
            }

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = await syntaxTree.GetRootAsync(cancellationToken);

            foreach (var node in root.DescendantNodes())
            {
               var symbol = semanticModel.GetDeclaredSymbol(node, cancellationToken);
               if (symbol == null)
                  continue;

               if (RoslynHelpers.HasAttribute(symbol, attributeName))
               {
                  results.Add(await RoslynHelpers.CreateSymbolResponseAsync(
                     symbol, verbosity, solution, _workspace.SolutionPath, _workspace.UseAbsolutePaths, cancellationToken));

                  if (results.Count >= maxResults)
                     break;
               }
            }

            if (results.Count >= maxResults)
               break;
         }

         if (results.Count >= maxResults)
            break;
      }

      return ToolResult.Create(
         (IReadOnlyList<SymbolResponse>)results,
         suggestedNextTools: SuggestedNextTools,
         totalCount: results.Count,
         returnedCount: results.Count);
   }
}
