using Microsoft.CodeAnalysis;

namespace DotLens.Infrastructure;

/// <summary>
/// Helper for analyzing missing interface and abstract members on a type.
/// Shared by GetMissingMembersTool and ImplementMissingMembersTool.
/// </summary>
public static class MissingMemberAnalyzer
{
   /// <summary>
   /// Checks if the type has any missing interface or abstract members.
   /// </summary>
   public static bool HasMissingMembers(INamedTypeSymbol typeSymbol)
   {
      return GetMissingMembers(typeSymbol).Any();
   }

   /// <summary>
   /// Gets all missing interface and abstract members for a type.
   /// </summary>
   public static IEnumerable<MissingMember> GetMissingMembers(INamedTypeSymbol typeSymbol)
   {
      var implementedMembers = typeSymbol.GetMembers().ToHashSet<ISymbol>(SymbolEqualityComparer.Default);

      // Check interface members
      foreach (var iface in typeSymbol.AllInterfaces)
      {
         foreach (var member in iface.GetMembers())
         {
            if (member.IsStatic || member is IMethodSymbol { MethodKind: not MethodKind.Ordinary })
               continue;

            var implementation = typeSymbol.FindImplementationForInterfaceMember(member);
            if (implementation == null || !implementedMembers.Contains(implementation))
            {
               yield return new MissingMember(member, iface, IsAbstract: false);
            }
         }
      }

      // Check abstract base class members
      var baseType = typeSymbol.BaseType;
      while (baseType != null && baseType.SpecialType != SpecialType.System_Object)
      {
         foreach (var member in baseType.GetMembers())
         {
            if (!member.IsAbstract)
               continue;

            var isOverridden = typeSymbol.GetMembers(member.Name)
               .Any(m =>
               {
                  var overridden = m.GetOverriddenMember();
                  return overridden != null &&
                         SymbolEqualityComparer.Default.Equals(overridden, member);
               });

            if (!isOverridden)
            {
               yield return new MissingMember(member, baseType, IsAbstract: true);
            }
         }
         baseType = baseType.BaseType;
      }
   }

   /// <summary>
   /// Represents a missing member that needs to be implemented.
   /// </summary>
   /// <param name="Member">The missing member symbol.</param>
   /// <param name="SourceType">The interface or base class where the member is declared.</param>
   /// <param name="IsAbstract">True if from abstract base class, false if from interface.</param>
   public readonly record struct MissingMember(
      ISymbol Member,
      INamedTypeSymbol SourceType,
      bool IsAbstract);
}
