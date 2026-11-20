using System.Collections;
using System.Runtime.CompilerServices;

namespace ReferenceSearchTestProject;

// The tests assert every use of each member that they look up, so add no other uses.

public sealed class ImplicitSequence
{
   public ImplicitEnumerator GetEnumerator() => new ImplicitEnumerator();
}

public sealed class ImplicitEnumerator
{
   public int Current => 1;

   public bool MoveNext() => false;
}

public sealed class ImplicitAwaitable
{
   public ImplicitAwaiter GetAwaiter() => new ImplicitAwaiter();
}

public sealed class ImplicitAwaiter : INotifyCompletion
{
   public bool IsCompleted => true;

   public int GetResult() => 1;

   public void OnCompleted(Action continuation) => continuation();
}

public sealed class ImplicitResource : IDisposable
{
   public void Dispose()
   {
   }
}

public sealed class ImplicitBag : IEnumerable<int>
{
   public void Add(int item)
   {
   }

   public IEnumerator<int> GetEnumerator() => Enumerable.Empty<int>().GetEnumerator();

   IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

[CollectionBuilder(typeof(ImplicitBuilder), "Create")]
public sealed class ImplicitBuilt : IEnumerable<int>
{
   public IEnumerator<int> GetEnumerator() => Enumerable.Empty<int>().GetEnumerator();

   IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public static class ImplicitBuilder
{
   public static ImplicitBuilt Create(ReadOnlySpan<int> items) => new ImplicitBuilt();
}

public sealed class ImplicitCreated
{
   public ImplicitCreated()
   {
   }
}

public static class ImplicitCallUses
{
   public static int Loop(ImplicitSequence sequence)
   {
      var sum = 0;
      foreach (var item in sequence)
         sum += item;
      return sum;
   }

   public static async Task<int> Await(ImplicitAwaitable awaitable) => await awaitable;

   public static void Release(ImplicitResource first, ImplicitResource second)
   {
      using (first)
      {
      }

      using var declared = second;
   }

   public static ImplicitBag Fill() => new ImplicitBag { 1, 2 };

   public static ImplicitBuilt Build() => [1, 2];

   public static ImplicitCreated Create() => new();
}

/// <summary>
/// Refers to <see cref="operator +(ImplicitOperand, ImplicitOperand)"/>, <see cref="ImplicitOperand(int)"/> and
/// <see cref="ImplicitTarget.Next"/> from documentation.
/// </summary>
public sealed class ImplicitOperand
{
   public ImplicitOperand(int value) => Value = value;

   public int Value { get; }

   public static ImplicitOperand operator +(ImplicitOperand left, ImplicitOperand right) => left;

   public static ImplicitOperand operator &(ImplicitOperand left, ImplicitOperand right) => left;

   public static bool operator true(ImplicitOperand operand) => operand.Value != 0;

   public static bool operator false(ImplicitOperand operand) => operand.Value == 0;

   public static ImplicitOperand operator ++(ImplicitOperand operand) => operand;

   public static explicit operator int(ImplicitOperand operand) => operand.Value;
}

public class ImplicitBase
{
   public ImplicitBase(int value)
   {
   }
}

public sealed class ImplicitPrimaryDerived() : ImplicitBase(1);

public sealed class ImplicitChained : ImplicitBase
{
   public ImplicitChained()
      : base(2)
   {
   }

   public ImplicitChained(int value)
      : this()
   {
   }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class ImplicitTagAttribute : Attribute
{
   public ImplicitTagAttribute()
   {
   }

   public ImplicitTagAttribute(int level)
   {
   }
}

public sealed class ImplicitTarget
{
   public int Next() => 1;
}

public sealed class ImplicitQualified
{
   public ImplicitQualified()
   {
   }
}

public sealed class ImplicitGeneric<T>
{
   public ImplicitGeneric()
   {
   }
}

public static class ImplicitInvocationUses
{
   public static int Operators(ImplicitOperand first, ImplicitOperand second)
   {
      var sum = first + second;
      var both = first && second ? 1 : 0;
      sum++;
      sum += first;
      return (int)sum + both;
   }

   public static ImplicitOperand Create() => new ImplicitOperand(1);

   [ImplicitTag]
   public static void Tagged()
   {
   }

   [ImplicitTagAttribute]
   public static void FullyNamed()
   {
   }

   [ImplicitTag(1)]
   public static void Leveled()
   {
   }

   public static int? Conditional(ImplicitTarget? target) => target?.Next();

   public static Func<int> Group(ImplicitTarget target) => target.Next;

   public static string Name() => nameof(ImplicitTarget.Next);

   public static ImplicitQualified Qualified() => new ReferenceSearchTestProject.ImplicitQualified();

   public static ImplicitGeneric<int> Generic() => new ImplicitGeneric<int>();
}

public sealed class ImplicitSuppressed
{
   public ImplicitSuppressed(int value)
   {
   }

   public static ImplicitSuppressed operator -(ImplicitSuppressed operand) => operand;

   public static ImplicitSuppressed Create() => -new ImplicitSuppressed(1);
}
