namespace CoreTestProject.Analysis;

// Each comment gives the cognitive complexity increment of the construct on its line.
public static class CognitiveLoops
{
   public static int CountDigits(int value)
   {
      var digits = 0;
      do // +1
      {
         digits++;
         value /= 10;
      }
      while (value != 0);

      return digits;
   }

   public static int SumUntilZero(int[][] rows)
   {
      var sum = 0;
      foreach (var row in rows) // +1
      {
         var i = 0;
         do // +2
         {
            if (row[i] == 0) // +3
               break;
            sum += row[i];
            i++;
         }
         while (i < row.Length);
      }

      return sum;
   }

   public static int SumProducts((int Left, int Right)[] pairs)
   {
      var sum = 0;
      foreach (var (left, right) in pairs) // +1
      {
         if (left != 0) // +2
            sum += left * right;
      }

      return sum;
   }
}
