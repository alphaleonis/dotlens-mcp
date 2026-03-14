using System.ComponentModel;
using DotLens.Infrastructure;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Intelligence;

/// <summary>
/// Check if one type can be assigned to another.
/// </summary>
[McpServerToolType]
[DotLensWorkerTool]
public class CheckTypeCompatibilityTool
{
   private static readonly string[] SuggestedNextTools = ["get_type_members to explore type details"];
   private readonly IReadOnlyWorkspace _workspaceManager;

   public CheckTypeCompatibilityTool(IReadOnlyWorkspace workspaceManager)
   {
      _workspaceManager = workspaceManager;
   }

   [McpServerTool(Name = "check_type_compatibility", ReadOnly = true, OpenWorld = false)]
   [Description("Check if one type can be assigned to another, including conversion kind and whether a cast is required.")]
   public async Task<ToolResult<TypeCompatibilityResponse>> CheckTypeCompatibilityAsync(
      [Description("Fully-qualified source type name (e.g., 'MyNamespace.MyClass')")] string sourceType,
      [Description("Fully-qualified target type name (e.g., 'MyNamespace.IMyInterface')")] string targetType,
      CancellationToken cancellationToken)
   {
      // Look up source type with its compilation context
      // Require fully-qualified names for precise type compatibility checking
      var sourceResult = await _workspaceManager.FindTypeWithCompilationAsync(sourceType, requireFullyQualifiedName: true, cancellationToken: cancellationToken);
      if (sourceResult == null)
      {
         throw new ToolException(
            ErrorCodes.TypeNotFound,
            $"Source type not found: {sourceType}",
            "Use fully-qualified type name (e.g., 'MyNamespace.MyClass')");
      }

      var source = sourceResult.Type;
      var compilation = sourceResult.Compilation;

      // Look up target type within the source compilation to ensure correct comparison
      // This is critical: ClassifyConversion requires both types from the same compilation
      var target = compilation.GetTypeByMetadataName(targetType);
      if (target == null)
      {
         // Target not visible from source compilation - types are incompatible for assignment
         throw new ToolException(
            ErrorCodes.TypeNotFound,
            $"Target type '{targetType}' not found or not visible from source type's compilation",
            "Ensure the target type is visible from the source type's project (via project/package references), or verify the fully-qualified type name is correct");
      }

      var conversion = compilation.ClassifyConversion(source, target);

      var explanations = new List<string>();
      if (conversion.IsIdentity)
         explanations.Add("Types are identical");
      if (conversion.IsImplicit)
         explanations.Add("Implicit conversion available");
      if (conversion.IsExplicit)
         explanations.Add("Explicit cast required");
      if (conversion.IsReference)
         explanations.Add("Reference conversion");
      if (conversion.IsBoxing)
         explanations.Add("Boxing conversion");
      if (conversion.IsUnboxing)
         explanations.Add("Unboxing conversion");
      if (conversion.IsNullable)
         explanations.Add("Nullable conversion");
      if (conversion.IsUserDefined)
         explanations.Add("User-defined conversion operator");

      var isDirectlyAssignable = source.AllInterfaces.Contains(target, SymbolEqualityComparer.Default) ||
                                 IntelligenceHelpers.IsBaseType(source, target);

      return ToolResult.Create(
         new TypeCompatibilityResponse
         {
            SourceType = source.ToDisplayString(),
            TargetType = target.ToDisplayString(),
            Compatible = conversion.Exists,
            RequiresCast = conversion.IsExplicit,
            ConversionKind = IntelligenceHelpers.GetConversionKind(conversion),
            IsIdentity = conversion.IsIdentity,
            IsImplicit = conversion.IsImplicit,
            IsExplicit = conversion.IsExplicit,
            IsDirectlyAssignable = isDirectlyAssignable,
            Explanation = string.Join("; ", explanations)
         },
         SuggestedNextTools);
   }
}

/// <summary>
/// Result for CheckTypeCompatibilityTool.
/// </summary>
public record TypeCompatibilityResponse
{
   [Description("Fully-qualified name of the source type.")]
   public required string SourceType { get; init; }

   [Description("Fully-qualified name of the target type.")]
   public required string TargetType { get; init; }

   [Description("True if a conversion exists from source to target (implicit, explicit, or user-defined).")]
   public required bool Compatible { get; init; }

   [Description("True if an explicit cast is required (e.g., (TargetType)sourceValue).")]
   public required bool RequiresCast { get; init; }

   [Description("The kind of conversion: Identity, ImplicitReference, Boxing, ImplicitNumeric, Unboxing, ExplicitNumeric, Explicit, Implicit, or None.")]
   public required string ConversionKind { get; init; }

   [Description("True if the types are identical (no conversion needed).")]
   public required bool IsIdentity { get; init; }

   [Description("True if an implicit conversion is available (no cast needed).")]
   public required bool IsImplicit { get; init; }

   [Description("True if only an explicit conversion exists (cast required).")]
   public required bool IsExplicit { get; init; }

   [Description("True if source inherits from target or implements target interface (direct type relationship).")]
   public required bool IsDirectlyAssignable { get; init; }

   [Description("Human-readable explanation of the conversion (e.g., 'Implicit conversion available; Reference conversion').")]
   public required string Explanation { get; init; }
}
