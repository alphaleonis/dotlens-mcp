using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Intelligence;

[McpServerToolType]
[DotLensWorkerTool]
public class GetInstantiationOptionsTool
{
   private static readonly string[] SuggestedNextTools = ["get_type_members for all type members"];
   private readonly SymbolLookupService _symbolLookup;

   public GetInstantiationOptionsTool(SymbolLookupService symbolLookup)
   {
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "get_instantiation_options", ReadOnly = true, OpenWorld = false, Idempotent = true)]
   [Description("Get all ways to create an instance of a type: constructors, factory methods, and builder patterns.")]
   public async Task<ToolResult<InstantiationOptionsResponse>> GetInstantiationOptionsAsync(
      [Description("Type to analyze.")] SymbolLookup target,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Full,
      CancellationToken cancellationToken = default)
   {
      var lookupResult = await _symbolLookup.FindSingleAsync<INamedTypeSymbol>(target, cancellationToken: cancellationToken);
      var typeSymbol = lookupResult.Symbol;

      var constructors = typeSymbol.Constructors
         .Where(c => c.DeclaredAccessibility == Accessibility.Public && !c.IsStatic)
         .Select(c => new ConstructorOptionInfo
         {
            Signature = c.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            Parameters = verbosity == ToolResultVerbosity.Full
               ? c.Parameters.Select(p => new MethodParameterInfo
               {
                  Name = p.Name,
                  Type = p.Type.ToDisplayString(),
                  Modifiers = p.GetParameterModifiers(),
                  DefaultValue = p.GetDefaultValueString()
               }).ToList()
               : null,
            IsImplicit = c.IsImplicitlyDeclared
         })
         .ToList();

      var factoryMethods = typeSymbol.GetMembers()
         .OfType<IMethodSymbol>()
         .Where(m => m.IsStatic && m.DeclaredAccessibility == Accessibility.Public)
         .Where(m => SymbolEqualityComparer.Default.Equals(m.ReturnType, typeSymbol) ||
                    (m.ReturnType is INamedTypeSymbol named && SymbolEqualityComparer.Default.Equals(named.ConstructedFrom, typeSymbol)))
         .Select(m => new FactoryMethodInfo
         {
            Name = m.Name,
            Signature = m.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            ReturnType = verbosity == ToolResultVerbosity.Summary ? null : m.ReturnType.ToDisplayString()
         })
         .ToList();

      List<string>? hints = null;
      if (verbosity != ToolResultVerbosity.Summary)
      {
         hints = [];
         if (typeSymbol.IsAbstract)
            hints.Add("Type is abstract - cannot instantiate directly");
         if (typeSymbol.AllInterfaces.Any(i => i.ToDisplayString() == "System.IDisposable"))
            hints.Add("Implements IDisposable - remember to dispose");
         if (typeSymbol.IsRecord)
            hints.Add("Record type - can use with expression for copying");
         if (typeSymbol.TypeKind == TypeKind.Struct)
            hints.Add("Value type (struct) - has implicit default constructor");
      }

      return ToolResult.Create(
         new InstantiationOptionsResponse
         {
            TypeName = typeSymbol.ToDisplayString(),
            TypeKind = typeSymbol.TypeKind.ToTypeKindDto(),
            IsAbstract = typeSymbol.IsAbstract,
            ConstructorCount = constructors.Count,
            Constructors = verbosity == ToolResultVerbosity.Summary ? null : constructors,
            FactoryMethodCount = factoryMethods.Count,
            FactoryMethods = verbosity == ToolResultVerbosity.Summary ? null : factoryMethods,
            Hints = hints
         },
         SuggestedNextTools,
         constructors.Count + factoryMethods.Count,
         constructors.Count + factoryMethods.Count);
   }
}

#region Result Types

/// <summary>
/// Result for GetInstantiationOptionsTool.
/// </summary>
public record InstantiationOptionsResponse
{
   [Description("Fully-qualified type name.")]
   public required string TypeName { get; init; }

   [Description("Kind of type: Class, Struct, Interface, Enum, or Delegate.")]
   public required TypeKindDto TypeKind { get; init; }

   [Description("True if the type is abstract and cannot be instantiated directly.")]
   public required bool IsAbstract { get; init; }

   [Description("Number of public constructors.")]
   public required int ConstructorCount { get; init; }

   [Description("Public constructors. Null when verbosity is Summary.")]
   public IReadOnlyList<ConstructorOptionInfo>? Constructors { get; init; }

   [Description("Number of static factory methods returning this type.")]
   public required int FactoryMethodCount { get; init; }

   [Description("Static factory methods returning this type. Null when verbosity is Summary.")]
   public IReadOnlyList<FactoryMethodInfo>? FactoryMethods { get; init; }

   [Description("Usage hints (e.g., 'Implements IDisposable - remember to dispose'). Null when verbosity is Summary.")]
   public IReadOnlyList<string>? Hints { get; init; }
}

/// <summary>
/// Information about a constructor.
/// </summary>
public record ConstructorOptionInfo
{
   [Description("Constructor signature (e.g., 'MyClass(string name, int count)').")]
   public required string Signature { get; init; }

   [Description("List of parameters with names and types. Only included when verbosity is Full.")]
   public IReadOnlyList<MethodParameterInfo>? Parameters { get; init; }

   [Description("True if this is a compiler-generated default constructor.")]
   public required bool IsImplicit { get; init; }
}

/// <summary>
/// Information about a static factory method.
/// </summary>
public record FactoryMethodInfo
{
   [Description("Method name (e.g., 'Create', 'FromJson').")]
   public required string Name { get; init; }

   [Description("Full method signature.")]
   public required string Signature { get; init; }

   [Description("Return type of the factory method. Null when verbosity is Summary.")]
   public string? ReturnType { get; init; }
}

#endregion
