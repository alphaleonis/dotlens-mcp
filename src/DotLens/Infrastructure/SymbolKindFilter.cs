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
/// Filter for member kinds in type member listing operations.
/// Subset of SymbolKindFilter containing only member kinds.
/// </summary>
public enum MemberKindFilter
{
   Method,
   Property,
   Field,
   Event
}
