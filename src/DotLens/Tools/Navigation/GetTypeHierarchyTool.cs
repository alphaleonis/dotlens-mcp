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
/// Result from GetTypeHierarchy.
/// </summary>
public record GetTypeHierarchyResponse
{
   /// <summary>Fully qualified name of the analyzed type.</summary>
   public required string TypeName { get; init; }

   /// <summary>Number of base types in the inheritance chain (excludes System.Object).</summary>
   public int BaseTypeCount { get; init; }

   /// <summary>Number of implemented interfaces (includes inherited interfaces).</summary>
   public int InterfaceCount { get; init; }

   /// <summary>Number of derived types (null if includeDerived=false).</summary>
   public int? DerivedTypeCount { get; init; }

   /// <summary>Base type chain from immediate parent to root (null at summary verbosity).</summary>
   public IReadOnlyList<SymbolResponse>? BaseTypes { get; init; }

   /// <summary>All implemented interfaces (null at summary verbosity).</summary>
   public IReadOnlyList<SymbolResponse>? Interfaces { get; init; }

   /// <summary>Derived types (null at summary verbosity or if includeDerived=false).</summary>
   public IReadOnlyList<SymbolResponse>? DerivedTypes { get; init; }
}

#endregion

[McpServerToolType]
[DotLensWorkerTool]
public class GetTypeHierarchyTool
{
   private readonly IReadOnlyWorkspace _workspace;
   private readonly SymbolLookupService _symbolLookup;

   public GetTypeHierarchyTool(IReadOnlyWorkspace workspace, SymbolLookupService symbolLookup)
   {
      _workspace = workspace;
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "get_type_hierarchy", ReadOnly = true, OpenWorld = false)]
   [Description("Get inheritance information for a type: base types, implemented interfaces, and optionally derived types.")]
   public async Task<ToolResult<GetTypeHierarchyResponse>> GetTypeHierarchyAsync(
      [Description("Type to analyze.")] SymbolLookup target,
      [Description("Include derived types in results. Set to false for base types only (faster).")]
      bool includeDerived = true,
      [Description("Maximum number of derived types to return.")]
      [PositiveInteger]
      int maxDerivedTypes = 50,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      CancellationToken cancellationToken = default)
   {
      var solution = _workspace.EnsureSolutionLoaded();

      // Find the type symbol
      var lookupResult = await _symbolLookup.FindSingleAsync<INamedTypeSymbol>(target, filter: null, cancellationToken);
      var typeSymbol = lookupResult.Symbol;

      // Get base types (excluding System.Object)
      var baseTypes = new List<SymbolResponse>();
      int baseTypeCount = 0;
      var currentBase = typeSymbol.BaseType;
      while (currentBase != null && currentBase.SpecialType != SpecialType.System_Object)
      {
         baseTypeCount++;
         if (verbosity != ToolResultVerbosity.Summary)
         {
            baseTypes.Add(CreateSymbolResponse(currentBase, verbosity));
         }
         currentBase = currentBase.BaseType;
      }

      // Get interfaces
      var interfaces = new List<SymbolResponse>();
      int interfaceCount = typeSymbol.AllInterfaces.Length;
      if (verbosity != ToolResultVerbosity.Summary)
      {
         foreach (var iface in typeSymbol.AllInterfaces)
         {
            interfaces.Add(CreateSymbolResponse(iface, verbosity));
         }
      }

      // Get derived types (only if requested)
      // For interfaces, use FindImplementationsAsync; for classes, use FindDerivedClassesAsync
      var derivedTypes = new List<SymbolResponse>();
      int totalDerived = 0;
      if (includeDerived)
      {
         List<INamedTypeSymbol> allDerived;
         if (typeSymbol.TypeKind == TypeKind.Interface)
         {
            var implementations = await SymbolFinder.FindImplementationsAsync(
               typeSymbol, solution, transitive: false, cancellationToken: cancellationToken);
            allDerived = implementations.OfType<INamedTypeSymbol>().ToList();
         }
         else
         {
            var derivedClasses = await SymbolFinder.FindDerivedClassesAsync(
               typeSymbol, solution, transitive: false, cancellationToken: cancellationToken);
            allDerived = derivedClasses.ToList();
         }
         totalDerived = allDerived.Count;

         if (verbosity != ToolResultVerbosity.Summary)
         {
            foreach (var derived in allDerived.Take(maxDerivedTypes))
            {
               derivedTypes.Add(CreateSymbolResponse(derived, verbosity));
            }
         }
      }

      // Build context-aware suggestions
      var suggestions = new List<string>
      {
         $"get_type_members for {RoslynHelpers.GetName(typeSymbol)} to see all members"
      };

      if (typeSymbol.TypeKind == TypeKind.Interface || interfaces.Count > 0)
      {
         suggestions.Add("find_implementations for interface implementations");
      }

      if (totalDerived > maxDerivedTypes)
      {
         suggestions.Add($"get_type_hierarchy with higher maxDerivedTypes to see all {totalDerived} derived types");
      }

      return ToolResult.Create(
         new GetTypeHierarchyResponse
         {
            TypeName = typeSymbol.ToDisplayString(),
            BaseTypeCount = baseTypeCount,
            InterfaceCount = interfaceCount,
            DerivedTypeCount = includeDerived ? totalDerived : null,
            BaseTypes = verbosity == ToolResultVerbosity.Summary ? null : baseTypes,
            Interfaces = verbosity == ToolResultVerbosity.Summary ? null : interfaces,
            DerivedTypes = verbosity == ToolResultVerbosity.Summary || !includeDerived ? null : derivedTypes
         },
         suggestedNextTools: suggestions.ToArray(),
         totalCount: includeDerived ? totalDerived : null,
         returnedCount: derivedTypes.Count);
   }

   private SymbolResponse CreateSymbolResponse(INamedTypeSymbol symbol, ToolResultVerbosity verbosity)
   {
      return RoslynHelpers.CreateSymbolResponse(symbol, verbosity, _workspace.SolutionPath, _workspace.UseAbsolutePaths);
   }
}
