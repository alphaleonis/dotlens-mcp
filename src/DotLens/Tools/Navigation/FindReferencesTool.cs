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
            // ClassifyReference widens from the reference's own node, not from a same-span wrapper such as an argument.
            var refNode = refRoot.FindNode(refSpan, getInnermostNodeForTie: true);
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

      return IsWrittenTo(GetAccessExpression(node)) ? ReferenceKind.Write : ReferenceKind.Read;
   }

   /// <summary>
   /// Returns the expression that reads or writes the referenced symbol: the member access, member binding
   /// or element access that holds <paramref name="node"/>, widened through parentheses.
   /// </summary>
   private static SyntaxNode GetAccessExpression(SyntaxNode node)
   {
      var access = node switch
      {
         // An indexer reference sits on the brackets of the element access.
         BracketedArgumentListSyntax
         {
            Parent: (ElementAccessExpressionSyntax or ElementBindingExpressionSyntax or ImplicitElementAccessSyntax) and var elementAccess
         } => elementAccess,
         _ when node.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == node => memberAccess,
         _ when node.Parent is MemberBindingExpressionSyntax memberBinding => memberBinding,
         _ => node,
      };

      while (access.Parent is ParenthesizedExpressionSyntax parenthesized)
         access = parenthesized;

      return access;
   }

   /// <summary>
   /// Returns whether <paramref name="access"/> is assigned, incremented, decremented, deconstructed into,
   /// or passed as a <c>ref</c> or <c>out</c> argument.
   /// </summary>
   /// <remarks>
   /// A compound assignment such as <c>+=</c> counts as a write, because <see cref="ReferenceKind"/> has no
   /// read-write kind. A member initializer with a nested initializer, such as <c>Items = { 1 }</c>, reads the
   /// member.
   /// </remarks>
   private static bool IsWrittenTo(SyntaxNode access)
   {
      switch (access.Parent)
      {
         case AssignmentExpressionSyntax assignment when assignment.Left == access:
            return assignment.Right is not InitializerExpressionSyntax;
         case PostfixUnaryExpressionSyntax postfix:
            return postfix.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression;
         case PrefixUnaryExpressionSyntax prefix:
            return prefix.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression;
         case ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.OutKeyword or (int)SyntaxKind.RefKeyword }:
            return true;
         case ArgumentSyntax { Parent: TupleExpressionSyntax tuple }:
            return IsDeconstructionTarget(tuple);
         default:
            return false;
      }
   }

   /// <summary>
   /// Returns whether <paramref name="tuple"/> is the left side of a deconstruction, or nested inside one.
   /// </summary>
   private static bool IsDeconstructionTarget(TupleExpressionSyntax tuple)
   {
      var outermost = tuple;
      while (outermost.Parent is ArgumentSyntax { Parent: TupleExpressionSyntax enclosing })
         outermost = enclosing;

      return outermost.Parent is AssignmentExpressionSyntax assignment && assignment.Left == outermost;
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
