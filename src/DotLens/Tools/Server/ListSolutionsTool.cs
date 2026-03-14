using System.ComponentModel;
using DotLens.Infrastructure;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Server;

#region Response Types

/// <summary>
/// A solution in the list.
/// </summary>
public record SolutionListItem
{
   [Description("Absolute path to the solution file.")]
   public required string Path { get; init; }

   [Description("Solution filename.")]
   public required string Name { get; init; }

   [Description("Whether this is the currently active solution.")]
   public required bool IsActive { get; init; }

   [Description("When this solution was loaded (UTC).")]
   public required DateTime LoadedAt { get; init; }
}

/// <summary>
/// Result from list solutions - unified type for all states (empty and non-empty).
/// </summary>
public record ListSolutionsResponse
{
   [Description("Status message when no solutions are loaded.")]
   public string? Message { get; init; }

   [Description("Number of solutions currently loaded.")]
   public required int TotalSolutions { get; init; }

   [Description("Path to the currently active solution.")]
   public string? ActiveSolution { get; init; }

   [Description("List of all loaded solutions.")]
   public IReadOnlyList<SolutionListItem> Solutions { get; init; } = [];
}

#endregion

[McpServerToolType]
[DotLensServerTool]
public class ListSolutionsTool
{
   private readonly IMultiWorkerManager _workerManager;

   public ListSolutionsTool(IMultiWorkerManager workerManager)
   {
      _workerManager = workerManager;
   }

   [McpServerTool(Name = "list_solutions", ReadOnly = true, Idempotent = true, OpenWorld = false)]
   [Description("List all currently loaded solutions and which one is active.")]
   public ToolResult<ListSolutionsResponse> ListSolutions()
   {
      var workers = _workerManager.Workers;
      var activePath = _workerManager.ActiveSolutionPath;

      if (workers.Count == 0)
      {
         return ToolResult.Create(
            new ListSolutionsResponse
            {
               Message = "No solutions loaded",
               TotalSolutions = 0,
               ActiveSolution = null
            });
      }

      var solutions = workers.Select(w => new SolutionListItem
      {
         Path = w.Key,
         Name = Path.GetFileName(w.Key),
         IsActive = w.Key == activePath,
         LoadedAt = w.Value.LoadedAt
      }).ToList();

      return ToolResult.Create(
         new ListSolutionsResponse
         {
            TotalSolutions = workers.Count,
            ActiveSolution = activePath,
            Solutions = solutions
         });
   }
}
