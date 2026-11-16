namespace CSharp15TestProject;

public interface ILedger
{
   int Count { get; }
}

public class Ledger<TKey,
   TValue>(
   IDictionary<TKey, TValue> entries,
   string title = "Ledger  one") // A comment inside the parameter list.
   : ILedger,
     IDisposable
   where TKey : notnull,
      IComparable<TKey>
{
   private int _disposals;

   private event EventHandler? Disposed;

   public Ledger(
      string name,
      bool strict = 1 < 2)
      : this(
         new Dictionary<TKey, TValue>(),
         name)
   {
   }

   public int Count => entries.Count;

   public Dictionary<TResult,
      List<TValue>> Group<TResult>(
      Func<TValue, TResult> selector, /* the grouping key */
      int limit)
      where TResult : notnull
   {
      throw new NotSupportedException();
   }

   public TValue this[
      TKey key] => entries[key];

   public void Dispose()
   {
      _disposals++;
      Disposed?.Invoke(this, EventArgs.Empty);
   }
}

public delegate TResult Folder<in TItem,
   TResult>(
   TResult seed,
   TItem item)
   where TResult : struct;

public static class LedgerExtensions
{
   extension<TKey, TValue>(
      Ledger<TKey, TValue> ledger)
      where TKey : notnull, IComparable<TKey>
   {
      public bool IsEmpty => ledger.Count == 0;
   }
}
