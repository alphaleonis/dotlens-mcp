namespace CSharp15TestProject;

public static class CatalogExtensions
{
   /// <summary>
   /// Factories for <see cref="Catalog"/>.
   /// </summary>
   extension(Catalog)
   {
      public static Catalog Empty() => new();
   }

   /**
    * Lookups that fill in missing values.
    */
   extension<TKey, TValue>(IDictionary<TKey, TValue> map)
      where TKey : notnull
      where TValue : class, new()
   {
      public TValue GetOrCreate(TKey key) => map.TryGetValue(key, out var value) ? value : map[key] = new TValue();

      private int Spare => map.Count;
   }
}
