using System.Runtime.CompilerServices;

namespace CoreTestProject.Analysis;

// Each comment gives the cognitive complexity increments of the constructs on its line.
public sealed class CognitiveEnumerableSource
{
   public CognitiveEnumerableSource.Enumerator GetEnumerator()
      => new(DrainSource()); // +1 for the cycle through foreach

   public static int DrainSource()
   {
      foreach (var item in new CognitiveEnumerableSource()) // +1
         return item;
      return 0;
   }

   public sealed class Enumerator(int start)
   {
      public int Current
         => start;

      public bool MoveNext()
         => false;
   }
}

public sealed class CognitiveMoveNextSource
{
   public Enumerator GetEnumerator()
      => new();

   public static int DrainMoveNext()
   {
      foreach (var item in new CognitiveMoveNextSource()) // +1
         return item;
      return 0;
   }

   public sealed class Enumerator
   {
      public int Current
         => 0;

      public bool MoveNext()
         => DrainMoveNext() > 0; // +1 for the cycle through foreach
   }
}

public sealed class CognitiveCurrentSource
{
   public Enumerator GetEnumerator()
      => new();

   public static int DrainCurrent()
   {
      foreach (var item in new CognitiveCurrentSource()) // +1
         return item;
      return 0;
   }

   public sealed class Enumerator
   {
      public int Current
         => DrainCurrent();

      public bool MoveNext()
         => true;
   }
}

public sealed class CognitiveDisposeSource
{
   public Enumerator GetEnumerator()
      => new();

   public static int DrainDispose()
   {
      foreach (var item in new CognitiveDisposeSource()) // +1
         return item;
      return 0;
   }

   public ref struct Enumerator
   {
      public int Current
         => 0;

      public bool MoveNext()
         => false;

      public void Dispose()
         => DrainDispose(); // +1 for the cycle through the end of foreach
   }
}

public sealed class CognitiveAsyncSource
{
   public Enumerator GetAsyncEnumerator()
      => new();

   public static async Task<int> DrainAsync()
   {
      await foreach (var item in new CognitiveAsyncSource()) // +1
         return item;
      return 0;
   }

   public sealed class Enumerator
   {
      public int Current
         => 0;

      public async ValueTask<bool> MoveNextAsync()
         => await DrainAsync() > 0; // +1 for the cycle through await foreach
   }
}

public sealed class CognitiveAwaitable
{
   public Awaiter GetAwaiter()
      => new();

   public static async Task<int> AwaitResult()
      => await new CognitiveAwaitable(); // +1 for the cycle through GetResult

   public sealed class Awaiter : INotifyCompletion
   {
      public bool IsCompleted
         => true;

      public int GetResult()
         => AwaitResult().Id; // +1 for the cycle through await

      public void OnCompleted(Action continuation)
      {
      }
   }
}

public sealed class CognitiveSplitter
{
   public void Deconstruct(out int first, out int second)
      => (first, second) = (Split(), 0); // +1 for the cycle through the deconstruction

   public static int Split()
   {
      var (first, _) = new CognitiveSplitter(); // Calls Deconstruct
      return first; // +1 for the cycle
   }
}

public sealed class CognitiveLoopSplitter
{
   public void Deconstruct(out int first, out int second)
      => (first, second) = (SplitAll(), 0); // +1 for the cycle through the deconstruction in foreach

   public static int SplitAll()
   {
      foreach (var (first, _) in new[] { new CognitiveLoopSplitter() }) // +1
         return first;
      return 0; // +1 for the cycle
   }
}

public sealed class CognitivePatternSplitter
{
   public void Deconstruct(out int first, out int second)
      => (first, second) = (Match() ? 1 : 0, 0); // +1 for ?:, +1 for the cycle through the positional pattern

   public static bool Match()
      => new CognitivePatternSplitter() is (1, _); // +1 for the cycle
}

public sealed class CognitiveBag : IEnumerable<int>
{
   public void Add(int item)
      => FillWithInitializer(); // +1 for the cycle through the collection initializer

   public static CognitiveBag FillWithInitializer()
      => new() { 1 }; // +1 for the cycle through Add

   public IEnumerator<int> GetEnumerator()
      => Enumerable.Empty<int>().GetEnumerator();

   System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
      => GetEnumerator();
}

