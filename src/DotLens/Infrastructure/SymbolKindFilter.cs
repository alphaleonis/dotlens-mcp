namespace DotLens.Infrastructure;

/// <summary>
/// Filter for symbol kinds in search operations.
/// </summary>
/// <remarks>
/// Records are included in Class or Struct filters (their underlying TypeKind).
/// To identify records, check the Record flag in SymbolModifiers on the returned symbols.
/// </remarks>
public enum SymbolKindFilter
{
   // Types
   Class,
   Interface,
   Struct,
   Enum,
   Delegate,

   // Members
   Method,
   Property,
   Field,
   Event,
   Constructor
}

/// <summary>
/// Filter for the member kinds that the type member tools report, as <see cref="RoslynHelpers.GetMemberKindString"/> names them.
/// </summary>
public enum MemberKindFilter
{
   Method,
   Property,
   Field,
   Event,
   Constructor,
   Destructor,
   Operator,
   Conversion,
   Accessor,

   /// <summary>Every nested type, whatever its type kind.</summary>
   NestedType
}
