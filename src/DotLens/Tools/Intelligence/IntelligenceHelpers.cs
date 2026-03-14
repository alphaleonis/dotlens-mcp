using System.ComponentModel;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotLens.Tools.Intelligence;

/// <summary>
/// Shared helper methods for intelligence tools.
/// </summary>
internal static class IntelligenceHelpers
{
   public static bool IsBaseType(ITypeSymbol derived, ITypeSymbol potentialBase)
   {
      var current = derived.BaseType;
      while (current != null)
      {
         if (SymbolEqualityComparer.Default.Equals(current, potentialBase))
            return true;
         current = current.BaseType;
      }
      return false;
   }

   public static string GetConversionKind(Conversion conversion)
   {
      if (conversion.IsIdentity)
         return "Identity";
      if (conversion.IsImplicit && conversion.IsReference)
         return "ImplicitReference";
      if (conversion.IsImplicit && conversion.IsBoxing)
         return "Boxing";
      if (conversion.IsImplicit && conversion.IsNumeric)
         return "ImplicitNumeric";
      if (conversion.IsExplicit && conversion.IsUnboxing)
         return "Unboxing";
      if (conversion.IsExplicit && conversion.IsNumeric)
         return "ExplicitNumeric";
      if (conversion.IsExplicit)
         return "Explicit";
      if (conversion.IsImplicit)
         return "Implicit";
      return "None";
   }

   public static (bool IsSafe, string? BreakingChangeReason, List<string> Suggestions) AnalyzeImpact(
      ISymbol symbol, ChangeType changeType, string? newValue, int referenceCount)
   {
      var suggestions = new List<string>();

      switch (changeType)
      {
         case ChangeType.Rename:
            suggestions.Add("Use rename_symbol for safe cross-solution rename");
            return (true, null, suggestions);

         case ChangeType.ChangeType:
            if (referenceCount > 0)
            {
               suggestions.Add("Review all call sites for type compatibility");
               return (false, "Changing type may break existing callers", suggestions);
            }
            return (true, null, suggestions);

         case ChangeType.AddParameter:
            if (referenceCount > 0)
            {
               suggestions.Add("Add default value to maintain backward compatibility");
               suggestions.Add("Or update all call sites");
               return (false, "Adding required parameter breaks existing callers", suggestions);
            }
            return (true, null, suggestions);

         case ChangeType.RemoveParameter:
            if (referenceCount > 0)
            {
               suggestions.Add("Update all call sites to remove the argument");
               return (false, "Removing parameter breaks existing callers", suggestions);
            }
            return (true, null, suggestions);

         case ChangeType.ChangeAccessibility:
            if (referenceCount > 0 && symbol.DeclaredAccessibility == Accessibility.Public)
            {
               suggestions.Add("Review all call sites for accessibility compatibility");
               return (false, "Reducing accessibility may break external callers", suggestions);
            }
            return (true, null, suggestions);

         case ChangeType.Delete:
            if (referenceCount > 0)
            {
               suggestions.Add("Remove or update all references first");
               return (false, $"Symbol has {referenceCount} references", suggestions);
            }
            return (true, null, suggestions);

         default:
            suggestions.Add("Review impact manually");
            return (true, null, suggestions);
      }
   }

   /// <summary>
   /// Gets deduplicated outgoing calls (unique symbols only).
   /// </summary>
   public static async Task<List<ISymbol>> GetOutgoingCallsForMethodAsync(
      IMethodSymbol method,
      Solution solution,
      int maxCalls,
      CancellationToken cancellationToken)
   {
      var (_, calls) = await EnumerateOutgoingCallsAsync(method, solution, maxCalls, cancellationToken);

      return calls
         .GroupBy(c => c.Symbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
         .Select(g => g.First().Symbol)
         .ToList();
   }

   /// <summary>
   /// Gets outgoing calls with their call site locations (no deduplication - shows all call sites).
   /// Returns total count and the (possibly truncated) list of calls.
   /// </summary>
   public static async Task<(int TotalCount, List<(ISymbol Symbol, FileLinePositionSpan CallSite)> Calls)> GetOutgoingCallsWithLocationsAsync(
      IMethodSymbol method,
      Solution solution,
      int maxCalls,
      CancellationToken cancellationToken)
   {
      return await EnumerateOutgoingCallsAsync(method, solution, maxCalls, cancellationToken);
   }

   /// <summary>
   /// Core implementation that enumerates all outgoing calls from a method.
   /// Returns total count and (possibly truncated) list of calls with their syntax nodes.
   /// </summary>
   private static async Task<(int TotalCount, List<(ISymbol Symbol, FileLinePositionSpan CallSite)> Calls)> EnumerateOutgoingCallsAsync(
      IMethodSymbol method,
      Solution solution,
      int maxCalls,
      CancellationToken cancellationToken)
   {
      var calls = new List<(ISymbol Symbol, FileLinePositionSpan CallSite)>();
      var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
      if (syntaxRef == null)
         return (0, []);

      var methodSyntax = await syntaxRef.GetSyntaxAsync(cancellationToken);
      var document = solution.GetDocument(syntaxRef.SyntaxTree);
      if (document == null)
         return (0, []);

      var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
      if (semanticModel == null)
         return (0, []);

      int totalCount = 0;

      foreach (var node in methodSyntax.DescendantNodes())
      {
         var (calledSymbol, callNode) = ResolveCallSymbol(node, semanticModel, cancellationToken);

         if (calledSymbol != null && callNode != null && !calledSymbol.IsImplicitlyDeclared)
         {
            totalCount++;
            if (calls.Count < maxCalls)
            {
               var lineSpan = callNode.GetLocation().GetLineSpan();
               calls.Add((calledSymbol, lineSpan));
            }
         }
      }

      return (totalCount, calls);
   }

   /// <summary>
   /// Resolves a syntax node to its called symbol if it represents a call site.
   /// </summary>
   private static (ISymbol? Symbol, SyntaxNode? CallNode) ResolveCallSymbol(
      SyntaxNode node,
      SemanticModel semanticModel,
      CancellationToken cancellationToken)
   {
      if (node is InvocationExpressionSyntax invocation)
      {
         return (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol, invocation);
      }

      if (node is ObjectCreationExpressionSyntax objectCreation)
      {
         return (semanticModel.GetSymbolInfo(objectCreation, cancellationToken).Symbol, objectCreation);
      }

      // Target-typed new expression: new(args) - C# 9+
      if (node is ImplicitObjectCreationExpressionSyntax implicitObjectCreation)
      {
         return (semanticModel.GetSymbolInfo(implicitObjectCreation, cancellationToken).Symbol, implicitObjectCreation);
      }

      if (node is MemberAccessExpressionSyntax memberAccess)
      {
         var symbol = semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol;
         if (symbol is IPropertySymbol or IEventSymbol)
            return (symbol, memberAccess);
      }

      if (node is ElementAccessExpressionSyntax elementAccess)
      {
         var symbol = semanticModel.GetSymbolInfo(elementAccess, cancellationToken).Symbol;
         if (symbol is IPropertySymbol)
            return (symbol, elementAccess);
      }

      return (null, null);
   }

   public static IEnumerable<ISymbol> GetInheritedMembers(INamedTypeSymbol type)
   {
      var baseType = type.BaseType;
      while (baseType != null && baseType.SpecialType != SpecialType.System_Object)
      {
         foreach (var member in baseType.GetMembers())
         {
            if (!member.IsStatic && member.DeclaredAccessibility != Accessibility.Private)
               yield return member;
         }
         baseType = baseType.BaseType;
      }
   }
}

