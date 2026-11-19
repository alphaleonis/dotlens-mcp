namespace CoreTestProject.Analysis;

// Each comment gives the cognitive complexity increment of the construct on its line.
public static class CognitiveNestedFunctions
{
   public static Func<int, int> MakeClamp(int max)
   {
      return value =>
      {
         if (value > max) // +2
            return max;
         return value;
      };
   }

   public static Action<int> MakeLogger(List<string> log)
   {
      return delegate (int value)
      {
         if (value < 0) // +2
            log.Add("negative");
      };
   }

   public static int SumPositive(int[] values)
   {
      var sum = 0;
      foreach (var value in values) // +1
         Add(value);
      return sum;

      void Add(int value)
      {
         if (value > 0) // +2 in SumPositive, +1 in Add alone
            sum += value;
      }
   }

   public static int CountAbove(int[][] rows, int limit)
   {
      var count = 0;
      foreach (var row in rows) // +1
         count += row.Sum(cell => cell > limit ? 1 : 0); // +3
      return count;
   }
}
