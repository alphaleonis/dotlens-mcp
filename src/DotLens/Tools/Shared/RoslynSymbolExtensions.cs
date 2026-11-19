using System.Collections.Immutable;
using DotLens.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotLens.Tools.Shared;

/// <summary>
/// Extension methods for Roslyn types to provide unified access to
/// override/interface implementation information and location conversion.
/// </summary>
public static class RoslynSymbolExtensions
{
   /// <summary>
   /// Converts a FileLinePositionSpan to a SourceSpan with formatted path.
   /// </summary>
   /// <param name="lineSpan">The Roslyn line span to convert.</param>
   /// <param name="formatPath">Function to format the file path (e.g., workspace.FormatPath).</param>
   public static SourceSpan ToSourceSpan(this FileLinePositionSpan lineSpan, Func<string?, string> formatPath)
   {
      return new SourceSpan
      {
         FilePath = formatPath(lineSpan.Path),
         StartLine = lineSpan.StartLinePosition.Line + 1,
         StartColumn = lineSpan.StartLinePosition.Character + 1,
         EndLine = lineSpan.EndLinePosition.Line + 1,
         EndColumn = lineSpan.EndLinePosition.Character + 1
      };
   }

   /// <summary>
   /// Converts a nullable FileLinePositionSpan to a nullable SourceSpan.
   /// </summary>
   public static SourceSpan? ToSourceSpan(this FileLinePositionSpan? lineSpan, Func<string?, string> formatPath)
   {
      return lineSpan?.ToSourceSpan(formatPath);
   }

   /// <summary>
   /// Converts a FileLinePositionSpan to a SourceSpan with path formatting options.
   /// </summary>
   /// <param name="lineSpan">The Roslyn line span to convert.</param>
   /// <param name="solutionPath">Path to the solution file for relative path calculation.</param>
   /// <param name="useAbsolutePaths">Whether to use absolute paths.</param>
   public static SourceSpan ToSourceSpan(this FileLinePositionSpan lineSpan, string? solutionPath, bool useAbsolutePaths)
   {
      return new SourceSpan
      {
         FilePath = PathUtils.FormatPath(lineSpan.Path, solutionPath, useAbsolutePaths),
         StartLine = lineSpan.StartLinePosition.Line + 1,
         StartColumn = lineSpan.StartLinePosition.Character + 1,
         EndLine = lineSpan.EndLinePosition.Line + 1,
         EndColumn = lineSpan.EndLinePosition.Character + 1
      };
   }

   /// <summary>
   /// Converts a FileLinePositionSpan to a SymbolLocation (start position only) with formatted path.
   /// </summary>
   /// <param name="lineSpan">The Roslyn line span to convert.</param>
   /// <param name="formatPath">Function to format the file path (e.g., workspace.FormatPath).</param>
   public static SymbolLocation ToSymbolLocation(this FileLinePositionSpan lineSpan, Func<string?, string> formatPath)
   {
      return new SymbolLocation
      {
         FilePath = formatPath(lineSpan.Path),
         Line = lineSpan.StartLinePosition.Line + 1,
         Column = lineSpan.StartLinePosition.Character + 1
      };
   }

   /// <summary>
   /// Converts a nullable FileLinePositionSpan to a nullable SymbolLocation.
   /// </summary>
   public static SymbolLocation? ToSymbolLocation(this FileLinePositionSpan? lineSpan, Func<string?, string> formatPath)
   {
      return lineSpan?.ToSymbolLocation(formatPath);
   }

   /// <summary>
   /// Gets the overridden member for methods, properties, and events.
   /// Returns null for symbols that don't override anything or aren't overridable.
   /// </summary>
   public static ISymbol? GetOverriddenMember(this ISymbol symbol)
   {
      return symbol switch
      {
         IMethodSymbol m => m.OverriddenMethod,
         IPropertySymbol p => p.OverriddenProperty,
         IEventSymbol e => e.OverriddenEvent,
         _ => null
      };
   }

   /// <summary>
   /// Gets explicit interface implementations for methods, properties, and events.
   /// Returns empty array for symbols that don't implement interfaces explicitly.
   /// </summary>
   public static ImmutableArray<ISymbol> GetExplicitInterfaceImplementations(this ISymbol symbol)
   {
      return symbol switch
      {
         IMethodSymbol m => m.ExplicitInterfaceImplementations.CastArray<ISymbol>(),
         IPropertySymbol p => p.ExplicitInterfaceImplementations.CastArray<ISymbol>(),
         IEventSymbol e => e.ExplicitInterfaceImplementations.CastArray<ISymbol>(),
         _ => ImmutableArray<ISymbol>.Empty
      };
   }

   /// <summary>
   /// Checks if a method symbol is a "callable" method that tools should work with.
   /// Includes ordinary methods, constructors, destructors, operators, conversions,
   /// local functions, and explicit interface implementations.
   /// Excludes property/event accessors, anonymous functions, and compiler-generated methods.
   /// </summary>
   public static bool IsCallableMethod(this IMethodSymbol method)
   {
      return method.MethodKind is MethodKind.Ordinary
         or MethodKind.Constructor
         or MethodKind.StaticConstructor
         or MethodKind.Destructor
         or MethodKind.UserDefinedOperator
         or MethodKind.Conversion
         or MethodKind.LocalFunction
         or MethodKind.ExplicitInterfaceImplementation;
   }

   /// <summary>
   /// Returns whether the symbol is the method that the compiler generates to hold a file's top-level statements.
   /// </summary>
   /// <remarks>
   /// C# source cannot declare a method with its name, and the method's declaration is the whole file.
   /// </remarks>
   public static bool IsTopLevelStatementsEntryPoint(this ISymbol symbol) =>
      symbol is IMethodSymbol { Name: WellKnownMemberNames.TopLevelStatementsEntryPointMethodName } method
      && method.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is CompilationUnitSyntax);

   /// <summary>
   /// Checks if a syntax node is a declaration for a "callable" method.
   /// This is the syntax-level equivalent of <see cref="IsCallableMethod(IMethodSymbol)"/>.
   /// Includes method declarations, constructors, destructors, operators, conversions,
   /// and local functions.
   /// </summary>
   public static bool IsCallableMethodSyntax(this SyntaxNode node)
   {
      return node is MethodDeclarationSyntax
         or ConstructorDeclarationSyntax
         or DestructorDeclarationSyntax
         or OperatorDeclarationSyntax
         or ConversionOperatorDeclarationSyntax
         or LocalFunctionStatementSyntax;
   }

   /// <summary>
   /// Gets the 1-based line number from a location, or null if not in source.
   /// </summary>
   public static int? GetLineNumber(this Location location)
   {
      return location.IsInSource
         ? location.GetLineSpan().StartLinePosition.Line + 1
         : null;
   }

   /// <summary>
   /// Gets the 1-based line number from a syntax node's location, or null if not in source.
   /// </summary>
   public static int? GetLineNumber(this SyntaxNode node)
   {
      return node.GetLocation().GetLineNumber();
   }
}
