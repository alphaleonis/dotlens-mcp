using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using DotLens.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using Serilog;

namespace DotLens.Tools.Analysis;

/// <summary>
/// Shared helper methods for analysis tools.
/// </summary>
internal static class AnalysisHelpers
{
   private static readonly ConditionalWeakTable<Compilation, RecursionCycles> RecursionCyclesByCompilation = new();

   /// <summary>
   /// Normalizes a 1-based line range: converts to 0-based, swaps if needed, clamps to valid range.
   /// Returns the TextSpan and the normalized 0-based line numbers.
   /// </summary>
   public static (TextSpan Span, int StartLine0, int EndLine0) NormalizeLineRange(SourceText text, int startLine, int endLine)
   {
      var maxLine = text.Lines.Count - 1;
      var (line0, line1) = (Math.Clamp(startLine - 1, 0, maxLine), Math.Clamp(endLine - 1, 0, maxLine));
      if (line0 > line1)
         (line0, line1) = (line1, line0);
      var span = TextSpan.FromBounds(text.Lines[line0].Start, text.Lines[line1].End);
      return (span, line0, line1);
   }

   /// <summary>
   /// Extracts the BlockSyntax body from a method-like syntax node.
   /// Returns null for expression-bodied members or unsupported syntax types.
   /// </summary>
   public static BlockSyntax? GetMethodBody(SyntaxNode methodSyntax)
   {
      return methodSyntax switch
      {
         MethodDeclarationSyntax m => m.Body,
         LocalFunctionStatementSyntax lf => lf.Body,
         AccessorDeclarationSyntax a => a.Body,
         ConstructorDeclarationSyntax c => c.Body,
         DestructorDeclarationSyntax d => d.Body,
         OperatorDeclarationSyntax o => o.Body,
         ConversionOperatorDeclarationSyntax co => co.Body,
         AnonymousMethodExpressionSyntax am => am.Block,
         ParenthesizedLambdaExpressionSyntax pl => pl.Block,
         SimpleLambdaExpressionSyntax sl => sl.Block,
         _ => null
      };
   }

   /// <summary>
   /// Finds a diagnostic by ID using a multi-strategy approach:
   /// 1. Exact ID match with position contained in diagnostic span
   /// 2. Exact ID match with nearby position (within 50 characters)
   /// 3. Exact ID match anywhere in the diagnostic list
   /// </summary>
   /// <param name="diagnostics">List of diagnostics to search.</param>
   /// <param name="diagnosticId">The diagnostic ID to find (e.g., "CS0168").</param>
   /// <param name="position">The text position to search near.</param>
   /// <returns>The matching diagnostic, or null if not found.</returns>
   public static Diagnostic? FindDiagnosticByIdAndPosition(
      IReadOnlyList<Diagnostic> diagnostics,
      string diagnosticId,
      int position)
   {
      // Strategy 1: Try exact ID match with position contained in span
      var diagnostic = diagnostics.FirstOrDefault(d =>
         d.Id == diagnosticId &&
         d.Location.SourceSpan.Contains(position));

      // Strategy 2: Try exact ID match with nearby position
      if (diagnostic == null)
      {
         diagnostic = diagnostics.FirstOrDefault(d =>
            d.Id == diagnosticId &&
            Math.Abs(d.Location.SourceSpan.Start - position) < 50);
      }

      // Strategy 3: Try exact ID match anywhere in list
      if (diagnostic == null)
      {
         diagnostic = diagnostics.FirstOrDefault(d => d.Id == diagnosticId);
      }

      return diagnostic;
   }

   /// <summary>
   /// Counts diagnostics near a given position (within 50 characters or containing the position).
   /// </summary>
   public static int CountDiagnosticsNearPosition(IReadOnlyList<Diagnostic> diagnostics, int position)
   {
      return diagnostics.Count(d =>
         d.Location.SourceSpan.Contains(position) ||
         Math.Abs(d.Location.SourceSpan.Start - position) < 50);
   }

   /// <summary>
   /// Computes a stable 8-character key for a code action based on provider type, title, and equivalence key.
   /// This key can be used to reliably identify the same fix across invocations.
   /// </summary>
   public static string ComputeCodeActionKey(CodeFixProvider provider, CodeAction action)
   {
      // 8 hex chars = 32 bits = ~4 billion unique values. For typical scenarios with <100 fixes
      // per diagnostic, collision probability is negligible (~1 in 40 million via birthday paradox).
      // In the unlikely event of a collision, FirstOrDefault returns the first match.
      var composite = $"{provider.GetType().Name}|{action.Title}|{action.EquivalenceKey ?? ""}";
      var hash = SHA256.HashData(Encoding.UTF8.GetBytes(composite));
      return Convert.ToHexString(hash)[..8].ToLowerInvariant();
   }

   /// <summary>
   /// Gets code actions for a diagnostic along with their providers, sorted by priority (highest first).
   /// </summary>
   public static async Task<List<(CodeFixProvider Provider, CodeAction Action)>> GetCodeActionsForDiagnosticAsync(
      Document document,
      Diagnostic diagnostic,
      CancellationToken cancellationToken)
   {
      var codeActions = new List<(CodeFixProvider Provider, CodeAction Action)>();
      var codeFixProviders = CodeFixHelpers.GetCodeFixProvidersForProject(document.Project);

      foreach (var provider in codeFixProviders)
      {
         cancellationToken.ThrowIfCancellationRequested();
         if (!provider.FixableDiagnosticIds.Contains(diagnostic.Id))
            continue;

         var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => codeActions.Add((provider, action)),
            cancellationToken);

         try
         {
            await provider.RegisterCodeFixesAsync(context);
         }
         catch (OperationCanceledException)
         {
            throw; // Preserve cancellation semantics
         }
         catch (Exception ex)
         {
            // Provider may throw if it can't handle the diagnostic - expected for some combinations
            Log.Debug(ex, "CodeFixProvider {Provider} failed for diagnostic {DiagnosticId}",
               provider.GetType().Name, diagnostic.Id);
         }
      }

