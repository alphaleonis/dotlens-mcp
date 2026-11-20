namespace ReferenceSearchTestProject;

// Each Deconstruct that the tests look up is called only by the deconstructions in the Uses classes.

public sealed class CallNest<T>(T inner, int count)
{
   public void Deconstruct(out T inner, out int count)
   {
      inner = Inner;
      count = Count;
   }

   public T Inner { get; } = inner;

   public int Count { get; } = count;
}

public sealed class CallPair(int first, string second)
{
   public void Deconstruct(out int first, out string second)
   {
      first = First;
      second = Second;
   }

   public int First { get; } = first;

   public string Second { get; } = second;
}

public sealed class CallPairUses
{
   private static readonly CallPair Shared = new(1, "a");

   public static readonly bool FieldPattern = Shared is var (_, _);

   public bool PropertyPattern => Shared is (1, _);

   public int Positional(object value) => value is CallPair (var first, _) ? first : 0;

   public int Untyped(CallPair value) => value is (> 0, _) ? 1 : 0;

   public int VarPattern(CallPair value) => value is var (first, _) ? first : 0;

   public int NestedPositional(CallNest<CallPair> nest) => nest is (CallPair (var first, _), _) ? first : 0;

   public int NestedVar(CallNest<CallPair> nest) => nest is var ((first, _), _) ? first : 0;

   public int Switch(object value) => value switch { CallPair (var first, _) => first, _ => 0 };

   public int Lambda(CallPair value) => new Func<int>(() => value is (var first, _) ? first : 0)();

   public int Declaration(CallPair value)
   {
      var (first, _) = value;
      return first;
   }

   public int Typed(CallPair value)
   {
      (int first, string _) = value;
      return first;
   }

   public int Existing(CallPair value)
   {
      int first;
      string second;
      (first, second) = value;
      return first + second.Length;
   }

   public int NestedAssignment(CallNest<CallPair> nest)
   {
      var ((first, _), _) = nest;
      return first;
   }

   public int Loop(CallPair[] values)
   {
      var sum = 0;
      foreach (var (first, _) in values)
         sum += first;
      return sum;
   }

   public int TypedLoop(CallPair[] values)
   {
      var sum = 0;
      foreach ((int first, string _) in values)
         sum += first;
      return sum;
   }
}

public sealed record CallRecord(int First, string Second);

public sealed class CallRecordUses
{
   private static readonly CallRecord Shared = new(1, "a");

   public static readonly bool FieldPattern = Shared is var (_, _);

   public bool PropertyPattern => Shared is (1, _);

   public int Positional(object value) => value is CallRecord (var first, _) ? first : 0;

   public int Untyped(CallRecord value) => value is (> 0, _) ? 1 : 0;

   public int VarPattern(CallRecord value) => value is var (first, _) ? first : 0;

   public int NestedPositional(CallNest<CallRecord> nest) => nest is (CallRecord (var first, _), _) ? first : 0;

   public int NestedVar(CallNest<CallRecord> nest) => nest is var ((first, _), _) ? first : 0;

   public int Switch(object value) => value switch { CallRecord (var first, _) => first, _ => 0 };

   public int Lambda(CallRecord value) => new Func<int>(() => value is (var first, _) ? first : 0)();

   public int Declaration(CallRecord value)
   {
      var (first, _) = value;
      return first;
   }

   public int Typed(CallRecord value)
   {
      (int first, string _) = value;
      return first;
   }

   public int Existing(CallRecord value)
   {
      int first;
      string second;
      (first, second) = value;
      return first + second.Length;
   }

   public int NestedAssignment(CallNest<CallRecord> nest)
   {
      var ((first, _), _) = nest;
      return first;
   }

   public int Loop(CallRecord[] values)
   {
      var sum = 0;
      foreach (var (first, _) in values)
         sum += first;
      return sum;
   }

   public int TypedLoop(CallRecord[] values)
   {
      var sum = 0;
      foreach ((int first, string _) in values)
         sum += first;
      return sum;
   }
}

public sealed class CallExtended(int first, string second)
{
   public int First { get; } = first;

   public string Second { get; } = second;
}

public static class CallExtendedExtensions
{
   public static void Deconstruct(this CallExtended value, out int first, out string second)
   {
      first = value.First;
      second = value.Second;
   }
}

public sealed class CallExtendedUses
{
   private static readonly CallExtended Shared = new(1, "a");

   public static readonly bool FieldPattern = Shared is var (_, _);

   public bool PropertyPattern => Shared is (1, _);

