namespace CoreTestProject.Analysis;

// Each comment gives the cognitive complexity increments of the constructs on its line.
public static class CognitiveLogicalOperators
{
   public static bool AllPositive(int a, int b, int c)
      => a > 0 && b > 0 && c > 0; // +1

   public static bool MixedSequences(bool a, bool b, bool c, bool d, bool e, bool f)
   {
      if (a && b && c || d || e && f) // +1 for if, +1 for each of the three sequences
         return true;
      return false;
   }

   public static bool NegatedGroup(bool a, bool b, bool c)
   {
      if (a && !(b && c)) // +1 for if, +1 for each of the two sequences
         return true;
      return false;
   }

   public static bool ParenthesizedSequence(bool a, bool b, bool c, bool d)
      => a && (b && c) && d; // +1

   public static bool ParenthesizedMix(bool a, bool b, bool c)
      => (a || b) && c; // +1 for &&, +1 for ||

   public static bool InSmallRange(int value)
      => value is > 0 and < 10 or 100; // +1 for and, +1 for or

   public static bool IsSmallPrime(int value)
      => value is 2 or 3 or 5 or 7; // +1
}
