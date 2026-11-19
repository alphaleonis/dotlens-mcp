namespace CoreTestProject.Analysis;

// Each comment gives the cognitive complexity increments of the constructs on its line.
public class CognitiveAccessorRecursion
{
   private int _value;

   public int Computed
      => ReadComputed();

   public int ReadComputed()
      => Computed; // +1 for the cycle through the expression-bodied getter

   public int Tally
   {
      get => _value;
      set => AddTally(value);
   }

   public void AddTally(int amount)
      => Tally += amount; // +1 for the cycle through the setter of the compound assignment

   public int Counter
   {
      get => _value;
      set => IncrementCounter();
   }

   public void IncrementCounter()
      => Counter++; // +1 for the cycle through the setter of ++

   public int Settable
   {
      get => _value;
      set => AssignSettable();
   }

   public void AssignSettable()
      => Settable = 1; // +1 for the cycle through the setter

   public int ReadSettable()
      => Settable; // The getter is in no cycle

   public int Seed
   {
      get => _value;
      init => _value = Plant(value);
   }

   public static int Plant(int seed)
      => new CognitiveAccessorRecursion { Seed = seed }._value; // +1 for the cycle through the init accessor

   public int Swapped
   {
      get => 0;
      set => SwapIn();
   }

   public void SwapIn()
      => (Swapped, _value) = (1, 2); // +1 for the cycle through the setter of a deconstruction target

   public int this[int index]
   {
      get => Lookup(index);
      set { }
   }

   public int Lookup(int index)
      => this[index - 1]; // +1 for the cycle through the indexer getter

   public int this[string key]
   {
      get => 0;
      set => Store(key, value);
   }

   public void Store(string key, int value)
      => this[key] = value; // +1 for the cycle through the indexer setter

   public int this[long index]
      => LookupLong(index);

   public int LookupLong(long index)
      => this[index]; // +1 for the cycle through the expression-bodied indexer

   public int Length
      => LastItem();

   public int LastItem()
      => this[^1]; // +1 for the cycle through Length, which the implicit index reads

   public event Action Changed
   {
      add => Subscribe(value);
      remove => Unsubscribe(value);
   }

   public void Subscribe(Action handler)
      => Changed += handler; // +1 for the cycle through the add accessor

   public void Unsubscribe(Action handler)
      => Changed -= handler; // +1 for the cycle through the remove accessor

   public static int Shared
      => ReadShared();

   public static int ReadShared()
      => Shared; // +1 for the cycle through a static getter

   public int Deferred
      => ReadDeferred();

   public int ReadDeferred()
      => Evaluate(() => Deferred); // +1 for the cycle through the getter read in the lambda

   public ref int Slot
      => ref SlotOwner();

   public ref int SlotOwner()
   {
      Slot = 1; // +1 for the cycle, as a write to a ref-returning property calls its getter
      return ref _value;
   }

   public int Plain { get; set; }

   public int ReadPlain()
      => Plain + Evaluate(() => Plain = 1); // An auto-property has no body to close a cycle

   public string Named
      => NameOfNamed();

   public string NameOfNamed()
      => nameof(Named); // nameof reads no property

   public int Echo
   {
      get => 0;
      set => ReadEcho();
   }

   public int ReadEcho()
      => Echo; // A read calls only the getter, so the setter closes no cycle

   public int Mirror
   {
      get => WriteMirror();
      set { }
   }

   public int WriteMirror()
   {
      Mirror = 1; // A write calls only the setter, so the getter closes no cycle
      return 0;
   }

   private static int Evaluate(Func<int> read)
      => read();
}

public interface ICognitiveGauge
{
   int Level { get; }
}

public sealed class CognitiveGauge : ICognitiveGauge
{
   public int Level
      => MeasureExact(this) + MeasureAny(this);

   public static int MeasureExact(CognitiveGauge gauge)
      => gauge.Level; // +1 for the cycle through CognitiveGauge.Level

   public static int MeasureAny(ICognitiveGauge gauge)
      => gauge.Level; // Binds to ICognitiveGauge.Level, which has no body
}

public class CognitiveFieldBacked
{
   public int Value
   {
      get => field + Rebuild();
   }

   public CognitiveFieldBacked()
      => Value = 1; // Writes the backing field, so the getter is in no cycle

   public static int Rebuild()
   {
      _ = new CognitiveFieldBacked();
      return 0;
   }
}

public class CognitiveInitBacked
{
   public int Value
   {
      get => field + Reseed();
      init;
   }

   public static int Reseed()
   {
      _ = new CognitiveInitBacked { Value = 1 }; // Calls only the init accessor, so the getter is in no cycle
      return 0;
   }
}
