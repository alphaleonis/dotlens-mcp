namespace CoreTestProject.Analysis;

// Each comment gives the cognitive complexity increments of the constructs on its line.
public static class CognitiveGenericRecursion
{
   public static int Outward<T>(T value, int n)
      => n <= 0 ? 0 : Inward(value, n - 1); // +1 for ?:, +1 for the cycle with Inward

   public static int Inward<T>(T value, int n)
      => Outward(n, n); // +1 for the cycle through the constructed Outward<int>
}

public sealed class CognitiveGenericHost<T>
{
   public int TurnLeft(int n)
      => new CognitiveGenericHost<string>().TurnRight(n); // +1 for the cycle through a constructed type

   public int TurnRight(int n)
      => n <= 0 ? 0 : TurnLeft(n - 1); // +1 for ?:, +1 for the cycle
}

public static partial class CognitivePartialCycle
{
   public static partial int Descend(int n);

   public static partial int Descend(int n)
      => n <= 0 ? 0 : Climb(n - 1); // +1 for ?:, +1 for the cycle with Climb

   public static int Climb(int n)
      => Descend(n); // +1 for the cycle with Descend
}

public static class CognitiveLocalFunctionRecursion
{
   public static int Spiral(int n)
   {
      return EnterLevel(n);

      int EnterLevel(int k) // +1 for the cycle with LeaveLevel
      {
         return LeaveLevel<int>(k);

         int LeaveLevel<TValue>(int m)
            => m <= 0 ? 0 : EnterLevel(m - 1); // +3 for ?: in Spiral, +2 in EnterLevel, +1 alone; +1 for the cycle
      }
   }

   public static int Defer(int n)
      => Apply(() => Defer(n - 1)); // +1 for the call in the lambda, which belongs to Defer

   public static int CallsBack(int n)
   {
      Func<int, int> back = Back;
      return back(n);

      int Back(int k)
         => CallsBack(k); // Only Back calls CallsBack, so neither is in a cycle
   }

   private static int Apply(Func<int> next)
      => next();
}

public readonly struct CognitiveMeters
{
   public CognitiveMeters(int value)
      => Value = value;

   public int Value { get; }

   public static explicit operator CognitiveMeters(int value)
      => value <= 0 ? new CognitiveMeters(0) : new CognitiveMeters(0) + value; // +1 for ?:, +1 for the cycle with +

   public static CognitiveMeters operator +(CognitiveMeters meters, int value)
      => (CognitiveMeters)(meters.Value + value - 1); // +1 for the cycle with the conversion
}

public static class CognitiveExtensionRecursion
{
   public static int Halve(this int n)
      => n <= 0 ? 0 : CognitiveExtensionRecursion.Shrink("text", n - 1); // +1 for ?:, +1 for the cycle with Shrink

   public static int Shrink<T>(this T value, int n)
      => n.Halve(); // +1 for the cycle with Halve
}

public static class CognitiveExtensionBlockRecursion
{
   extension(int n)
   {
      public int Fold()
         => n <= 0 ? 0 : (n - 1).Unfold(); // +1 for ?:, +1 for the cycle with Unfold

      public int Unfold()
         => CognitiveExtensionBlockRecursion.Fold(n); // +1 for the cycle with Fold
   }

   extension<T>(T value)
   {
      public int Spin(int count)
         => count <= 0 ? 0 : "text".Spin(count - 1); // +1 for ?:, +1 for the recursion
   }
}

public partial class CognitiveConstructorRecursion
{
   public partial CognitiveConstructorRecursion(int depth);

   public partial CognitiveConstructorRecursion(int depth)
      : this(depth, 0) // +1 for the cycle with the two-parameter constructor
   {
   }

   public CognitiveConstructorRecursion(int depth, int unused)
   {
      if (depth > 0) // +1
         _ = new CognitiveConstructorRecursion(depth - 1); // +1 for the cycle
   }
}

public class CognitiveConstructorBase
{
   public CognitiveConstructorBase(int depth)
   {
      if (depth > 0) // +1
         _ = new CognitiveConstructorDerived(depth - 1); // +1 for the cycle through the derived constructor
   }
}

public sealed class CognitiveConstructorDerived : CognitiveConstructorBase
{
   public CognitiveConstructorDerived(int depth)
      : base(depth) // +1 for the cycle through the base constructor
   {
   }
}

public class CognitiveVisitor
{
   public virtual int Visit(int n)
      => n <= 0 ? 0 : Revisit(n - 1); // +1 for ?:, +1 for the cycle with Revisit

   private int Revisit(int n)
      => Visit(n); // +1 for the cycle with Visit
}

public sealed class CognitiveDerivedVisitor : CognitiveVisitor
{
   public override int Visit(int n)
      => VisitAgain(n); // +1 for the cycle with VisitAgain

   private int VisitAgain(int n)
      => Visit(n); // +1 for the cycle, as Visit on this receiver binds to the override
}

public sealed class CognitiveUpcastVisitor : CognitiveVisitor
{
   public override int Visit(int n)
      => VisitAsBase(n);

   private int VisitAsBase(int n)
      => ((CognitiveVisitor)this).Visit(n); // Binds to CognitiveVisitor.Visit, never to this override
}

public interface ICognitiveTicker
{
   int Tick(int n)
      => n <= 0 ? 0 : Tock(n - 1); // +1 for ?:, +1 for the cycle with Tock

   int Tock(int n)
      => Tick(n); // +1 for the cycle with Tick
}

public sealed record CognitiveVersion(int Major)
{
   public bool Equals(CognitiveVersion? other)
      => this == other; // +1 for the cycle through the generated ==

   public override int GetHashCode()
      => Major;
}
