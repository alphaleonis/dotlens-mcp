namespace CSharp15TestProject;

public static class LabeledLoops
{
   public static int CountRowsBeforeNegative(int[][] rows)
   {
      var count = 0;
   outer:
      for (var i = 0; i < rows.Length; i++)
      {
         for (var j = 0; j < rows[i].Length; j++)
         {
            if (rows[i][j] < 0)
               break outer;
         }

         count++;
      }

      return count;
   }

   public static int CountRowsWithoutZero(int[][] rows)
   {
      var count = 0;
   outer:
      for (var i = 0; i < rows.Length; i++)
      {
         for (var j = 0; j < rows[i].Length; j++)
         {
            if (rows[i][j] == 0)
               continue outer;
         }

         count++;
      }

      return count;
   }

   public static int FindNegativeRow(int[][] rows)
   {
      var i = 0;
   outer:
      while (true)
      {
         while (true)
         {
            if (rows[i][0] < 0)
               break outer;
            i++;
         }
      }

      return i; // Reachable only through break outer.
   }
}
