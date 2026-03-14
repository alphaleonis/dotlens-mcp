using Microsoft.CodeAnalysis;

namespace DotLens.Tools.Shared;

/// <summary>
/// Shared SymbolDisplayFormat definitions for consistent symbol formatting across tools.
/// </summary>
public static class RoslynDisplayFormats
{
   /// <summary>
   /// Compact method signature with simple type names.
   /// Example: "public static int Calculate(int x, string name = null)"
   /// </summary>
   public static readonly SymbolDisplayFormat MethodSignatureCompact = new(
      globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
      typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
      genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
      memberOptions:
         SymbolDisplayMemberOptions.IncludeAccessibility |
         SymbolDisplayMemberOptions.IncludeModifiers |
         SymbolDisplayMemberOptions.IncludeType |
         SymbolDisplayMemberOptions.IncludeParameters,
      parameterOptions:
         SymbolDisplayParameterOptions.IncludeType |
         SymbolDisplayParameterOptions.IncludeName |
         SymbolDisplayParameterOptions.IncludeParamsRefOut |
         SymbolDisplayParameterOptions.IncludeDefaultValue,
      miscellaneousOptions:
         SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

   /// <summary>
   /// Full method signature with fully qualified type names.
   /// Example: "public static System.Int32 Calculate(System.Int32 x, System.String? name = null)"
   /// </summary>
   public static readonly SymbolDisplayFormat MethodSignatureFull = new(
      globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
      typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
      genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
      memberOptions:
         SymbolDisplayMemberOptions.IncludeAccessibility |
         SymbolDisplayMemberOptions.IncludeModifiers |
         SymbolDisplayMemberOptions.IncludeType |
         SymbolDisplayMemberOptions.IncludeParameters,
      parameterOptions:
         SymbolDisplayParameterOptions.IncludeType |
         SymbolDisplayParameterOptions.IncludeName |
         SymbolDisplayParameterOptions.IncludeParamsRefOut |
         SymbolDisplayParameterOptions.IncludeDefaultValue,
      miscellaneousOptions:
         SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
         SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
}
