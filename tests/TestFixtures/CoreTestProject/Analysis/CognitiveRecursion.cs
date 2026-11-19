namespace CoreTestProject.Analysis;

// Each comment gives the cognitive complexity increments of the constructs on its line.
public static class CognitiveRecursion
{
   public static int Factorial(int n)
   {
      if (n <= 1) // +1
         return 1;
      return n * Factorial(n - 1); // +1 for the recursion
   }

   public static int Fibonacci(int n)
      => n < 2 ? n : Fibonacci(n - 1) + Fibonacci(n - 2); // +1 for ?:, +1 for the recursion

   public static int SumFrom(int[] values)
      => SumFrom(values, 0); // Calls an overload, not itself

   public static int SumFrom(int[] values, int start)
   {
      if (start >= values.Length) // +1
         return 0;
      return values[start] + SumFrom(values, start + 1); // +1 for the recursion
   }

   public static int CountNodes(CognitiveTree tree)
   {
      return Count(tree);

      static int Count(CognitiveTree? node)
         => node is null ? 0 : 1 + Count(node.Left) + Count(node.Right); // +2 for ?: in CountNodes, +1 alone; +1 for the recursion
   }
}

public sealed record CognitiveTree(CognitiveTree? Left, CognitiveTree? Right)
{
   public int Height()
      => 1 + Math.Max(Left?.Height() ?? 0, Right?.Height() ?? 0); // +1 for the recursion
}

public static partial class CognitivePartialRecursion
{
   public static partial int Countdown(int n);

   public static partial int Countdown(int n)
   {
      if (n <= 0) // +1
         return 0;
      return Countdown(n - 1); // +1 for the recursion
   }
}
