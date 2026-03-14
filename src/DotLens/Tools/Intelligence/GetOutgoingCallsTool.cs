using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
namespace DotLens.Tools.Intelligence;

#region Response Types

/// <summary>
/// Response for GetOutgoingCallsTool.
/// </summary>
public record OutgoingCallsResponse
{
   public required string MethodName { get; init; }
   public string? ContainingType { get; init; }
   public required int OutgoingCallCount { get; init; }
   /// <summary>List of outgoing calls as SymbolResponse. Null when verbosity is Summary.</summary>
   public IReadOnlyList<SymbolResponse>? OutgoingCalls { get; init; }
}

#endregion

[McpServerToolType]
[DotLensWorkerTool]
public class GetOutgoingCallsTool
{
   private readonly IReadOnlyWorkspace _workspace;
   private readonly SymbolLookupService _symbolLookup;

   public GetOutgoingCallsTool(IReadOnlyWorkspace workspace, SymbolLookupService symbolLookup)
   {
      _workspace = workspace;
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "get_outgoing_calls", ReadOnly = true, Idempotent = true, OpenWorld = false)]
   [Description("Get all methods and properties that a method calls.")]
   public async Task<ToolResult<OutgoingCallsResponse>> GetOutgoingCallsAsync(
      [Description("Method to analyze.")] SymbolLookup target,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      [Description("Maximum number of outgoing calls to return.")]
      [PositiveInteger]
      int maxResults = 100,
      CancellationToken cancellationToken = default)
   {
      // Find the method using SymbolLookupService
      SourceSymbolResult<IMethodSymbol> lookupResult;

      if (target is LookupByPosition positionLookup)
      {
         // For position-based lookup, find the enclosing method (position may be inside method body)
         lookupResult = await _symbolLookup.FindEnclosingSymbolAsync<IMethodSymbol>(positionLookup, cancellationToken);
      }
      else
      {
         // For name-based lookup, find the method directly
         lookupResult = await _symbolLookup.FindSingleSourceSymbolAsync<IMethodSymbol>(target, cancellationToken);
      }

      var methodSymbol = lookupResult.Symbol;
      var solution = _workspace.EnsureSolutionLoaded();

      // Delegate to shared helper for outgoing call collection
      // Pass int.MaxValue to get all calls, we'll apply maxResults after
      var distinctSymbols = await IntelligenceHelpers.GetOutgoingCallsForMethodAsync(
         methodSymbol, solution, int.MaxValue, cancellationToken);

      var totalCount = distinctSymbols.Count;

      // Apply maxResults limit and convert to SymbolResponse
      var distinctCalls = distinctSymbols
         .Take(maxResults)
         .Select(s => RoslynHelpers.CreateSymbolResponse(s, verbosity, _workspace.SolutionPath, _workspace.UseAbsolutePaths))
         .ToList();

      // Build context-aware suggestions
      var suggestions = new List<string>
      {
         $"find_callers to see who calls {methodSymbol.Name}",
         $"get_method_source to see the full implementation"
      };
      if (methodSymbol.ContainingType != null)
         suggestions.Add($"get_type_members for {methodSymbol.ContainingType.Name}");

      return ToolResult.Create(
         new OutgoingCallsResponse
         {
            MethodName = methodSymbol.Name,
            ContainingType = methodSymbol.ContainingType?.Name,
            OutgoingCallCount = totalCount,
            OutgoingCalls = verbosity == ToolResultVerbosity.Summary ? null : distinctCalls
         },
         suggestedNextTools: suggestions.ToArray(),
         totalCount: totalCount,
         returnedCount: verbosity == ToolResultVerbosity.Summary ? 0 : distinctCalls.Count);
   }
}
