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
   /// Only direct recursion counts: a cycle through other methods needs a call graph beyond the measured node.
   /// </remarks>
   public static int CalculateCognitiveComplexity(SyntaxNode node, SemanticModel semanticModel, CancellationToken cancellationToken)
   {
      int complexity = 0;
      CalculateCognitiveComplexityRecursive(node, 0, ref complexity);
      return complexity + CountDirectlyRecursiveMethods(node, semanticModel, cancellationToken);
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
   /// Counts the methods declared in the node, the node itself and its local functions, that call themselves.
   /// </summary>
   private static int CountDirectlyRecursiveMethods(SyntaxNode node, SemanticModel semanticModel, CancellationToken cancellationToken)
   {
      return node.DescendantNodesAndSelf()
         .Where(n => n.IsCallableMethodSyntax())
         .Count(declaration =>
            semanticModel.GetDeclaredSymbol(declaration, cancellationToken) is IMethodSymbol method
            && semanticModel.GetOperation(declaration, cancellationToken) is { } body
            && body.Descendants().Any(operation => GetCalledMethod(operation) is { } called && IsSameMethod(called, method)));
   }

   private static IMethodSymbol? GetCalledMethod(IOperation operation) => operation switch
   {
      IInvocationOperation invocation => invocation.TargetMethod,
      IObjectCreationOperation creation => creation.Constructor,
      IBinaryOperation binary => binary.OperatorMethod,
      IUnaryOperation unary => unary.OperatorMethod,
      IConversionOperation conversion => conversion.OperatorMethod,
      ICompoundAssignmentOperation compoundAssignment => compoundAssignment.OperatorMethod,
      IIncrementOrDecrementOperation incrementOrDecrement => incrementOrDecrement.OperatorMethod,
      _ => null
   };

   // A call names the defining part of a partial method, while the declaration with the body has the implementation part.
   private static bool IsSameMethod(IMethodSymbol called, IMethodSymbol method)
   {
      var definition = called.OriginalDefinition;
      return SymbolEqualityComparer.Default.Equals(definition, method)
         || SymbolEqualityComparer.Default.Equals(definition.PartialImplementationPart, method);
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
