using System.Text;
using Microsoft.CodeAnalysis;

namespace DotLens.Tools.Shared;

/// <summary>
/// Shared SymbolDisplayFormat definitions for consistent symbol formatting across tools.
/// </summary>
public static class RoslynDisplayFormats
{
   /// <summary>
   /// Compact method signature with simple type names.
   /// Example: "public static int Calculate(int x, string? name = null)"
   /// </summary>
   public static readonly SymbolDisplayFormat MethodSignatureCompact = new(
      globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
      typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
      genericsOptions:
         SymbolDisplayGenericsOptions.IncludeTypeParameters |
         SymbolDisplayGenericsOptions.IncludeTypeConstraints |
         SymbolDisplayGenericsOptions.IncludeVariance,
      memberOptions:
         SymbolDisplayMemberOptions.IncludeAccessibility |
         SymbolDisplayMemberOptions.IncludeModifiers |
         SymbolDisplayMemberOptions.IncludeType |
         SymbolDisplayMemberOptions.IncludeRef |
         SymbolDisplayMemberOptions.IncludeExplicitInterface |
         SymbolDisplayMemberOptions.IncludeParameters,
      parameterOptions:
         SymbolDisplayParameterOptions.IncludeExtensionThis |
         SymbolDisplayParameterOptions.IncludeType |
         SymbolDisplayParameterOptions.IncludeName |
         SymbolDisplayParameterOptions.IncludeParamsRefOut |
         SymbolDisplayParameterOptions.IncludeDefaultValue,
      miscellaneousOptions:
         SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
         SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

   /// <summary>
   /// Full method signature with fully qualified type names.
   /// Example: "public static int Calculate(int x, System.Text.StringBuilder? builder = null)"
   /// </summary>
   public static readonly SymbolDisplayFormat MethodSignatureFull = new(
      globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
      typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
      genericsOptions: MethodSignatureCompact.GenericsOptions,
      memberOptions: MethodSignatureCompact.MemberOptions,
      parameterOptions: MethodSignatureCompact.ParameterOptions,
      miscellaneousOptions: MethodSignatureCompact.MiscellaneousOptions);

   /// <summary>
   /// Returns the symbol's declaration in <paramref name="format"/>, with default values written as C# source writes them.
   /// </summary>
   /// <remarks>
   /// Roslyn's display leaves enum members unqualified, drops the <c>m</c> and <c>f</c> suffixes and writes
   /// <c>NaN</c> and <c>Infinity</c> bare. An explicit interface implementation shows no accessibility, as in source.
   /// </remarks>
   public static string ToDeclarationString(ISymbol symbol, SymbolDisplayFormat format)
   {
      if (IsExplicitInterfaceImplementation(symbol))
         format = format.RemoveMemberOptions(SymbolDisplayMemberOptions.IncludeAccessibility);

      // Roslyn writes an explicitly implemented conversion as "static int IConv<C>.explicit operator int(C v)".
      var isExplicitConversion = symbol is IMethodSymbol { ExplicitInterfaceImplementations: [{ MethodKind: MethodKind.Conversion }, ..] };
      if (isExplicitConversion)
         format = format.RemoveMemberOptions(SymbolDisplayMemberOptions.IncludeType);

      var parts = symbol.ToDisplayParts(format).ToList();
      if (isExplicitConversion)
         MoveConversionKeywordBeforeInterface(parts);

      var text = new StringBuilder();
      IParameterSymbol? parameter = null;
      foreach (var part in parts)
      {
         switch (part.Kind)
         {
            case SymbolDisplayPartKind.ParameterName:
               parameter = part.Symbol as IParameterSymbol;
               text.Append(part.ToString());
               break;

            // An enum member that is not the displayed symbol is a constant in a default value.
            case SymbolDisplayPartKind.EnumMemberName
               when part.Symbol is IFieldSymbol { ContainingType: { } enumType } member
                  && !SymbolEqualityComparer.Default.Equals(member, symbol):
               text.Append(enumType.ToDisplayString(format)).Append('.').Append(part.ToString());
               break;

            // Only a default value displays a numeric literal, so it belongs to the last parameter named.
            case SymbolDisplayPartKind.NumericLiteral when parameter is not null:
               text.Append(FormatNumericDefault(part.ToString(), parameter.Type, format));
               break;

            default:
               text.Append(part.ToString());
               break;
         }
      }

      return text.ToString();
   }

   /// <summary>
   /// Moves the <c>explicit</c> or <c>implicit</c> keyword and the space after it in front of the interface that
   /// qualifies the operator, as in <c>static explicit IConv&lt;C&gt;.operator int(C v)</c>.
   /// </summary>
   private static void MoveConversionKeywordBeforeInterface(List<SymbolDisplayPart> parts)
   {
      var keyword = parts.FindIndex(p => p.Kind == SymbolDisplayPartKind.Keyword && p.ToString() is "explicit" or "implicit");
      if (keyword < 1 || keyword + 1 >= parts.Count)
         return;

      // The interface starts after the last space outside its type argument list.
      var interfaceStart = 0;
      var depth = 0;
      for (var i = keyword - 1; i >= 0; i--)
      {
         var text = parts[i].ToString();
         if (text == ">")
            depth++;
         else if (text == "<")
            depth--;
         else if (depth == 0 && parts[i].Kind == SymbolDisplayPartKind.Space)
         {
            interfaceStart = i + 1;
            break;
         }
      }

      var moved = parts.GetRange(keyword, 2);
      parts.RemoveRange(keyword, 2);
      parts.InsertRange(interfaceStart, moved);
   }

   private static bool IsExplicitInterfaceImplementation(ISymbol symbol) => symbol switch
   {
      IMethodSymbol method => method.ExplicitInterfaceImplementations.Length > 0,
      IPropertySymbol property => property.ExplicitInterfaceImplementations.Length > 0,
      IEventSymbol @event => @event.ExplicitInterfaceImplementations.Length > 0,
      _ => false,
   };

   private static string FormatNumericDefault(string literal, ITypeSymbol type, SymbolDisplayFormat format)
   {
      if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments: [var underlying] })
         type = underlying;

      return type.SpecialType switch
      {
         SpecialType.System_Decimal => literal + "m",
         SpecialType.System_Single or SpecialType.System_Double when literal == "NaN" => $"{type.ToDisplayString(format)}.NaN",
         SpecialType.System_Single or SpecialType.System_Double when literal == "Infinity" => $"{type.ToDisplayString(format)}.PositiveInfinity",
         SpecialType.System_Single or SpecialType.System_Double when literal == "-Infinity" => $"{type.ToDisplayString(format)}.NegativeInfinity",
         SpecialType.System_Single => literal + "f",
         _ => literal,
      };
   }
}
