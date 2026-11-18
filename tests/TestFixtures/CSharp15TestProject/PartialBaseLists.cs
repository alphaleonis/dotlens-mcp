using Disposable = System.IDisposable;

namespace CSharp15TestProject;

public class LedgerBase(int seed)
{
   public int Seed { get; } = seed;
}

public partial class MergedLedger : IComparable<int>
{
   public int CompareTo(int other) => 0;
}

public partial class MergedLedger(int seed) : LedgerBase(seed), System.IComparable<int>, Disposable
{
   public void Dispose() { }
}

public abstract record Entry(int Id);

public partial record PostedEntry(int Id) : Entry(Id);

public partial record PostedEntry : Entry, IEquatable<PostedEntry>;

public partial record DraftEntry(int Id) : Entry(Id), IComparable<DraftEntry>;

public partial record DraftEntry : Entry
{
   public int CompareTo(DraftEntry? other) => 0;
}

public static class ConstructedUses
{
   public static Inventory<string> Stock() => new();

   public static List<int> Numbers() => [];

   public static object Anything() => new();
}