public sealed class CognitiveStack : IEnumerable<int>
{
   public CognitiveStack()
      => _ = Build(); // +1 for the cycle through the collection expression

   public static CognitiveStack Build()
      => []; // +1 for the cycle through the constructor

   public void Add(int item)
   {
   }

   public IEnumerator<int> GetEnumerator()
      => Enumerable.Empty<int>().GetEnumerator();

   System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
      => GetEnumerator();
}

public readonly struct CognitiveCelsius(int degrees)
{
   public int Degrees { get; } = degrees;

   public static implicit operator CognitiveCelsius(int degrees)
      => ToCelsius(degrees); // +1 for the cycle with ToCelsius

   public static CognitiveCelsius ToCelsius(int degrees)
   {
      CognitiveCelsius celsius = degrees; // +1 for the cycle through the implicit conversion
      return celsius;
   }
}

public readonly struct CognitiveFlag
{
   public static bool operator true(CognitiveFlag flag)
      => true;

   public static bool operator false(CognitiveFlag flag)
      => Check(flag); // +1 for the cycle through &&

   public static CognitiveFlag operator &(CognitiveFlag left, CognitiveFlag right)
      => left;

   public static bool Check(CognitiveFlag flag)
   {
      _ = flag && flag; // +1 for &&, +1 for the cycle through operator false
      return true;
   }
}

public class CognitiveImplicitBase
{
   public CognitiveImplicitBase()
      => _ = new CognitiveImplicitDerived(); // +1 for the cycle through the implicit constructor of the derived class
}

public sealed class CognitiveImplicitDerived : CognitiveImplicitBase
{
}

public class CognitivePrimaryBase
{
   public CognitivePrimaryBase(int depth)
   {
      if (depth > 0) // +1
         _ = new CognitivePrimaryDerived(depth - 1); // +1 for the cycle through the primary constructor
   }
}

public sealed class CognitivePrimaryDerived(int depth) : CognitivePrimaryBase(depth)
{
}

public sealed class CognitiveSeeded
{
   private readonly int _seed = Sow();

   public static int Sow()
      => new CognitiveSeeded()._seed; // +1 for the cycle through the field initializer
}

public sealed class CognitiveSeededDeclared
{
   private readonly int _seed = Grow();

   public CognitiveSeededDeclared() // +1 for the cycle through the field initializer
   {
   }

   public static int Grow()
      => new CognitiveSeededDeclared()._seed; // +1 for the cycle
}

public sealed record CognitiveVersionTag(int Code)
{
   public bool Equals(CognitiveVersionTag? other)
      => other is not null && Matches(other); // +1 for &&, +1 for the cycle through the generated Equals(object)

   public bool Matches(object other)
      => Equals(other); // +1 for the cycle

   public bool Differs(CognitiveVersionTag? other)
      => this != other; // Calls the generated != and ==, which call Equals(CognitiveVersionTag)

   public override int GetHashCode()
      => Code;
}

public sealed record CognitiveDisagreement(int Code)
{
   public bool Equals(CognitiveDisagreement? other)
      => !Differs(other); // +1 for the cycle through the generated != and ==

   public bool Differs(CognitiveDisagreement? other)
      => this != other; // +1 for the cycle

   public override int GetHashCode()
      => Code;
}

public sealed record CognitiveLabel(string Text)
{
   public string Shout
      => Describe();

   public string Describe()
      => ToString(); // +1 for the cycle through the generated ToString and PrintMembers
}

public sealed record CognitivePair(int First, int Second)
{
   private readonly int _first = First;

   public int First
   {
      get => Unpack() + _first;
      init => _first = value;
   }

   public int Unpack()
   {
      var (first, _) = this; // Calls the generated Deconstruct, which reads First
      return first; // +1 for the cycle
   }
}

public sealed record CognitiveDraft(int Revision)
{
   public CognitiveDraft(CognitiveDraft original) // +1 for the cycle through the generated clone
      => Revision = Revise(original);

   public static int Revise(CognitiveDraft draft)
      => (draft with { }).Revision; // +1 for the cycle
}

public class CognitivePlainBase
{
   public CognitivePlainBase()
      => _ = new CognitivePlainDerived(0); // +1 for the cycle through the implicit base() of a primary constructor
}

