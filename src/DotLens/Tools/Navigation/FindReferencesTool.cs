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
/// Result from FindReferences search.
/// </summary>
public record FindReferencesResponse
{
   /// <summary>The symbol being queried for references.</summary>
   public required SymbolResponse Symbol { get; init; }

   /// <summary>Total number of references found.</summary>
   public required int TotalReferences { get; init; }

   /// <summary>List of references (null at summary verbosity).</summary>
   public IReadOnlyList<ReferenceInfo>? References { get; init; }
}

#endregion

[McpServerToolType]
[DotLensWorkerTool]
public class FindReferencesTool
{
   private readonly IReadOnlyWorkspace _workspace;
   private readonly SymbolLookupService _symbolLookup;

   public FindReferencesTool(IReadOnlyWorkspace workspace, SymbolLookupService symbolLookup)
   {
      _workspace = workspace;
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "find_references", ReadOnly = true, OpenWorld = false)]
   [Description("Find all references to a symbol across the solution.")]
   public async Task<ToolResult<FindReferencesResponse>> FindReferencesAsync(
      [Description("Symbol to find references for.")] SymbolLookup target,
      [Description("Maximum number of references to return.")]
      [PositiveInteger]
      int maxResults = 50,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      CancellationToken cancellationToken = default)
   {
      var solution = _workspace.EnsureSolutionLoaded();

      // Find the symbol using SymbolLookupService
      var lookupResult = await _symbolLookup.FindSingleAsync<ISymbol>(target, filter: null, cancellationToken);
      var symbol = lookupResult.Symbol;

      // Find all references
      var references = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken: cancellationToken);
      var allLocations = references
         .SelectMany(r => r.Locations)
         .Where(loc => loc.Location.IsInSource)
         .ToList();

      var totalReferences = allLocations.Count;

      // Build reference list (respecting maxResults and verbosity)
      List<ReferenceInfo>? referenceList = null;
      if (verbosity != ToolResultVerbosity.Summary)
      {
         referenceList = [];
         foreach (var loc in allLocations.Take(maxResults))
         {
            var refDocument = solution.GetDocument(loc.Document.Id);
            if (refDocument?.FilePath == null)
               continue;

            var refTree = await refDocument.GetSyntaxTreeAsync(cancellationToken);
            if (refTree == null)
               continue;

            var refSpan = loc.Location.SourceSpan;
            var lineSpan = refTree.GetLineSpan(refSpan, cancellationToken);

            var refRoot = await refTree.GetRootAsync(cancellationToken);
            var refNode = refRoot.FindNode(refSpan);
            var kind = ClassifyReference(refNode);

            string? lineText = null;
            if (verbosity == ToolResultVerbosity.Full)
            {
               var text = refTree.GetText(cancellationToken);
               lineText = text.Lines[lineSpan.StartLinePosition.Line].ToString().Trim();
            }

            referenceList.Add(new ReferenceInfo
            {
               FilePath = _workspace.FormatPath(refDocument.FilePath),
               Line = lineSpan.StartLinePosition.Line + 1,
               Column = lineSpan.StartLinePosition.Character + 1,
               LineText = lineText,
               Kind = kind
            });
         }
      }

      // Build context-aware suggestions
      var suggestions = new List<string>
      {
         $"get_symbol_info to get details about {symbol.Name}",
         $"find_callers to see methods that call {symbol.Name}"
      };
      if (symbol is INamedTypeSymbol)
         suggestions.Add($"get_type_members for {symbol.Name}");
      if (totalReferences > maxResults)
         suggestions.Add($"find_references with higher maxResults to see all {totalReferences} references");

      return ToolResult.Create(
         new FindReferencesResponse
         {
            Symbol = CreateSymbolResponse(symbol, verbosity),
            TotalReferences = totalReferences,
            References = referenceList
         },
         suggestedNextTools: suggestions.ToArray(),
         totalCount: totalReferences,
         returnedCount: referenceList?.Count ?? 0);
   }

   private SymbolResponse CreateSymbolResponse(ISymbol symbol, ToolResultVerbosity verbosity)
   {
      return RoslynHelpers.CreateSymbolResponse(symbol, verbosity, _workspace.SolutionPath, _workspace.UseAbsolutePaths);
   }

   /// <summary>
   /// Classifies a reference as Read, Write, or Call based on its syntax context.
   /// </summary>
   private static ReferenceKind ClassifyReference(SyntaxNode node)
   {
      var parent = node.Parent;

      // === Call detection ===

      // Direct invocation: Method() — node is the expression of InvocationExpression
      if (parent is InvocationExpressionSyntax invocation && invocation.Expression == node)
         return ReferenceKind.Call;

      // Member invocation: obj.Method() — node is Name of MemberAccess, grandparent is Invocation
      if (parent is MemberAccessExpressionSyntax memberAccess1 && memberAccess1.Name == node
          && memberAccess1.Parent is InvocationExpressionSyntax)
         return ReferenceKind.Call;

      // Constructor: new Type() — walk up through QualifiedName/MemberAccess to find ObjectCreation
      if (IsWithinObjectCreationType(node))
         return ReferenceKind.Call;

      // === Write detection ===

      // Assignment: x = val, x += val, etc. — node is the Left of an AssignmentExpression.
      // Compound assignments (+=, -=, etc.) are read-modify-write but we classify as Write
      // since write is the dominant action and the enum doesn't support ReadWrite.
      if (parent is AssignmentExpressionSyntax assignment && assignment.Left == node)
         return ReferenceKind.Write;

      // Member assignment: obj.Prop = val, obj.Prop += val — same logic as above
      if (parent is MemberAccessExpressionSyntax memberAccess2 && memberAccess2.Name == node
          && memberAccess2.Parent is AssignmentExpressionSyntax parentAssignment
          && parentAssignment.Left == memberAccess2)
         return ReferenceKind.Write;

      // Out/ref argument: Method(out x), Method(ref x)
      if (parent is ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.OutKeyword or (int)SyntaxKind.RefKeyword })
         return ReferenceKind.Write;

      // Out/ref argument via member access: Method(out obj.Field), Method(ref obj.Field)
      if (parent is MemberAccessExpressionSyntax memberAccess3 && memberAccess3.Name == node
          && memberAccess3.Parent is ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.OutKeyword or (int)SyntaxKind.RefKeyword })
         return ReferenceKind.Write;

      // Increment/decrement: x++, --x
      if (parent is PostfixUnaryExpressionSyntax postfix
          && postfix.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression)
         return ReferenceKind.Write;

      if (parent is PrefixUnaryExpressionSyntax prefix
          && prefix.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression)
         return ReferenceKind.Write;

      // === Default: Read ===
      return ReferenceKind.Read;
   }

   /// <summary>
   /// Checks whether <paramref name="node"/> is part of the type portion of an
   /// <see cref="ObjectCreationExpressionSyntax"/> (e.g., <c>new Ns.Type()</c>).
   /// Walks up through QualifiedNameSyntax and MemberAccessExpressionSyntax chains.
   /// </summary>
   private static bool IsWithinObjectCreationType(SyntaxNode node)
   {
      var current = node;
      while (current.Parent is QualifiedNameSyntax or MemberAccessExpressionSyntax)
         current = current.Parent;

      return current.Parent is ObjectCreationExpressionSyntax creation && creation.Type == current;
   }
}