   public int Positional(object value) => value is CallExtended (var first, _) ? first : 0;

   public int Untyped(CallExtended value) => value is (> 0, _) ? 1 : 0;

   public int VarPattern(CallExtended value) => value is var (first, _) ? first : 0;

   public int NestedPositional(CallNest<CallExtended> nest) => nest is (CallExtended (var first, _), _) ? first : 0;

   public int NestedVar(CallNest<CallExtended> nest) => nest is var ((first, _), _) ? first : 0;

   public int Switch(object value) => value switch { CallExtended (var first, _) => first, _ => 0 };

   public int Lambda(CallExtended value) => new Func<int>(() => value is (var first, _) ? first : 0)();

   public int Declaration(CallExtended value)
   {
      var (first, _) = value;
      return first;
   }

   public int Typed(CallExtended value)
   {
      (int first, string _) = value;
      return first;
   }

   public int Existing(CallExtended value)
   {
      int first;
      string second;
      (first, second) = value;
      return first + second.Length;
   }

   public int NestedAssignment(CallNest<CallExtended> nest)
   {
      var ((first, _), _) = nest;
      return first;
   }

   public int Loop(CallExtended[] values)
   {
      var sum = 0;
      foreach (var (first, _) in values)
         sum += first;
      return sum;
   }

   public int TypedLoop(CallExtended[] values)
   {
      var sum = 0;
      foreach ((int first, string _) in values)
         sum += first;
      return sum;
   }
}

public sealed class CallBlock(int first, string second)
{
   public int First { get; } = first;

   public string Second { get; } = second;
}

public static class CallBlockExtensions
{
   extension(CallBlock value)
   {
      public void Deconstruct(out int first, out string second)
      {
         first = value.First;
         second = value.Second;
      }
   }
}

public sealed class CallBlockUses
{
   private static readonly CallBlock Shared = new(1, "a");

   public static readonly bool FieldPattern = Shared is var (_, _);

   public bool PropertyPattern => Shared is (1, _);

   public int Positional(object value) => value is CallBlock (var first, _) ? first : 0;

   public int Untyped(CallBlock value) => value is (> 0, _) ? 1 : 0;

   public int VarPattern(CallBlock value) => value is var (first, _) ? first : 0;

   public int NestedPositional(CallNest<CallBlock> nest) => nest is (CallBlock (var first, _), _) ? first : 0;

   public int NestedVar(CallNest<CallBlock> nest) => nest is var ((first, _), _) ? first : 0;

   public int Switch(object value) => value switch { CallBlock (var first, _) => first, _ => 0 };

   public int Lambda(CallBlock value) => new Func<int>(() => value is (var first, _) ? first : 0)();

   public int Declaration(CallBlock value)
   {
      var (first, _) = value;
      return first;
   }

   public int Typed(CallBlock value)
   {
      (int first, string _) = value;
      return first;
   }

   public int Existing(CallBlock value)
   {
      int first;
      string second;
      (first, second) = value;
      return first + second.Length;
   }

   public int NestedAssignment(CallNest<CallBlock> nest)
   {
      var ((first, _), _) = nest;
      return first;
   }

   public int Loop(CallBlock[] values)
   {
      var sum = 0;
      foreach (var (first, _) in values)
         sum += first;
      return sum;
   }

   public int TypedLoop(CallBlock[] values)
   {
      var sum = 0;
      foreach ((int first, string _) in values)
         sum += first;
      return sum;
   }
}

public interface ICallSource
{
   void Deconstruct(out int first, out string second);
}

public sealed class CallSource : ICallSource
{
   public void Deconstruct(out int first, out string second)
   {
      first = 1;
      second = "a";
   }
}

public class CallBase
{
   public virtual void Deconstruct(out int first, out string second)
   {
      first = 1;
      second = "a";
   }
}

public sealed class CallDerived : CallBase
{
   public override void Deconstruct(out int first, out string second)
   {
      first = 2;
      second = "b";
   }
}

public sealed class CallSibling : CallBase
{
   public override void Deconstruct(out int first, out string second)
   {
      first = 3;
      second = "c";
   }
}

public static class CallHierarchyUses
{
   public static bool ViaInterface(ICallSource source) => source is (> 0, _);

   public static bool ViaImplementation(CallSource source) => source is (> 0, _);

   public static bool ViaBase(CallBase value) => value is (> 0, _);

   public static bool ViaDerived(CallDerived value) => value is (> 0, _);

   public static bool ViaSibling(CallSibling value) => value is (> 0, _);
}
