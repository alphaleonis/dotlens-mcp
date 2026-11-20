using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
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

      var allLocations = await ReferenceSearch.FindReferencesAsync(symbol, solution, cancellationToken);

      var totalReferences = allLocations.Count;

      // Build reference list (respecting maxResults and verbosity)
      List<ReferenceInfo>? referenceList = null;
      if (verbosity != ToolResultVerbosity.Summary)
      {
         referenceList = [];
         foreach (var loc in allLocations.Take(maxResults))
         {
            var refDocument = loc.Document;
            if (refDocument.FilePath == null)
               continue;

            var refTree = await refDocument.GetSyntaxTreeAsync(cancellationToken);
            if (refTree == null)
               continue;

            var refSpan = loc.Location.SourceSpan;
            var lineSpan = refTree.GetLineSpan(refSpan, cancellationToken);

            var refRoot = await refTree.GetRootAsync(cancellationToken);
            // ClassifyReferenceAsync widens from the reference's own node, not from a same-span wrapper such as an argument.
            var refNode = refRoot.FindNode(refSpan, getInnermostNodeForTie: true);
            var kind = await ClassifyReferenceAsync(loc, refNode, cancellationToken);

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
               Kind = kind,
               IsGenerated = refDocument is SourceGeneratedDocument
            });
         }
      }

      // Build context-aware suggestions
      var symbolName = RoslynHelpers.GetName(symbol);
      var suggestions = new List<string>
      {
         $"get_symbol_info to get details about {symbolName}",
         $"find_callers to see methods that call {symbolName}"
      };
      if (symbol is INamedTypeSymbol)
         suggestions.Add($"get_type_members for {symbolName}");
      if (totalReferences > maxResults)
         suggestions.Add($"find_references with higher maxResults to see all {totalReferences} references");

      return ToolResult.Create(
         new FindReferencesResponse
         {
            Symbol = await CreateSymbolResponseAsync(symbol, solution, verbosity, cancellationToken),
            TotalReferences = totalReferences,
            References = referenceList
         },
         suggestedNextTools: suggestions.ToArray(),
         totalCount: totalReferences,
         returnedCount: referenceList?.Count ?? 0);
   }

   private Task<SymbolResponse> CreateSymbolResponseAsync(
      ISymbol symbol, Solution solution, ToolResultVerbosity verbosity, CancellationToken cancellationToken)
   {
      return RoslynHelpers.CreateSymbolResponseAsync(
         symbol, verbosity, solution, _workspace.SolutionPath, _workspace.UseAbsolutePaths, cancellationToken);
   }

   /// <summary>
   /// Classifies a reference as Read, Write, or Call, from its syntax context unless it invokes a method whatever the
   /// syntax at its location.
   /// </summary>
   /// <remarks>
   /// An implicit reference to a method is a call that the compiler inserts, such as the Deconstruct call of a
   /// deconstruction. Code cannot name an operator, a conversion or a constructor except to call it, so a reference to
   /// one in code is a call. A reference outside code, see <see cref="IsInCode"/>, keeps the kind of its syntax. Such
   /// are a cref and a documentation ID in a SuppressMessage target.
   /// </remarks>
   private static async Task<ReferenceKind> ClassifyReferenceAsync(SourceReference reference, SyntaxNode node, CancellationToken cancellationToken)
   {
      if (reference.Definition is IMethodSymbol method
         && (reference.IsImplicit || method.MethodKind is MethodKind.Constructor or MethodKind.UserDefinedOperator or MethodKind.Conversion)
         && IsInCode(node, reference.Location.SourceSpan.Start))
         return ReferenceKind.Call;

      var document = reference.Document;
      var parent = node.Parent;

      // === Call detection ===

      // Direct invocation: Method() — node is the expression of InvocationExpression
      if (parent is InvocationExpressionSyntax invocation && invocation.Expression == node)
         return ReferenceKind.Call;

      // Member invocation: obj.Method() — node is Name of MemberAccess, grandparent is Invocation
      if (parent is MemberAccessExpressionSyntax memberAccess1 && memberAccess1.Name == node
          && memberAccess1.Parent is InvocationExpressionSyntax)
         return ReferenceKind.Call;

      // Conditional invocation: obj?.Method() — node is Name of MemberBinding, grandparent is Invocation
      if (parent is MemberBindingExpressionSyntax memberBinding && memberBinding.Name == node
          && memberBinding.Parent is InvocationExpressionSyntax conditionalInvocation && conditionalInvocation.Expression == memberBinding)
         return ReferenceKind.Call;

      // Constructor: new Type() — walk up through QualifiedName/MemberAccess to find ObjectCreation
      if (IsWithinObjectCreationType(node))
         return ReferenceKind.Call;

      return await IsWrittenToAsync(GetAccessExpression(node), document, cancellationToken) ? ReferenceKind.Write : ReferenceKind.Read;
   }

   /// <summary>
   /// Returns whether <paramref name="position"/> is in the code that <paramref name="node"/> spans, outside comments and
   /// the text of string literals.
   /// </summary>
   internal static bool IsInCode(SyntaxNode node, int position)
   {
      var token = node.FindToken(position, findInsideTrivia: true);
      return token.Span.Contains(position)
         && token.Parent?.FirstAncestorOrSelf<DocumentationCommentTriviaSyntax>() is null
         && token.Kind() is not (SyntaxKind.StringLiteralToken
            or SyntaxKind.Utf8StringLiteralToken
            or SyntaxKind.SingleLineRawStringLiteralToken
            or SyntaxKind.MultiLineRawStringLiteralToken
            or SyntaxKind.Utf8SingleLineRawStringLiteralToken
            or SyntaxKind.Utf8MultiLineRawStringLiteralToken
            or SyntaxKind.InterpolatedStringTextToken);
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
   /// Returns whether <paramref name="access"/> is assigned, incremented, decremented, deconstructed into, passed to a
   /// <c>ref</c> or <c>out</c> parameter, taken by a writable reference, or set by an attribute's named argument.
   /// </summary>
   /// <remarks>
   /// A compound assignment such as <c>+=</c> counts as a write, because <see cref="ReferenceKind"/> has no
   /// read-write kind. A writable reference counts as a write for the same reason, as code can write through it later.
   /// A member initializer with a nested initializer, such as <c>Items = { 1 }</c>, reads the member.
   /// </remarks>
   private static async Task<bool> IsWrittenToAsync(SyntaxNode access, Document document, CancellationToken cancellationToken)
   {
      switch (access.Parent)
      {
         case NameEqualsSyntax { Parent: AttributeArgumentSyntax }:
            return true;
         case RefExpressionSyntax refExpression:
            return await IsWritableReferenceAsync(refExpression, document, cancellationToken);
         case AssignmentExpressionSyntax assignment when assignment.Left == access:
            return assignment.Right is not InitializerExpressionSyntax;
         case PostfixUnaryExpressionSyntax postfix:
            return postfix.Kind() is SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression;
         case PrefixUnaryExpressionSyntax prefix:
            return prefix.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression;
         case ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.OutKeyword or (int)SyntaxKind.RefKeyword } argument:
            return await IsWritableArgumentAsync(argument, document, cancellationToken);
         case ArgumentSyntax { Parent: TupleExpressionSyntax tuple }:
            return IsDeconstructionTarget(tuple);
         default:
            return false;
      }
   }

   /// <summary>
   /// Returns whether the reference that <paramref name="refExpression"/> takes allows writes: it goes to a ref local,
   /// ref field or ref parameter that is not readonly, to a <c>ref</c> or <c>out</c> parameter, or out of a member that
   /// returns by writable reference.
   /// </summary>
   private static async Task<bool> IsWritableReferenceAsync(
      RefExpressionSyntax refExpression, Document document, CancellationToken cancellationToken)
   {
      // A branch of a ref conditional hands its reference to the context of the whole conditional.
      SyntaxNode taken = refExpression;
      while (taken.Parent is ParenthesizedExpressionSyntax or RefExpressionSyntax
         || (taken.Parent is ConditionalExpressionSyntax conditional && conditional.Condition != taken))
         taken = taken.Parent;

      if (taken.Parent is ArgumentSyntax argument)
         return await IsWritableArgumentAsync(argument, document, cancellationToken);

      if (await document.GetSemanticModelAsync(cancellationToken) is not { } model)
         return false;

      var receiver = taken.Parent switch
      {
         EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator } => model.GetDeclaredSymbol(declarator, cancellationToken),
         AssignmentExpressionSyntax assignment when assignment.Right == taken => model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol,
         ReturnStatementSyntax or ArrowExpressionClauseSyntax or LambdaExpressionSyntax => model.GetEnclosingSymbol(taken.SpanStart, cancellationToken),
         _ => null,
      };
      return receiver switch
      {
         ILocalSymbol local => local.RefKind == RefKind.Ref,
         IFieldSymbol field => field.RefKind == RefKind.Ref,
         IParameterSymbol parameter => parameter.RefKind is RefKind.Ref or RefKind.Out,
         IMethodSymbol method => method.RefKind == RefKind.Ref,
         IPropertySymbol property => property.RefKind == RefKind.Ref,
         _ => false,
      };
   }

   /// <summary>
   /// Returns whether <paramref name="argument"/> passes a reference that the callee can write through, to a
   /// <c>ref</c> or <c>out</c> parameter.
   /// </summary>
   /// <remarks>
   /// The parameter decides, because <c>ref</c> may also pass to an <c>in</c> or <c>ref readonly</c> parameter. An
   /// argument that binds to no parameter counts as a write when it has the <c>ref</c> or <c>out</c> keyword.
   /// </remarks>
   private static async Task<bool> IsWritableArgumentAsync(ArgumentSyntax argument, Document document, CancellationToken cancellationToken)
   {
      var hasWritableKeyword = argument.RefKindKeyword.Kind() is SyntaxKind.RefKeyword or SyntaxKind.OutKeyword;
      if (!hasWritableKeyword || await document.GetSemanticModelAsync(cancellationToken) is not { } model)
         return hasWritableKeyword;

      return model.GetOperation(argument, cancellationToken) is IArgumentOperation { Parameter: { } parameter }
         ? parameter.RefKind is RefKind.Ref or RefKind.Out
         : hasWritableKeyword;
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
