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
/// Result from FindOverrides.
/// </summary>
public record FindOverridesResponse
{
   /// <summary>The root virtual/abstract member that defines the override chain.</summary>
   public required SymbolResponse BaseMember { get; init; }

   /// <summary>Total number of overrides found.</summary>
   public required int TotalOverrides { get; init; }

   /// <summary>List of overriding members (null at summary verbosity).</summary>
   public IReadOnlyList<SymbolResponse>? Overrides { get; init; }
}

#endregion

[McpServerToolType]
[DotLensWorkerTool]
public class FindOverridesTool
{
   private readonly IReadOnlyWorkspace _workspace;
   private readonly SymbolLookupService _symbolLookup;

   public FindOverridesTool(IReadOnlyWorkspace workspace, SymbolLookupService symbolLookup)
   {
      _workspace = workspace;
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "find_overrides", ReadOnly = true, OpenWorld = false)]
   [Description("Find all overrides of a virtual/abstract method, property, or event.")]
   public async Task<ToolResult<FindOverridesResponse>> FindOverridesAsync(
      [Description("Virtual or abstract member to find overrides for.")] SymbolLookup target,
      [Description("Maximum number of overrides to return.")]
      [PositiveInteger]
      int maxResults = 50,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      CancellationToken cancellationToken = default)
   {
      var solution = _workspace.EnsureSolutionLoaded();

      // Find the symbol - must be a regular method, property, or event (not constructors)
      var lookupResult = await _symbolLookup.FindSingleAsync<ISymbol>(
         target,
         s => s switch
         {
            IMethodSymbol m => m.MethodKind == MethodKind.Ordinary,
            IPropertySymbol => true,
            IEventSymbol => true,
            _ => false
         },
         cancellationToken);

      var symbol = lookupResult.Symbol;

      // Check if the symbol is overridable or an override
      if (!symbol.IsVirtual && !symbol.IsAbstract && !symbol.IsOverride)
      {
         var kind = symbol.ToSymbolKindDto().ToString().ToLowerInvariant();
         throw new ToolException(
            ErrorCodes.InvalidParameter,
            $"The {kind} '{symbol.Name}' is not virtual, abstract, or an override",
            $"Only virtual, abstract, or override members can have overrides.");
      }

      // Walk up override chain to find the root base member
      var baseMember = GetRootBaseMember(symbol);

      // Find all overrides of the base member
      var overrides = await SymbolFinder.FindOverridesAsync(baseMember, solution, cancellationToken: cancellationToken);
      var allOverrides = overrides.ToList();
      var totalOverrides = allOverrides.Count;

      // Build override list (respecting maxResults)
      var overrideResponses = new List<SymbolResponse>();
      if (verbosity != ToolResultVerbosity.Summary)
      {
         foreach (var overrideSymbol in allOverrides.Take(maxResults))
         {
            overrideResponses.Add(CreateSymbolResponse(overrideSymbol, verbosity));
         }
      }

      // Build context-aware suggestions
      var memberKind = symbol.ToSymbolKindDto();
      var suggestions = new List<string>
      {
         "go_to_definition to navigate to a specific override"
      };

      if (memberKind == SymbolKindDto.Method)
         suggestions.Add("get_method_source to see the implementation");
      else
         suggestions.Add("get_type_members to see member details");

      if (totalOverrides > maxResults)
         suggestions.Add($"find_overrides with higher maxResults to see all {totalOverrides} overrides");

      return ToolResult.Create(
         new FindOverridesResponse
         {
            BaseMember = CreateSymbolResponse(baseMember, verbosity),
            TotalOverrides = totalOverrides,
            Overrides = verbosity == ToolResultVerbosity.Summary ? null : overrideResponses
         },
         suggestedNextTools: suggestions.ToArray(),
         totalCount: totalOverrides,
         returnedCount: overrideResponses.Count);
   }

   private static ISymbol GetRootBaseMember(ISymbol symbol)
   {
      // Walk up override chain to find the root virtual/abstract member
      var current = symbol;
      var overridden = current.GetOverriddenMember();

      while (overridden != null)
      {
         current = overridden;
         overridden = current.GetOverriddenMember();
      }

      return current;
   }

   private SymbolResponse CreateSymbolResponse(ISymbol symbol, ToolResultVerbosity verbosity)
   {
      return RoslynHelpers.CreateSymbolResponse(symbol, verbosity, _workspace.SolutionPath, _workspace.UseAbsolutePaths);
   }
}
