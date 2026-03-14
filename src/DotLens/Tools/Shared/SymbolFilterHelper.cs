using DotLens.Infrastructure;
using Microsoft.CodeAnalysis;

namespace DotLens.Tools.Shared;

/// <summary>
/// Shared filter logic for symbol search tools (find_symbols, search_symbols, find_unused_code).
/// </summary>
public static class SymbolFilterHelper
{
   /// <summary>
   /// Checks if a symbol matches a single <see cref="SymbolKindFilter"/>.
   /// Handles both type kinds and member kinds in a unified way.
   /// </summary>
   public static bool MatchesKind(ISymbol symbol, SymbolKindFilter filter)
   {
      return filter switch
      {
         // Type kinds (records included in Class/Struct based on underlying TypeKind)
         SymbolKindFilter.Class => symbol is INamedTypeSymbol { TypeKind: TypeKind.Class },
         SymbolKindFilter.Interface => symbol is INamedTypeSymbol { TypeKind: TypeKind.Interface },
         SymbolKindFilter.Struct => symbol is INamedTypeSymbol { TypeKind: TypeKind.Struct },
         SymbolKindFilter.Enum => symbol is INamedTypeSymbol { TypeKind: TypeKind.Enum },
         SymbolKindFilter.Delegate => symbol is INamedTypeSymbol { TypeKind: TypeKind.Delegate },

         // Member kinds
         SymbolKindFilter.Method => symbol is IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation },
         SymbolKindFilter.Property => symbol.Kind == SymbolKind.Property,
         SymbolKindFilter.Field => symbol.Kind == SymbolKind.Field,
         SymbolKindFilter.Event => symbol.Kind == SymbolKind.Event,
         SymbolKindFilter.Constructor => symbol is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor },

         _ => false
      };
   }

   /// <summary>
   /// Checks if a symbol matches any of the specified kinds (OR logic).
   /// </summary>
   public static bool MatchesAnyKind(ISymbol symbol, SymbolKindFilter[] kinds)
   {
      for (int i = 0; i < kinds.Length; i++)
      {
         if (MatchesKind(symbol, kinds[i]))
            return true;
      }
      return false;
   }

   /// <summary>
   /// Checks if a symbol's declared accessibility matches the filter.
   /// Returns false for unmapped accessibility values.
   /// </summary>
   public static bool MatchesAccessibility(Accessibility symbolAccessibility, AccessibilityDto filter)
   {
      return filter switch
      {
         AccessibilityDto.Public => symbolAccessibility == Accessibility.Public,
         AccessibilityDto.Internal => symbolAccessibility == Accessibility.Internal,
         AccessibilityDto.Protected => symbolAccessibility == Accessibility.Protected,
         AccessibilityDto.ProtectedInternal => symbolAccessibility == Accessibility.ProtectedOrInternal,
         AccessibilityDto.PrivateProtected => symbolAccessibility == Accessibility.ProtectedAndInternal,
         AccessibilityDto.Private => symbolAccessibility == Accessibility.Private,
         _ => false
      };
   }

   /// <summary>
   /// Checks if a symbol has ALL specified modifiers (AND semantics).
   /// </summary>
   public static bool MatchesModifiers(ISymbol symbol, ModifierFilter[] modifiers)
   {
      var symbolModifiers = RoslynHelpers.GetSymbolModifiers(symbol);

      for (int i = 0; i < modifiers.Length; i++)
      {
         var requiredFlag = MapModifierFilterToFlag(modifiers[i]);
         if ((symbolModifiers & requiredFlag) == 0)
            return false;
      }
      return true;
   }

   /// <summary>
   /// Returns sort order for symbol kinds (types first, then members).
   /// </summary>
   public static int GetKindSortOrder(ISymbol symbol) => symbol switch
   {
      INamedTypeSymbol { TypeKind: TypeKind.Interface } => 0,
      INamedTypeSymbol { TypeKind: TypeKind.Class } => 1,
      INamedTypeSymbol { TypeKind: TypeKind.Struct } => 2,
      INamedTypeSymbol { TypeKind: TypeKind.Enum } => 3,
      INamedTypeSymbol { TypeKind: TypeKind.Delegate } => 4,
      IMethodSymbol { MethodKind: MethodKind.Constructor } => 5,
      IMethodSymbol => 6,
      IPropertySymbol => 7,
      IFieldSymbol => 8,
      IEventSymbol => 9,
      _ => 99
   };

   private static SymbolModifiers MapModifierFilterToFlag(ModifierFilter filter)
   {
      return filter switch
      {
         ModifierFilter.Static => SymbolModifiers.Static,
         ModifierFilter.Virtual => SymbolModifiers.Virtual,
         ModifierFilter.Override => SymbolModifiers.Override,
         ModifierFilter.Abstract => SymbolModifiers.Abstract,
         ModifierFilter.Sealed => SymbolModifiers.Sealed,
         ModifierFilter.Async => SymbolModifiers.Async,
         ModifierFilter.Partial => SymbolModifiers.Partial,
         ModifierFilter.ReadOnly => SymbolModifiers.ReadOnly,
         ModifierFilter.Const => SymbolModifiers.Const,
         ModifierFilter.Volatile => SymbolModifiers.Volatile,
         ModifierFilter.Extern => SymbolModifiers.Extern,
         ModifierFilter.Required => SymbolModifiers.Required,
         ModifierFilter.Record => SymbolModifiers.Record,
         _ => throw new ArgumentOutOfRangeException(nameof(filter), filter,
            $"Unknown modifier filter: {filter}")
      };
   }
}
