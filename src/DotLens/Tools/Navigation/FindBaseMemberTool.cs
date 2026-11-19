using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Navigation;

#region Response Types

/// <summary>
/// Result from FindBaseMember.
/// </summary>
public record FindBaseMemberResponse
{
   /// <summary>The member being analyzed.</summary>
   public required SymbolResponse Member { get; init; }

   /// <summary>Message when no base members are found.</summary>
   public string? Message { get; init; }

   /// <summary>The overridden base class member, if any.</summary>
   public SymbolResponse? OverriddenMember { get; init; }

   /// <summary>Explicit interface implementations (null at summary verbosity).</summary>
   public IReadOnlyList<SymbolResponse>? ExplicitInterfaceImplementations { get; init; }

   /// <summary>Implicit interface implementations (null at summary verbosity).</summary>
   public IReadOnlyList<SymbolResponse>? ImplicitInterfaceImplementations { get; init; }

   /// <summary>Count of explicit interface implementations (always populated, even at summary verbosity).</summary>
   public int ExplicitInterfaceCount { get; init; }

   /// <summary>Count of implicit interface implementations (always populated, even at summary verbosity).</summary>
   public int ImplicitInterfaceCount { get; init; }
}

#endregion

[McpServerToolType]
[DotLensWorkerTool]
public class FindBaseMemberTool
{
   private static readonly string[] NoBaseMemberSuggestedTools =
   [
      "find_overrides to see if this member has any overrides",
      "get_type_hierarchy to see the inheritance tree"
   ];

   private readonly IReadOnlyWorkspace _workspace;
   private readonly SymbolLookupService _symbolLookup;

   public FindBaseMemberTool(IReadOnlyWorkspace workspace, SymbolLookupService symbolLookup)
   {
      _workspace = workspace;
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "find_base_member", ReadOnly = true, OpenWorld = false)]
   [Description("Find the base class or interface member that a method/property/event overrides or implements.")]
   public async Task<ToolResult<FindBaseMemberResponse>> FindBaseMemberAsync(
      [Description("Member to find base declaration for.")] SymbolLookup target,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Full,
      CancellationToken cancellationToken = default)
   {
      var solution = _workspace.EnsureSolutionLoaded();

      // Find the symbol - must be a method, property, or event
      var lookupResult = await _symbolLookup.FindSingleAsync<ISymbol>(target, s => s is IMethodSymbol or IPropertySymbol or IEventSymbol, cancellationToken);

      var symbol = lookupResult.Symbol;

      // Get overridden member and explicit implementations using extension methods
      var overridden = symbol.GetOverriddenMember();
      var explicitInterfaceImpls = symbol.GetExplicitInterfaceImplementations();

      SymbolResponse? overriddenMember = overridden != null
         ? await CreateSymbolResponseAsync(overridden, solution, verbosity, cancellationToken)
         : null;

      var explicitImplementations = new List<SymbolResponse>();
      var implicitImplementations = new List<SymbolResponse>();

      if (verbosity != ToolResultVerbosity.Summary)
      {
         // Collect explicit interface implementations
         foreach (var impl in explicitInterfaceImpls)
         {
            explicitImplementations.Add(await CreateSymbolResponseAsync(impl, solution, verbosity, cancellationToken));
         }

         // Find implicit interface implementations
         if (symbol.ContainingType != null)
         {
            var implementedMembers = await SymbolFinder.FindImplementedInterfaceMembersAsync(symbol, solution, cancellationToken: cancellationToken);

            foreach (var impl in implementedMembers.Where(m => m.Kind == symbol.Kind))
            {
               if (explicitInterfaceImpls.Contains(impl, SymbolEqualityComparer.Default))
                  continue;

               implicitImplementations.Add(await CreateSymbolResponseAsync(impl, solution, verbosity, cancellationToken));
            }
         }
      }

      // Compute counts first - for summary verbosity, implementations aren't collected so we count separately
      int explicitCount = explicitInterfaceImpls.Length;
      int implicitCount = verbosity == ToolResultVerbosity.Summary && symbol.ContainingType != null
         ? await CountImplicitImplementationsAsync(symbol, solution, cancellationToken)
         : implicitImplementations.Count;

      var hasBaseMembers = overriddenMember != null ||
                           explicitCount > 0 ||
                           implicitCount > 0;

      var memberKind = symbol.ToSymbolKindDto();

      if (!hasBaseMembers)
      {
         return ToolResult.Create(
            new FindBaseMemberResponse
            {
               Member = await CreateSymbolResponseAsync(symbol, solution, verbosity, cancellationToken),
               Message = $"This {memberKind.ToString().ToLowerInvariant()} does not override or implement any base member",
               ExplicitInterfaceCount = 0,
               ImplicitInterfaceCount = 0
            },
            suggestedNextTools: NoBaseMemberSuggestedTools);
      }

      // Build context-aware suggestions
      var suggestions = new List<string>();
      if (overriddenMember != null)
      {
         suggestions.Add("find_overrides to see all overrides of the base member");
      }
      suggestions.Add("find_callers to see where this member is called");
      suggestions.Add(memberKind == SymbolKindDto.Method
         ? "get_method_source to see the implementation"
         : "get_type_members to see the member details");

      return ToolResult.Create(
         new FindBaseMemberResponse
         {
            Member = await CreateSymbolResponseAsync(symbol, solution, verbosity, cancellationToken),
            OverriddenMember = overriddenMember,
            ExplicitInterfaceImplementations = verbosity == ToolResultVerbosity.Summary ? null : explicitImplementations,
            ImplicitInterfaceImplementations = verbosity == ToolResultVerbosity.Summary ? null : implicitImplementations,
            ExplicitInterfaceCount = explicitCount,
            ImplicitInterfaceCount = implicitCount
         },
         suggestedNextTools: suggestions.ToArray());
   }

   private Task<SymbolResponse> CreateSymbolResponseAsync(
      ISymbol symbol, Solution solution, ToolResultVerbosity verbosity, CancellationToken cancellationToken)
   {
      return RoslynHelpers.CreateSymbolResponseAsync(
         symbol, verbosity, solution, _workspace.SolutionPath, _workspace.UseAbsolutePaths, cancellationToken);
   }

   private static async Task<int> CountImplicitImplementationsAsync(ISymbol symbol, Solution solution, CancellationToken cancellationToken)
   {
      var implementedMembers = await SymbolFinder.FindImplementedInterfaceMembersAsync(
         symbol, solution, cancellationToken: cancellationToken);

      var explicitImpls = symbol.GetExplicitInterfaceImplementations();

      return implementedMembers.Count(m =>
         m.Kind == symbol.Kind && !explicitImpls.Contains(m, SymbolEqualityComparer.Default));
   }
}
