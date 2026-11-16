using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Navigation;

[McpServerToolType]
[DotLensWorkerTool]
public class GetSymbolInfoTool
{
   private readonly IReadOnlyWorkspace _workspace;
   private readonly SymbolLookupService _symbolLookup;

   public GetSymbolInfoTool(IReadOnlyWorkspace workspace, SymbolLookupService symbolLookup)
   {
      _workspace = workspace;
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "get_symbol_info", ReadOnly = true, OpenWorld = false)]
   [Description("Get detailed semantic information about a symbol: kind, type, namespace, documentation, location.")]
   public async Task<ToolResult<SymbolResponse>> GetSymbolInfoAsync(SymbolLookup target,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Full,
      CancellationToken cancellationToken = default)
   {
      _workspace.EnsureSolutionLoaded();

      var lookupResult = await _symbolLookup.FindSingleAsync<ISymbol>(target, filter: null, cancellationToken);
      var symbol = lookupResult.Symbol;

      var response = RoslynHelpers.CreateSymbolResponse(
         symbol,
         verbosity,
         _workspace.SolutionPath,
         _workspace.UseAbsolutePaths);

      // Build context-aware suggestions
      var symbolName = RoslynHelpers.GetName(symbol);
      var suggestions = new List<string>
      {
         $"find_references to see all usages of {symbolName}"
      };

      if (symbol is INamedTypeSymbol)
      {
         suggestions.Add($"get_type_members for {symbolName} to see members");
         suggestions.Add($"get_type_hierarchy to see inheritance chain");
      }
      else if (symbol is IMethodSymbol or IPropertySymbol or IEventSymbol)
      {
         suggestions.Add($"find_callers to see methods that call {symbolName}");
         suggestions.Add($"find_base_member to see what {symbolName} overrides/implements");
      }

      return ToolResult.Create(response, suggestedNextTools: suggestions.ToArray());
   }
}
