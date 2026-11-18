using System.ComponentModel;
using Microsoft.CodeAnalysis;

namespace DotLens.Infrastructure.SymbolLookup;

/// <summary>
/// Base result type for symbol lookups. Polymorphic to distinguish
/// source symbols (editable) from metadata symbols (external references).
/// </summary>
public abstract record SymbolLookupResult<TSymbol> where TSymbol : ISymbol
{
   /// <summary>
   /// The resolved symbol.
   /// </summary>
   public required TSymbol Symbol { get; init; }

   /// <summary>
   /// The compilation the lookup found the symbol in.
   /// </summary>
   public required Compilation Compilation { get; init; }
}

/// <summary>
/// Result for a symbol defined in solution source code.
/// These symbols can be edited, refactored, and navigated to.
/// </summary>
[Description("Symbol defined in solution source code")]
public record SourceSymbolResult<TSymbol> : SymbolLookupResult<TSymbol>
   where TSymbol : ISymbol
{
   /// <summary>
   /// The document containing the symbol definition.
   /// </summary>
   public required Document Document { get; init; }

   /// <summary>
   /// The syntax node for the symbol, if requested.
   /// </summary>
   public SyntaxNode? Syntax { get; init; }
}

/// <summary>
/// Result for a symbol defined in an external reference (NuGet package, framework assembly).
/// These symbols can be inspected but not edited.
/// </summary>
[Description("Symbol defined in external reference (NuGet, framework)")]
public record MetadataSymbolResult<TSymbol> : SymbolLookupResult<TSymbol>
   where TSymbol : ISymbol
{
   /// <summary>
   /// The name of the assembly containing the symbol.
   /// </summary>
   public required string AssemblyName { get; init; }
}
