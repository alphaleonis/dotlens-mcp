using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;
namespace DotLens.Tools.Navigation;

#region Response Types

/// <summary>
/// Result from FindCallers.
/// </summary>
public record FindCallersResponse
{
   /// <summary>The method or property being queried.</summary>
   public required SymbolResponse Symbol { get; init; }

   /// <summary>Total number of call sites found.</summary>
   public required int TotalCallers { get; init; }

   /// <summary>List of call sites (null at summary verbosity).</summary>
   public IReadOnlyList<CallerInfo>? Callers { get; init; }
}

/// <summary>
/// Info about a call site.
/// </summary>
public record CallerInfo
{
   /// <summary>The method or property containing the call.</summary>
   public required SymbolResponse CallingSymbol { get; init; }

   /// <summary>Location of the call within the calling symbol.</summary>
   public required CallerLocationInfo Location { get; init; }
}

#endregion

[McpServerToolType]
[DotLensWorkerTool]
public class FindCallersTool
{
   private readonly IReadOnlyWorkspace _workspace;
   private readonly SymbolLookupService _symbolLookup;

   public FindCallersTool(IReadOnlyWorkspace workspace, SymbolLookupService symbolLookup)
   {
      _workspace = workspace;
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "find_callers", ReadOnly = true, Idempotent = true, OpenWorld = false)]
   [Description("Find all methods/properties that call or reference a specific symbol.")]
   public async Task<ToolResult<FindCallersResponse>> FindCallersAsync(
      [Description("Method or property to find callers for.")] SymbolLookup target,
      [Description("Maximum number of call sites to return.")]
      [PositiveInteger]
      int maxResults = 50,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      CancellationToken cancellationToken = default)
   {
      var solution = _workspace.EnsureSolutionLoaded();

      // Find the symbol - must be a method or property
      var lookupResult = await _symbolLookup.FindSingleAsync<ISymbol>(
         target,
         s => s is IMethodSymbol or IPropertySymbol,
         cancellationToken);

      var symbol = lookupResult.Symbol;

      // Find callers
      var callers = await SymbolFinder.FindCallersAsync(symbol, solution, cancellationToken);

      // Count total call sites
      var totalCallers = 0;
      foreach (var caller in callers)
      {
         totalCallers += caller.Locations.Count(loc => loc.IsInSource);
      }

      // Build caller list (respecting maxResults and verbosity)
      var callerList = new List<CallerInfo>();
      if (verbosity != ToolResultVerbosity.Summary)
      {
         foreach (var caller in callers)
         {
            var callingSymbol = caller.CallingSymbol;

            foreach (var location in caller.Locations.Where(loc => loc.IsInSource))
            {
               if (callerList.Count >= maxResults)
                  break;

               if (location.SourceTree == null)
                  continue;

               var callerDocument = solution.GetDocument(location.SourceTree);
               if (callerDocument?.FilePath == null)
                  continue;

               var lineSpan = location.GetLineSpan();
               var text = location.SourceTree.GetText(cancellationToken);
               var lineText = text.Lines[lineSpan.StartLinePosition.Line].ToString().Trim();

               callerList.Add(new CallerInfo
               {
                  CallingSymbol = CreateSymbolResponse(callingSymbol, verbosity),
                  Location = new CallerLocationInfo
                  {
                     FilePath = _workspace.FormatPath(callerDocument.FilePath),
                     Line = lineSpan.StartLinePosition.Line + 1,
                     Column = lineSpan.StartLinePosition.Character + 1,
                     LineText = lineText
                  }
               });
            }

            if (callerList.Count >= maxResults)
               break;
         }
      }

      // Build context-aware suggestions
      var suggestions = new List<string>
      {
         $"get_symbol_info for details about {RoslynHelpers.GetName(symbol)}",
         "find_references for all usages (not just calls)",
         "analyze_change_impact to see what would break"
      };

      if (totalCallers > maxResults)
         suggestions.Add($"find_callers with higher maxResults to see all {totalCallers} call sites");

      return ToolResult.Create(
         new FindCallersResponse
         {
            Symbol = CreateSymbolResponse(symbol, verbosity),
            TotalCallers = totalCallers,
            Callers = verbosity == ToolResultVerbosity.Summary ? null : callerList
         },
         suggestedNextTools: suggestions.ToArray(),
         totalCount: totalCallers,
         returnedCount: callerList.Count);
   }

   private SymbolResponse CreateSymbolResponse(ISymbol symbol, ToolResultVerbosity verbosity)
   {
      return RoslynHelpers.CreateSymbolResponse(symbol, verbosity, _workspace.SolutionPath, _workspace.UseAbsolutePaths);
   }
}