      // Sort by priority (highest first)
      return codeActions
         .OrderByDescending(x => x.Action.Priority)
         .ToList();
   }

   public static ComplexityMetrics CalculateComplexityMetrics(
      SyntaxNode node,
      SemanticModel semanticModel,
      IEnumerable<MetricType> requestedMetrics,
      CancellationToken cancellationToken)
   {
      int? cyclomatic = null;
      int? nesting = null;
      int? loc = null;
      int? parameters = null;
      int? cognitive = null;

      foreach (var metric in requestedMetrics)
      {
         switch (metric)
         {
            case MetricType.Cyclomatic:
               cyclomatic = CalculateCyclomaticComplexity(node);
               break;
            case MetricType.Nesting:
               nesting = CalculateNestingDepth(node);
               break;
            case MetricType.Loc:
               loc = CalculateLinesOfCode(node);
               break;
            case MetricType.Parameters:
               parameters = CountParameters(node);
               break;
            case MetricType.Cognitive:
               cognitive = CalculateCognitiveComplexity(node, semanticModel, cancellationToken);
               break;
         }
      }

      return new ComplexityMetrics
      {
         Cyclomatic = cyclomatic,
         Nesting = nesting,
         Loc = loc,
         Parameters = parameters,
         Cognitive = cognitive
      };
   }

   public static int CalculateCyclomaticComplexity(SyntaxNode node)
   {
      int complexity = 1;

      foreach (var descendant in node.DescendantNodes())
      {
         switch (descendant)
         {
            case IfStatementSyntax:
            case ConditionalExpressionSyntax:
            case CaseSwitchLabelSyntax:
            case CasePatternSwitchLabelSyntax:
            case WhileStatementSyntax:
            case ForStatementSyntax:
            case ForEachStatementSyntax:
            case CatchClauseSyntax:
            case ConditionalAccessExpressionSyntax:
               complexity++;
               break;
            case BinaryExpressionSyntax binary when
               binary.Kind() == SyntaxKind.LogicalAndExpression ||
               binary.Kind() == SyntaxKind.LogicalOrExpression ||
               binary.Kind() == SyntaxKind.CoalesceExpression:
               complexity++;
               break;
         }
      }

      return complexity;
   }

   public static int CalculateNestingDepth(SyntaxNode node)
   {
      int maxDepth = 0;
      CalculateNestingDepthRecursive(node, 0, ref maxDepth);
      return maxDepth;
   }

   private static void CalculateNestingDepthRecursive(SyntaxNode node, int currentDepth, ref int maxDepth)
   {
      foreach (var child in node.ChildNodes())
      {
         int newDepth = currentDepth;

         if (child is IfStatementSyntax or WhileStatementSyntax or ForStatementSyntax or
            ForEachStatementSyntax or SwitchStatementSyntax or TryStatementSyntax or
            LockStatementSyntax or UsingStatementSyntax)
         {
            newDepth = currentDepth + 1;
            maxDepth = Math.Max(maxDepth, newDepth);
         }

         CalculateNestingDepthRecursive(child, newDepth, ref maxDepth);
      }
   }

   public static int CalculateLinesOfCode(SyntaxNode node)
   {
      // Count unique lines that contain actual code tokens (not just trivia/comments)
      return node.DescendantTokens()
         .Where(t => !t.IsMissing)
         .Select(t => t.GetLocation().GetLineSpan().StartLinePosition.Line)
         .Distinct()
         .Count();
   }

   public static int CountParameters(SyntaxNode node)
   {
      return node is MethodDeclarationSyntax method ? method.ParameterList.Parameters.Count : 0;
   }

   /// <summary>
   /// Calculates cognitive complexity as SonarSource's
   /// <see href="https://www.sonarsource.com/docs/CognitiveComplexity.pdf">Cognitive Complexity white paper</see>
   /// (version 1.7) defines it.
   /// </summary>
   /// <remarks>
   /// A method in a recursion cycle adds one, once however many cycles it is in; <see cref="FindRecursionCycleMembers"/>
   /// lists which calls the cycle follows and which it does not.
   /// </remarks>
   public static int CalculateCognitiveComplexity(SyntaxNode node, SemanticModel semanticModel, CancellationToken cancellationToken)
   {
      int complexity = 0;
      CalculateCognitiveComplexityRecursive(node, 0, ref complexity);
      return complexity + CountRecursiveMethods(node, semanticModel, cancellationToken);
   }

   private static void CalculateCognitiveComplexityRecursive(SyntaxNode node, int nestingLevel, ref int complexity)
   {
      foreach (var child in node.ChildNodes())
      {
         var (increment, newNesting) = child switch
         {
            // The head if of the chain already raised the nesting level for each else-if and else.
            IfStatementSyntax { Parent: ElseClauseSyntax } => (1, nestingLevel),
            ElseClauseSyntax { Statement: IfStatementSyntax } => (0, nestingLevel),
            ElseClauseSyntax => (1, nestingLevel),
            IfStatementSyntax or ConditionalExpressionSyntax or SwitchStatementSyntax or SwitchExpressionSyntax
               or ForStatementSyntax or CommonForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax
               or CatchClauseSyntax => (1 + nestingLevel, nestingLevel + 1),
            AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax => (0, nestingLevel + 1),
            GotoStatementSyntax or BreakStatementSyntax { Name: not null } or ContinueStatementSyntax { Name: not null }
               => (1, nestingLevel),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.LogicalAndExpression) || binary.IsKind(SyntaxKind.LogicalOrExpression)
               => (StartsLogicalSequence(binary, binary.Left) ? 1 : 0, nestingLevel),
            BinaryPatternSyntax pattern when pattern.IsKind(SyntaxKind.AndPattern) || pattern.IsKind(SyntaxKind.OrPattern)
               => (StartsLogicalSequence(pattern, pattern.Left) ? 1 : 0, nestingLevel),
            _ => (0, nestingLevel)
         };

         complexity += increment;
         CalculateCognitiveComplexityRecursive(child, newNesting, ref complexity);
      }
   }

   /// <summary>
   /// Returns whether a logical operator starts a new sequence of like operators, which costs one increment.
   /// </summary>
   /// <remarks>
   /// The operator continues a sequence when its left operand, or the parent whose right operand it is, has the same kind,
   /// ignoring parentheses, so <c>a &amp;&amp; (b &amp;&amp; c)</c> is one sequence.
   /// </remarks>
   private static bool StartsLogicalSequence(SyntaxNode logicalOperator, SyntaxNode left)
   {
      var kind = logicalOperator.Kind();
      if (SkipParentheses(left).IsKind(kind))
         return false;

      var operand = logicalOperator;
      while (operand.Parent is ParenthesizedExpressionSyntax or ParenthesizedPatternSyntax)
         operand = operand.Parent;

      return operand.Parent switch
      {
         BinaryExpressionSyntax parent => !(parent.IsKind(kind) && parent.Right == operand),
         BinaryPatternSyntax parent => !(parent.IsKind(kind) && parent.Right == operand),
         _ => true
      };
   }

   private static SyntaxNode SkipParentheses(SyntaxNode node)
   {
      while (true)
      {
         switch (node)
         {
            case ParenthesizedExpressionSyntax parenthesized:
               node = parenthesized.Expression;
               break;
            case ParenthesizedPatternSyntax parenthesized:
               node = parenthesized.Pattern;
               break;
            default:
               return node;
         }
      }
   }

   /// <summary>
   /// Counts the methods declared in the node, the node itself and its local functions, that are in a recursion cycle.
   /// </summary>
   private static int CountRecursiveMethods(SyntaxNode node, SemanticModel semanticModel, CancellationToken cancellationToken)
   {
      var declared = node.DescendantNodesAndSelf()
         .Select(declaration => GetDeclaredCallGraphMethod(declaration, semanticModel, cancellationToken))
         .OfType<IMethodSymbol>()
         // The implementation part of a partial member holds the body, so only that part counts.
         .Where(method => method.PartialImplementationPart is null)
         .Select(GetCallGraphNode)
         .ToList();
      if (declared.Count == 0)
         return 0;

      var cycleMembers = FindRecursionCycleMembers(semanticModel.Compilation, declared, cancellationToken);
      return declared.Count(cycleMembers.Contains);
   }

   /// <summary>
   /// Returns those of the methods that are in a cycle of the compilation's call graph: a strongly connected component
   /// of two or more methods, or a method that calls itself.
   /// </summary>
   /// <remarks>
   /// <para>
   /// The nodes are the methods, constructors (primary and implicit ones too), operators, conversions, accessors and
   /// local functions of the compilation. A call links to the method it binds to at compile time on the receiver's
   /// static type, never to an override or implementation that dispatch could choose at run time. Project references
   /// are acyclic, so a cycle never leaves the compilation.
   /// </para>
   /// <para>
   /// Besides explicit calls, a property or indexer access links to the accessors it calls, an event subscription to
   /// its accessor, and foreach, await, deconstruction, positional patterns, collection expressions and with
   /// expressions to the members they call. A constructor links to its implicit base constructor and to the calls in
   /// the instance initializers it runs, and a record's generated members link to what the C# specification says they
   /// call. A delegate invocation, a method group, dynamic code and the awaits of an await foreach link to nothing.
   /// Neither do the Dispose call of a using statement and the Add calls of a collection expression, because the
   /// operation tree does not hold their targets.
   /// </para>
   /// <para>
   /// A call in a lambda belongs to the declaration around the lambda, and a call in a local function belongs to the
   /// local function.
   /// </para>
   /// <para>
   /// A method's strongly connected component lies within the methods it reaches, so the search binds only the methods
   /// reachable from the ones asked about. <see cref="RecursionCycles"/> keeps what it finds per compilation.
   /// </para>
   /// </remarks>
   /// <param name="methods">The methods to check, each as <see cref="GetCallGraphNode"/> returns it.</param>
   private static HashSet<IMethodSymbol> FindRecursionCycleMembers(
      Compilation compilation,
      IReadOnlyCollection<IMethodSymbol> methods,
      CancellationToken cancellationToken)
   {
      return RecursionCyclesByCompilation
         .GetValue(compilation, static key => new RecursionCycles(key))
         .FindMembers(methods, cancellationToken);
   }

   /// <summary>
   /// The recursion cycles of one compilation, searched for from the methods asked about and kept for later searches.
   /// </summary>
   /// <remarks>
   /// A search explores the call graph from the methods that no earlier search finished, then runs Tarjan's algorithm
   /// on what it explored. Every method it explored is then finished, with a final answer, because its strongly
   /// connected component lies within what the search explored. A later search stops at a finished method, because
   /// everything that method reaches is finished too, so it cannot join a new component.
   /// </remarks>
   private sealed class RecursionCycles(Compilation compilation)
   {
      private static readonly TimeSpan CancellationCheckInterval = TimeSpan.FromMilliseconds(50);

      private readonly Lock _gate = new();

      // Whether each finished method is in a cycle. A search writes it under _gate only once it has run to its end, so
      // a canceled search leaves no answer. Readers take no lock.
      private readonly ConcurrentDictionary<IMethodSymbol, bool> _finished = new(SymbolEqualityComparer.Default);

      // The fields below are used under _gate only. Each entry is complete once added, so a canceled search leaves
      // none half built.
      private readonly HashSet<SyntaxTree> _indexedTrees = [];
      private readonly Dictionary<IMethodSymbol, List<SyntaxNode>> _declarations = new(SymbolEqualityComparer.Default);
      private readonly Dictionary<INamedTypeSymbol, List<IMethodSymbol>> _initializerCalls = new(SymbolEqualityComparer.Default);
      private readonly Dictionary<INamedTypeSymbol, Dictionary<IMethodSymbol, List<IMethodSymbol?>>> _recordMemberCalls =
         new(SymbolEqualityComparer.Default);
      private readonly Dictionary<INamedTypeSymbol, Dictionary<IMethodSymbol, IMethodSymbol>> _baseConstructorCalls =
         new(SymbolEqualityComparer.Default);
      private readonly Dictionary<SyntaxTree, SemanticModel> _semanticModels = [];

      public HashSet<IMethodSymbol> FindMembers(IReadOnlyCollection<IMethodSymbol> methods, CancellationToken cancellationToken)
      {
         if (!methods.All(_finished.ContainsKey))
         {
            // One caller searches while the others wait.
            while (!_gate.TryEnter(CancellationCheckInterval))
               cancellationToken.ThrowIfCancellationRequested();

            try
            {
               Search(methods.Where(method => !_finished.ContainsKey(method)), cancellationToken);
            }
            finally
            {
               // The models hold what they bound, which no later search needs.
               _semanticModels.Clear();
               _gate.Exit();
            }
         }

         return methods.Where(method => _finished[method]).ToHashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
      }

      private void Search(IEnumerable<IMethodSymbol> methods, CancellationToken cancellationToken)
      {
         cancellationToken.ThrowIfCancellationRequested();
         var calls = new Dictionary<IMethodSymbol, List<IMethodSymbol>>(SymbolEqualityComparer.Default);
         var pending = new Stack<IMethodSymbol>(methods);
         var awaitingBaseCall = new List<IMethodSymbol>();
         while (true)
         {
            while (pending.TryPop(out var method))
            {
               if (_finished.ContainsKey(method) || calls.ContainsKey(method))
                  continue;

               cancellationToken.ThrowIfCancellationRequested();
               var callees = new List<IMethodSymbol>();
               calls.Add(method, callees);
               foreach (var callee in GetCalledMethods(method, cancellationToken))
                  AddCall(callees, callee);

               if (NeedsBaseConstructorCall(method, cancellationToken))
               {
                  if (_baseConstructorCalls.TryGetValue(method.ContainingType, out var baseCalls))
                     AddCall(callees, baseCalls.GetValueOrDefault(method));
                  else
                     awaitingBaseCall.Add(method);
               }
            }

            if (awaitingBaseCall.Count == 0)
               break;

            // Each binding of implicit base constructor calls builds a copy of the compilation, so one binding serves
            // every constructor that this round of the search reached.
            ResolveBaseConstructorCalls(
               awaitingBaseCall.Select(method => method.ContainingType).Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default),
               cancellationToken);
            foreach (var constructor in awaitingBaseCall)
               AddCall(calls[constructor], _baseConstructorCalls[constructor.ContainingType].GetValueOrDefault(constructor));
            awaitingBaseCall.Clear();
         }

         var explored = calls.Keys.ToList();
         var indices = new Dictionary<IMethodSymbol, int>(SymbolEqualityComparer.Default);
         foreach (var method in explored)
            indices.Add(method, indices.Count);
         var edges = new List<(int Caller, int Callee)>();
         foreach (var (caller, callees) in calls)
         {
            foreach (var callee in callees)
            {
               // A callee missing from the indices was finished by an earlier search.
               if (indices.TryGetValue(callee, out var calleeIndex))
                  edges.Add((indices[caller], calleeIndex));
            }
         }

         var members = FindCycleMembers(explored, edges, cancellationToken);
         foreach (var method in explored)
            _finished[method] = members.Contains(method);

         void AddCall(List<IMethodSymbol> callees, IMethodSymbol? callee)
         {
            // A method outside the compilation calls nothing in it, because project references are acyclic.
            if (callee is not null
               && GetCallGraphNode(callee) is var node
               && SymbolEqualityComparer.Default.Equals(node.ContainingAssembly, compilation.Assembly))
            {
               callees.Add(node);
               pending.Push(node);
            }
         }
      }

      /// <summary>
      /// Returns what the method calls, apart from an implicit base constructor call.
      /// </summary>
      private IEnumerable<IMethodSymbol?> GetCalledMethods(IMethodSymbol method, CancellationToken cancellationToken)
      {
         IndexDeclarations(method, cancellationToken);
         if (_declarations.TryGetValue(method, out var declarations))
         {
            foreach (var declaration in declarations)
            {
               var semanticModel = GetSemanticModel(declaration.SyntaxTree);
               if (semanticModel.GetOperation(declaration, cancellationToken) is { } body)
               {
                  foreach (var callee in AnalysisHelpers.GetCalledMethods(body, semanticModel))
                     yield return callee;
               }
            }
         }

         if (method.ContainingType is not { } type || !IsDeclaredByTypeDeclaration(type, cancellationToken))
            yield break;

         if (method.MethodKind == MethodKind.Constructor
            && type.InstanceConstructors.Any(constructor =>
               SymbolEqualityComparer.Default.Equals(GetCallGraphNode(constructor), method)
               && RunsInstanceInitializers(constructor, cancellationToken)))
         {
            foreach (var callee in GetInitializerCalls(type, cancellationToken))
               yield return callee;
         }

         if (type.IsRecord && GetRecordMemberCalls(type).TryGetValue(method, out var recordMemberCallees))
         {
            foreach (var callee in recordMemberCallees)
               yield return callee;
         }
      }

      /// <summary>
      /// Maps each method declared in the trees of the method and of its type to its declarations, once per tree.
      /// </summary>
      /// <remarks>
      /// Every declaration that maps to a method lies in a declaration of the method's type: a partial part, an
      /// extension block of the static class, or a member that holds a local function.
      /// </remarks>
      private void IndexDeclarations(IMethodSymbol method, CancellationToken cancellationToken)
      {
         var trees = (method.ContainingType?.DeclaringSyntaxReferences ?? [])
            .Concat(method.DeclaringSyntaxReferences)
            .Select(reference => reference.SyntaxTree);
         foreach (var tree in trees)
         {
            if (_indexedTrees.Contains(tree))
               continue;

            var semanticModel = GetSemanticModel(tree);
            var found = new List<(IMethodSymbol Method, SyntaxNode Declaration)>();
            foreach (var node in tree.GetRoot(cancellationToken).DescendantNodes())
            {
               cancellationToken.ThrowIfCancellationRequested();
               if (GetDeclaredCallGraphMethod(node, semanticModel, cancellationToken) is { } declared)
                  found.Add((GetCallGraphNode(declared), node));
            }

            foreach (var (declared, declaration) in found)
            {
               if (!_declarations.TryGetValue(declared, out var declarations))
                  _declarations.Add(declared, declarations = []);
               declarations.Add(declaration);
            }

            _indexedTrees.Add(tree);
         }
      }

      private List<IMethodSymbol> GetInitializerCalls(INamedTypeSymbol type, CancellationToken cancellationToken)
      {
         if (_initializerCalls.TryGetValue(type, out var calls))
            return calls;

         calls = [];
         foreach (var reference in type.DeclaringSyntaxReferences)
         {
            if (reference.GetSyntax(cancellationToken) is not TypeDeclarationSyntax declaration)
               continue;

            var semanticModel = GetSemanticModel(reference.SyntaxTree);
            var initializers = declaration.Members.SelectMany<MemberDeclarationSyntax, SyntaxNode?>(member => member switch
            {
               BaseFieldDeclarationSyntax field => field.Declaration.Variables.Select(variable => variable.Initializer),
               PropertyDeclarationSyntax property => [property.Initializer],
               _ => []
            });
            foreach (var initializer in initializers.OfType<SyntaxNode>())
            {
               if (GetInstanceInitializer(initializer, semanticModel, cancellationToken) is var (initializedType, operation)
                  && SymbolEqualityComparer.Default.Equals(initializedType, type))
               {
                  calls.AddRange(AnalysisHelpers.GetCalledMethods(operation, semanticModel).OfType<IMethodSymbol>());
               }
            }
         }

         _initializerCalls.Add(type, calls);
         return calls;
      }

      private Dictionary<IMethodSymbol, List<IMethodSymbol?>> GetRecordMemberCalls(INamedTypeSymbol record)
      {
         if (_recordMemberCalls.TryGetValue(record, out var calls))
            return calls;

         calls = new Dictionary<IMethodSymbol, List<IMethodSymbol?>>(SymbolEqualityComparer.Default);
         foreach (var (member, callees) in GetGeneratedRecordMemberCalls(record))
         {
            var node = GetCallGraphNode(member);
            if (!calls.TryGetValue(node, out var memberCallees))
               calls.Add(node, memberCallees = []);
            memberCallees.AddRange(callees);
         }

         _recordMemberCalls.Add(record, calls);
         return calls;
      }

      /// <summary>
      /// Returns whether the method is a constructor whose implicit base constructor call can close a cycle.
      /// </summary>
      private bool NeedsBaseConstructorCall(IMethodSymbol method, CancellationToken cancellationToken)
         => method is { MethodKind: MethodKind.Constructor, ContainingType: { TypeKind: TypeKind.Class, BaseType: { } baseType } type }
            && SymbolEqualityComparer.Default.Equals(baseType.ContainingAssembly, compilation.Assembly)
            && IsDeclaredByTypeDeclaration(type, cancellationToken);

      private void ResolveBaseConstructorCalls(IEnumerable<INamedTypeSymbol> types, CancellationToken cancellationToken)
      {
         var resolved = new Dictionary<INamedTypeSymbol, Dictionary<IMethodSymbol, IMethodSymbol>>(SymbolEqualityComparer.Default);
         foreach (var type in types)
            resolved.Add(type, new Dictionary<IMethodSymbol, IMethodSymbol>(SymbolEqualityComparer.Default));
         foreach (var (constructor, baseConstructor) in GetImplicitBaseConstructorCalls(compilation, resolved.Keys, cancellationToken))
            resolved[constructor.ContainingType][GetCallGraphNode(constructor)] = baseConstructor;

         foreach (var (type, calls) in resolved)
            _baseConstructorCalls.Add(type, calls);
      }

      private SemanticModel GetSemanticModel(SyntaxTree tree)
      {
         if (!_semanticModels.TryGetValue(tree, out var semanticModel))
            _semanticModels.Add(tree, semanticModel = compilation.GetSemanticModel(tree));
         return semanticModel;
      }

      // The search covers the types that a type declaration declares, which leaves out delegates and enums.
      private static bool IsDeclaredByTypeDeclaration(INamedTypeSymbol type, CancellationToken cancellationToken)
         => type.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax);
   }

   /// <summary>
   /// Returns the method that a declaration adds to the call graph, or null when the node declares none.
   /// </summary>
   /// <remarks>
   /// The getter of an expression-bodied property or indexer is its arrow clause, and a primary constructor is its type
   /// declaration.
   /// </remarks>
   private static IMethodSymbol? GetDeclaredCallGraphMethod(SyntaxNode node, SemanticModel semanticModel, CancellationToken cancellationToken)
   {
      return node switch
      {
         ArrowExpressionClauseSyntax { Parent: (PropertyDeclarationSyntax or IndexerDeclarationSyntax) and var property }
            => (semanticModel.GetDeclaredSymbol(property, cancellationToken) as IPropertySymbol)?.GetMethod,
         TypeDeclarationSyntax { ParameterList: not null } type
            => (semanticModel.GetDeclaredSymbol(type, cancellationToken) as INamedTypeSymbol)?.InstanceConstructors.FirstOrDefault(
               constructor => constructor.DeclaringSyntaxReferences.Any(r => r.SyntaxTree == type.SyntaxTree && r.Span == type.Span)),
         AccessorDeclarationSyntax => semanticModel.GetDeclaredSymbol(node, cancellationToken) as IMethodSymbol,
         _ when node.IsCallableMethodSyntax() => semanticModel.GetDeclaredSymbol(node, cancellationToken) as IMethodSymbol,
         _ => null
      };
   }

   /// <summary>
   /// Returns the initializer of an instance field or property, with the type whose constructors run it, or null when
   /// the node is no such initializer.
   /// </summary>
   private static (INamedTypeSymbol Type, IOperation Initializer)? GetInstanceInitializer(
      SyntaxNode node,
      SemanticModel semanticModel,
      CancellationToken cancellationToken)
   {
      if (node is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax } or PropertyDeclarationSyntax })
         return null;

      var initializer = semanticModel.GetOperation(node, cancellationToken);
      ISymbol? initialized = initializer switch
      {
         IFieldInitializerOperation { InitializedFields: [var field, ..] } => field,
         IPropertyInitializerOperation { InitializedProperties: [var property, ..] } => property,
         _ => null
      };

      return initializer is not null && initialized is { IsStatic: false, ContainingType: { } type } ? (type, initializer) : null;
   }

   /// <summary>
   /// Returns whether a constructor runs the instance field and property initializers of its type.
   /// </summary>
   /// <remarks>
   /// A constructor that calls this(...) leaves them to the constructor it calls. A struct's implicit parameterless
   /// constructor and a record's generated copy constructor run none.
   /// </remarks>
   private static bool RunsInstanceInitializers(IMethodSymbol constructor, CancellationToken cancellationToken)
   {
      if (constructor.IsImplicitlyDeclared)
         return constructor.ContainingType.TypeKind == TypeKind.Class && !IsRecordCopyConstructor(constructor);

      return !(constructor.PartialImplementationPart ?? constructor).DeclaringSyntaxReferences.Any(reference =>
         reference.GetSyntax(cancellationToken) is ConstructorDeclarationSyntax { Initializer: { } initializer }
         && initializer.ThisOrBaseKeyword.IsKind(SyntaxKind.ThisKeyword));
   }

   /// <summary>
   /// Returns the base constructor that each implicit constructor, and each primary constructor without base
   /// arguments, calls, as the compiler binds that call.
   /// </summary>
   /// <remarks>
   /// The operation tree holds no such call, so a copy of the compilation declares it in source: an empty constructor in
   /// place of the implicit one, and an empty base argument list for the primary one. Only a base class in this
   /// compilation can close a cycle, so other base classes are skipped.
   /// </remarks>
   internal static IEnumerable<(IMethodSymbol Constructor, IMethodSymbol BaseConstructor)> GetImplicitBaseConstructorCalls(
      Compilation compilation,
      IEnumerable<INamedTypeSymbol> types,
      CancellationToken cancellationToken)
   {
      var declarationsByTree = new Dictionary<SyntaxTree, Dictionary<TypeDeclarationSyntax, (SyntaxAnnotation Annotation, IMethodSymbol Constructor)>>();
      foreach (var type in types)
      {
         if (type is not { TypeKind: TypeKind.Class, BaseType: { } baseType }
            || !SymbolEqualityComparer.Default.Equals(baseType.ContainingAssembly, compilation.Assembly))
         {
            continue;
         }

         foreach (var constructor in type.InstanceConstructors)
         {
            var declaration = constructor.IsImplicitlyDeclared
               ? IsRecordCopyConstructor(constructor)
                  ? null
                  : type.DeclaringSyntaxReferences.Select(r => r.GetSyntax(cancellationToken)).OfType<TypeDeclarationSyntax>().FirstOrDefault()
               : constructor.DeclaringSyntaxReferences is [var reference]
                  && reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax primary
                  && primary.BaseList?.Types.Any(t => t is PrimaryConstructorBaseTypeSyntax) != true
                  ? primary
                  : null;

            if (declaration is null)
               continue;

            if (!declarationsByTree.TryGetValue(declaration.SyntaxTree, out var declarations))
               declarationsByTree.Add(declaration.SyntaxTree, declarations = []);
            declarations[declaration] = (new SyntaxAnnotation(), constructor);
         }
      }

      var copy = compilation;
      var copiedTrees = new List<(SyntaxTree Tree, Dictionary<TypeDeclarationSyntax, (SyntaxAnnotation Annotation, IMethodSymbol Constructor)> Declarations)>();
      foreach (var (tree, declarations) in declarationsByTree)
      {
         var semanticModel = compilation.GetSemanticModel(tree);
         var root = tree.GetRoot(cancellationToken).ReplaceNodes(
            declarations.Keys,
            (original, rewritten) => DeclareBaseConstructorCall(original, rewritten, declarations[original], semanticModel, cancellationToken));
         var copiedTree = tree.WithRootAndOptions(root, tree.Options);
         copy = copy.ReplaceSyntaxTree(tree, copiedTree);
         copiedTrees.Add((copiedTree, declarations));
      }

      foreach (var (copiedTree, declarations) in copiedTrees)
      {
         var semanticModel = copy.GetSemanticModel(copiedTree);
         var root = copiedTree.GetRoot(cancellationToken);
         foreach (var (annotation, constructor) in declarations.Values)
         {
            cancellationToken.ThrowIfCancellationRequested();
            var call = semanticModel.GetOperation(root.GetAnnotatedNodes(annotation).Single(), cancellationToken) switch
            {
               IConstructorBodyOperation { Initializer: IExpressionStatementOperation { Operation: IInvocationOperation invocation } } => invocation,
               IInvocationOperation invocation => invocation,
               _ => null
            };

            // The copy has symbols of its own, so the documentation ID finds the same constructor in the compilation.
            if (call is not null
               && DocumentationCommentId.CreateDeclarationId(call.TargetMethod.OriginalDefinition) is { } id
               && DocumentationCommentId.GetFirstSymbolForDeclarationId(id, compilation) is IMethodSymbol baseConstructor)
            {
               yield return (constructor, baseConstructor);
            }
            else
            {
               Log.Warning(
                  "Could not bind the implicit base constructor call of {Constructor}, so cognitive complexity misses cycles through it",
                  constructor.ToDisplayString());
            }
         }
      }
   }

   /// <summary>
   /// Returns a type declaration that calls its base constructor explicitly in place of implicitly: with an empty
   /// constructor added, or with an empty base argument list on a primary constructor.
   /// </summary>
   private static TypeDeclarationSyntax DeclareBaseConstructorCall(
      TypeDeclarationSyntax original,
      TypeDeclarationSyntax rewritten,
      (SyntaxAnnotation Annotation, IMethodSymbol Constructor) declaration,
      SemanticModel semanticModel,
      CancellationToken cancellationToken)
   {
      var annotation = declaration.Annotation;
      if (original.ParameterList is null)
      {
         // The added constructor keeps the implicit one's accessibility, so derived types in the copy can still call it.
         var constructor = SyntaxFactory.ConstructorDeclaration(rewritten.Identifier.WithoutTrivia())
            .WithModifiers(GetAccessibilityModifiers(declaration.Constructor.DeclaredAccessibility))
            .WithBody(SyntaxFactory.Block())
            .WithAdditionalAnnotations(annotation);

         return rewritten.AddMembers(constructor);
      }

      // The base class comes first in a base list, but another part of a partial type may name it instead.
      var baseList = rewritten.BaseList ?? SyntaxFactory.BaseList();
      var namesBaseClass = original.BaseList?.Types.FirstOrDefault() is { } first
         && semanticModel.GetTypeInfo(first.Type, cancellationToken).Type is { TypeKind: TypeKind.Class };
      var baseClass = semanticModel.GetDeclaredSymbol(original, cancellationToken)?.BaseType;
      var call = SyntaxFactory.PrimaryConstructorBaseType(
            namesBaseClass ? baseList.Types[0].Type : SyntaxFactory.ParseTypeName(baseClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "object"),
            SyntaxFactory.ArgumentList())
         .WithAdditionalAnnotations(annotation);

      return rewritten.WithBaseList(baseList.WithTypes(namesBaseClass ? baseList.Types.Replace(baseList.Types[0], call) : baseList.Types.Insert(0, call)));
   }

   private static SyntaxTokenList GetAccessibilityModifiers(Accessibility accessibility)
   {
      SyntaxKind[] keywords = accessibility switch
      {
         Accessibility.Public => [SyntaxKind.PublicKeyword],
         Accessibility.Protected => [SyntaxKind.ProtectedKeyword],
         Accessibility.Internal => [SyntaxKind.InternalKeyword],
         Accessibility.ProtectedOrInternal => [SyntaxKind.ProtectedKeyword, SyntaxKind.InternalKeyword],
         Accessibility.ProtectedAndInternal => [SyntaxKind.PrivateKeyword, SyntaxKind.ProtectedKeyword],
         Accessibility.Private => [SyntaxKind.PrivateKeyword],
         _ => throw new InvalidOperationException($"Unexpected accessibility: {accessibility}")
      };

      return SyntaxFactory.TokenList(keywords.Select(SyntaxFactory.Token));
   }

   private static bool IsRecordCopyConstructor(IMethodSymbol constructor)
      => constructor is { MethodKind: MethodKind.Constructor, ContainingType: { IsRecord: true } record, Parameters: [var parameter] }
         && SymbolEqualityComparer.Default.Equals(parameter.Type, record);

   /// <summary>
   /// Returns the calls of the members that the compiler generates for a record, as the C# specification defines them.
   /// </summary>
   /// <remarks>
   /// == and the Equals override for object call Equals of the record type, != calls ==, and the Equals override for a
   /// base record calls the one for object. Equals of the record type and GetHashCode read EqualityContract and call
   /// those of a base record. ToString calls PrintMembers, PrintMembers calls the base record's and reads the public
   /// properties the record declares, Deconstruct reads the positional properties, and the clone method calls the copy
   /// constructor.
   /// </remarks>
   private static IEnumerable<(IMethodSymbol Member, IEnumerable<IMethodSymbol?> Callees)> GetGeneratedRecordMemberCalls(INamedTypeSymbol record)
   {
      var methods = record.GetMembers().OfType<IMethodSymbol>().ToList();
      var properties = record.GetMembers().OfType<IPropertySymbol>().ToList();
      var baseRecord = record.BaseType is { IsRecord: true } baseType ? baseType : null;

      var typedEquals = FindTypedEquals(record);
      var objectEquals = methods.FirstOrDefault(m => m is { Name: WellKnownMemberNames.ObjectEquals, Parameters: [{ Type.SpecialType: SpecialType.System_Object }] });
      var equality = methods.FirstOrDefault(m => m is { MethodKind: MethodKind.UserDefinedOperator, Name: WellKnownMemberNames.EqualityOperatorName });
      var printMembers = methods.FirstOrDefault(m => m.Name == WellKnownMemberNames.PrintMembersMethodName);
      var copyConstructor = methods.FirstOrDefault(IsRecordCopyConstructor);
      var equalityContract = properties.FirstOrDefault(p => p.Name == "EqualityContract")?.GetMethod;

      foreach (var member in methods.Where(m => m.IsImplicitlyDeclared))
      {
         IEnumerable<IMethodSymbol?> callees = member switch
         {
            { MethodKind: MethodKind.UserDefinedOperator, Name: WellKnownMemberNames.EqualityOperatorName } => [typedEquals],
            { MethodKind: MethodKind.UserDefinedOperator, Name: WellKnownMemberNames.InequalityOperatorName } => [equality],
            { Name: WellKnownMemberNames.ObjectEquals, Parameters: [{ Type.SpecialType: SpecialType.System_Object }] } => [typedEquals],
            { Name: WellKnownMemberNames.ObjectEquals, Parameters: [var parameter] } when SymbolEqualityComparer.Default.Equals(parameter.Type, record)
               => [equalityContract, baseRecord is null ? null : FindTypedEquals(baseRecord)],
            { Name: WellKnownMemberNames.ObjectEquals, Parameters: [_] } => [objectEquals],
            { Name: WellKnownMemberNames.ObjectGetHashCode, Parameters: [] }
               => [equalityContract, baseRecord?.GetMembers(WellKnownMemberNames.ObjectGetHashCode).OfType<IMethodSymbol>().FirstOrDefault(m => m.Parameters.IsEmpty)],
            { Name: WellKnownMemberNames.ObjectToString, Parameters: [] } => [printMembers],
            { Name: WellKnownMemberNames.PrintMembersMethodName } =>
            [
               baseRecord?.GetMembers(WellKnownMemberNames.PrintMembersMethodName).OfType<IMethodSymbol>().FirstOrDefault(),
               .. properties
                  .Where(p => p is { DeclaredAccessibility: Accessibility.Public, IsStatic: false, IsIndexer: false, IsOverride: false })
                  .Select(p => p.GetMethod),
            ],
            { Name: WellKnownMemberNames.DeconstructMethodName } => member.Parameters.Select(p => FindProperty(record, p.Name)?.GetMethod),
            { Name: RecordCloneMethodName } => [copyConstructor],
            _ => []
         };

         yield return (member, callees);
      }
   }

   // The metadata name the compiler gives a record's clone method, which a with expression calls.
   private const string RecordCloneMethodName = "<Clone>$";

   private static IMethodSymbol? FindTypedEquals(INamedTypeSymbol record)
      => record.GetMembers(WellKnownMemberNames.ObjectEquals).OfType<IMethodSymbol>()
         .FirstOrDefault(m => m.Parameters is [var parameter] && SymbolEqualityComparer.Default.Equals(parameter.Type, record));

   /// <summary>
   /// Returns the property with the name that the type declares, else the nearest one that it inherits, or null when there
   /// is none. A record's generated Deconstruct reads the property that this returns for each parameter's name.
   /// </summary>
   internal static IPropertySymbol? FindProperty(INamedTypeSymbol type, string name)
   {
      for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
      {
         if (current.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault() is { } property)
            return property;
      }

      return null;
   }

   /// <summary>
   /// Returns the methods that the operation calls, outside the local functions declared in it.
   /// </summary>
   private static IEnumerable<IMethodSymbol?> GetCalledMethods(IOperation body, SemanticModel semanticModel)
   {
      var pending = new Stack<IOperation>();
      pending.Push(body);
      while (pending.TryPop(out var operation))
      {
         foreach (var called in GetMethodsCalledBy(operation, semanticModel))
            yield return called;

         // The operand of nameof is only named, never evaluated.
         if (operation is INameOfOperation)
            continue;

         foreach (var child in operation.ChildOperations)
         {
            if (child is not ILocalFunctionOperation)
               pending.Push(child);
         }
      }
   }

   /// <summary>
   /// Returns the methods in a strongly connected component of two or more methods, or with a call to themselves.
   /// </summary>
   /// <remarks>
   /// This is Tarjan's algorithm with an explicit stack, so a deep call chain cannot overflow the thread's stack.
   /// </remarks>
   private static FrozenSet<IMethodSymbol> FindCycleMembers(
      List<IMethodSymbol> methods,
      List<(int Caller, int Callee)> calls,
      CancellationToken cancellationToken)
   {
      var count = methods.Count;

      // The callees of method i are callees[firstCall[i]..firstCall[i + 1]].
      var firstCall = new int[count + 1];
      foreach (var (caller, _) in calls)
         firstCall[caller + 1]++;
      for (var i = 0; i < count; i++)
         firstCall[i + 1] += firstCall[i];

      var callees = new int[calls.Count];
      var nextSlot = firstCall[..count];
      var callsItself = new bool[count];
      foreach (var (caller, callee) in calls)
      {
         callees[nextSlot[caller]++] = callee;
         callsItself[caller] |= caller == callee;
      }

      var discoveryOrder = new int[count];
      Array.Fill(discoveryOrder, -1);
      var lowLink = new int[count];
      var onComponentStack = new bool[count];
      var componentStack = new Stack<int>();
      var path = new Stack<(int Method, int NextCall)>();
      var component = new List<int>();
      var members = new List<IMethodSymbol>();
      var discovered = 0;

      for (var start = 0; start < count; start++)
      {
         if (discoveryOrder[start] >= 0)
            continue;

         cancellationToken.ThrowIfCancellationRequested();
         Discover(start);
         while (path.TryPop(out var frame))
         {
            var (method, nextCall) = frame;
            if (nextCall < firstCall[method + 1])
            {
               path.Push((method, nextCall + 1));
               var callee = callees[nextCall];
               if (discoveryOrder[callee] < 0)
                  Discover(callee);
               else if (onComponentStack[callee])
                  lowLink[method] = Math.Min(lowLink[method], discoveryOrder[callee]);
               continue;
            }

            if (path.TryPeek(out var caller))
               lowLink[caller.Method] = Math.Min(lowLink[caller.Method], lowLink[method]);
            if (lowLink[method] == discoveryOrder[method])
               CloseComponent(method);
         }
      }

      return members.ToFrozenSet<IMethodSymbol>(SymbolEqualityComparer.Default);

      void Discover(int method)
      {
         discoveryOrder[method] = lowLink[method] = discovered++;
         componentStack.Push(method);
         onComponentStack[method] = true;
         path.Push((method, firstCall[method]));
      }

      void CloseComponent(int root)
      {
         component.Clear();
         int member;
         do
         {
            member = componentStack.Pop();
            onComponentStack[member] = false;
            component.Add(member);
         }
         while (member != root);

         if (component.Count >= 2 || callsItself[root])
            members.AddRange(component.Select(m => methods[m]));
      }
   }

   /// <summary>
   /// Returns the methods that one operation calls, without those of its child operations.
   /// </summary>
   private static IEnumerable<IMethodSymbol?> GetMethodsCalledBy(IOperation operation, SemanticModel semanticModel) => operation switch
   {
      IInvocationOperation invocation => [invocation.TargetMethod],
      IObjectCreationOperation creation => [creation.Constructor],
      IBinaryOperation binary => [binary.OperatorMethod, GetShortCircuitOperator(binary)],
      IUnaryOperation unary => [unary.OperatorMethod],
      IConversionOperation conversion => [conversion.OperatorMethod],
      ICompoundAssignmentOperation compoundAssignment => [compoundAssignment.OperatorMethod],
      IIncrementOrDecrementOperation incrementOrDecrement => [incrementOrDecrement.OperatorMethod],
      IPropertyReferenceOperation reference => GetAccessorsCalled(reference.Property, reference),
      IImplicitIndexerReferenceOperation reference =>
      [
         (reference.LengthSymbol as IPropertySymbol)?.GetMethod,
         .. reference.IndexerSymbol switch
         {
            IPropertySymbol indexer => GetAccessorsCalled(indexer, reference),
            IMethodSymbol slice => [slice],
            _ => []
         },
      ],
      IEventAssignmentOperation { EventReference: IEventReferenceOperation { Event: var @event } } assignment
         => [assignment.Adds ? @event.AddMethod : @event.RemoveMethod],
      IForEachLoopOperation { Syntax: CommonForEachStatementSyntax loop } => GetForEachCalls(loop, semanticModel),
      IAwaitOperation { Syntax: AwaitExpressionSyntax awaitExpression } => GetAwaitCalls(semanticModel.GetAwaitExpressionInfo(awaitExpression)),
      IDeconstructionAssignmentOperation { Syntax: AssignmentExpressionSyntax assignment }
         => GetDeconstructionCalls(semanticModel.GetDeconstructionInfo(assignment)),
      IRecursivePatternOperation pattern => [pattern.DeconstructSymbol as IMethodSymbol],
      ICollectionExpressionOperation collection => [collection.ConstructMethod],
      IWithOperation with => [with.CloneMethod],
      _ => []
   };

   /// <summary>
   /// Returns the false operator that a user-defined &amp;&amp; calls, or the true operator that a user-defined || calls, or
   /// null for any other binary operation.
   /// </summary>
   /// <remarks>
   /// The C# specification evaluates x &amp;&amp; y as T.false(x) ? x : T.&amp;(x, y), where T declares the &amp; operator in use.
   /// </remarks>
   private static IMethodSymbol? GetShortCircuitOperator(IBinaryOperation binary)
   {
      var name = binary.OperatorKind switch
      {
         BinaryOperatorKind.ConditionalAnd => WellKnownMemberNames.FalseOperatorName,
         BinaryOperatorKind.ConditionalOr => WellKnownMemberNames.TrueOperatorName,
         _ => null
      };

      return name is not null && binary.OperatorMethod is { ContainingType: { } type }
         ? type.GetMembers(name).OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.Parameters is [var parameter] && SymbolEqualityComparer.Default.Equals(parameter.Type, type))
         : null;
   }

   /// <summary>
   /// Returns the accessors that a property or indexer reference calls: the getter to read, the setter to write, and
   /// both for a compound assignment, ++ or --.
   /// </summary>
   /// <remarks>
   /// A ref-returning property has no setter, so a write calls its getter.
   /// </remarks>
   private static IEnumerable<IMethodSymbol?> GetAccessorsCalled(IPropertySymbol property, IOperation reference)
   {
      var target = reference;
      while (target.Parent is ITupleOperation tuple)
         target = tuple;

      var (reads, writes) = target.Parent switch
      {
         ISimpleAssignmentOperation { Target: var assigned } when assigned == target => (false, true),
         IDeconstructionAssignmentOperation { Target: var assigned } when assigned == target => (false, true),
         ICompoundAssignmentOperation { Target: var assigned } when assigned == target => (true, true),
         ICoalesceAssignmentOperation { Target: var assigned } when assigned == target => (true, true),
         IIncrementOrDecrementOperation { Target: var assigned } when assigned == target => (true, true),
         _ => (true, false)
      };

      return [reads || property.ReturnsByRef ? property.GetMethod : null, writes ? property.SetMethod : null];
   }

   private static IEnumerable<IMethodSymbol?> GetForEachCalls(CommonForEachStatementSyntax loop, SemanticModel semanticModel)
   {
      var info = semanticModel.GetForEachStatementInfo(loop);
      IEnumerable<IMethodSymbol?> calls =
      [
         info.GetEnumeratorMethod,
         info.MoveNextMethod,
         info.CurrentProperty?.GetMethod,
         info.DisposeMethod,
         info.CurrentConversion.MethodSymbol,
         info.ElementConversion.MethodSymbol,
      ];

      return loop is ForEachVariableStatementSyntax deconstruction
         ? [.. calls, .. GetDeconstructionCalls(semanticModel.GetDeconstructionInfo(deconstruction))]
         : calls;
   }

   private static IEnumerable<IMethodSymbol?> GetAwaitCalls(AwaitExpressionInfo info)
      => [info.GetAwaiterMethod, info.IsCompletedProperty?.GetMethod, info.GetResultMethod];

   private static IEnumerable<IMethodSymbol?> GetDeconstructionCalls(DeconstructionInfo info)
      => [info.Method, info.Conversion?.MethodSymbol, .. info.Nested.SelectMany(GetDeconstructionCalls)];

   /// <summary>
   /// Returns the method that stands for a call target or a declared method in the call graph.
   /// </summary>
   /// <remarks>
   /// All forms of one method map to one node: a constructed generic method, a reduced extension method, an extension
   /// block member and its implementation method, and both parts of a partial method.
   /// </remarks>
   private static IMethodSymbol GetCallGraphNode(IMethodSymbol method)
   {
      var definition = (method.ReducedFrom ?? method).OriginalDefinition;
      definition = definition.AssociatedExtensionImplementation ?? definition;
      return definition.PartialImplementationPart ?? definition;
   }

   public static List<string> DetectCycles(Dictionary<string, List<string>> graph)
   {
      var cycles = new List<string>();
      var visited = new HashSet<string>();
      var recursionStack = new HashSet<string>();

      foreach (var node in graph.Keys)
      {
         if (DetectCyclesDfs(node, graph, visited, recursionStack, new List<string>(), cycles))
         {
            // Cycle detected
         }
      }

      return cycles;
   }

   private static bool DetectCyclesDfs(
      string node,
      Dictionary<string, List<string>> graph,
      HashSet<string> visited,
      HashSet<string> recursionStack,
      List<string> path,
      List<string> cycles)
   {
      if (recursionStack.Contains(node))
      {
         var cycleStart = path.IndexOf(node);
         var cycle = string.Join(" -> ", path.Skip(cycleStart).Append(node));
         cycles.Add(cycle);
         return true;
      }

      if (visited.Contains(node))
         return false;

      visited.Add(node);
      recursionStack.Add(node);
      path.Add(node);

      if (graph.TryGetValue(node, out var neighbors))
      {
         foreach (var neighbor in neighbors)
         {
            DetectCyclesDfs(neighbor, graph, visited, recursionStack, path, cycles);
         }
      }

      recursionStack.Remove(node);
      path.RemoveAt(path.Count - 1);
      return false;
   }

   public static string GenerateMermaidGraph(Dictionary<string, List<string>> graph)
   {
      var lines = new List<string> { "graph TD" };

      foreach (var (project, dependencies) in graph)
      {
         var safeName = project.Replace(".", "_").Replace("-", "_");
         lines.Add($"    {safeName}[\"{project}\"]");

         foreach (var dep in dependencies)
         {
            var safeDep = dep.Replace(".", "_").Replace("-", "_");
            lines.Add($"    {safeName} --> {safeDep}");
         }
      }

      return string.Join("\n", lines);
   }
}