public sealed class CognitivePlainDerived(int depth) : CognitivePlainBase
{
   public int Depth
      => depth;
}

public record CognitiveNote
{
   public string Body
      => CognitiveMemo.Show();
}

public sealed record CognitiveMemo : CognitiveNote
{
   public static string Show()
      => new CognitiveMemo().ToString(); // +1 for the cycle through the base record's generated PrintMembers
}

public record CognitiveShape
{
   public virtual bool Equals(CognitiveShape? other)
      => CognitiveSquare.Compare(); // +1 for the cycle through the derived record's generated Equals

   public override int GetHashCode()
      => CognitiveSquare.Hash(); // +1 for the cycle through the derived record's generated GetHashCode
}

public sealed record CognitiveSquare : CognitiveShape
{
   public static bool Compare()
      => new CognitiveSquare() == new CognitiveSquare(); // +1 for the cycle

   public static int Hash()
      => new CognitiveSquare().GetHashCode(); // +1 for the cycle
}

public record CognitiveContract
{
   protected virtual Type EqualityContract
      => Probe();

   public static Type Probe()
   {
      _ = new CognitiveContract() == new CognitiveContract(); // +1 for the cycle through EqualityContract
      return typeof(CognitiveContract);
   }
}

public readonly struct CognitiveSwitch
{
   public static bool operator true(CognitiveSwitch value)
      => Flip(value); // +1 for the cycle through ||

   public static bool operator false(CognitiveSwitch value)
      => false;

   public static CognitiveSwitch operator |(CognitiveSwitch left, CognitiveSwitch right)
      => left;

   public static bool Flip(CognitiveSwitch value)
   {
      _ = value || value; // +1 for ||, +1 for the cycle through operator true
      return true;
   }
}

public class CognitiveHiddenBase
{
   private CognitiveHiddenBase()
      => _ = new CognitiveHiddenDerived(); // The implicit base() of CognitiveHiddenDerived cannot reach this private constructor

   protected CognitiveHiddenBase(int depth = 0)
      => _ = new CognitiveHiddenDerived(); // +1 for the cycle through the implicit base() that binds here
}

public sealed class CognitiveHiddenDerived : CognitiveHiddenBase
{
}

public class CognitiveOptionalBase
{
   protected CognitiveOptionalBase(int depth = 0)
      => _ = new CognitiveOptionalDerived(); // +1 for the cycle, as base() prefers this to the params overload

   protected CognitiveOptionalBase(params int[] depths)
   {
   }
}

public sealed class CognitiveOptionalDerived : CognitiveOptionalBase
{
}

public class CognitiveEnclosingBase
{
   private CognitiveEnclosingBase()
      => _ = new Nested(); // +1 for the cycle, as a nested type reaches the private constructor

   public sealed class Nested : CognitiveEnclosingBase
   {
   }
}

public class CognitiveBodilessBase
{
   public CognitiveBodilessBase()
      => _ = new CognitiveBodilessDerived(); // +1 for the cycle through the implicit constructor of a type without a body
}

public sealed class CognitiveBodilessDerived : CognitiveBodilessBase;

public class CognitiveChainBase
{
   public CognitiveChainBase()
      => _ = new CognitiveChainLeaf(); // +1 for the cycle through two implicit constructors
}

public class CognitiveChainMiddle : CognitiveChainBase
{
}

public sealed class CognitiveChainLeaf : CognitiveChainMiddle
{
}

public class CognitiveAbstractChainBase
{
   public CognitiveAbstractChainBase()
      => _ = new CognitiveAbstractChainLeaf(); // +1 for the cycle through the protected implicit constructor of an abstract class
}

public abstract class CognitiveAbstractChainMiddle : CognitiveAbstractChainBase
{
}

public sealed class CognitiveAbstractChainLeaf : CognitiveAbstractChainMiddle
{
}

public class CognitivePrimaryChainBase
{
   public CognitivePrimaryChainBase()
      => _ = new CognitivePrimaryChainLeaf(); // +1 for the cycle through a primary constructor without base arguments
}

public class CognitivePrimaryChainMiddle(int depth = 0) : CognitivePrimaryChainBase
{
   public int Depth
      => depth;
}

public sealed class CognitivePrimaryChainLeaf : CognitivePrimaryChainMiddle
{
}
