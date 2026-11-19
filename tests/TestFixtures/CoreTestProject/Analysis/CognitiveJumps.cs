namespace CoreTestProject.Analysis;

// Each comment gives the cognitive complexity increment of the construct on its line.
public static class CognitiveJumps
{
   public static int FirstNegative(int[] values)
   {
      var i = 0;
   next:
      if (i < values.Length) // +1
      {
         if (values[i] < 0) // +2
            return i;
         i++;
         goto next; // +1, no nesting penalty
      }

      return -1;
   }

   public static string DescribeCode(int code)
   {
      switch (code) // +1
      {
         case 0:
            return "zero";
         case 1:
            goto case 0; // +1
         case 2:
            goto default; // +1
         default:
            return "other";
      }
   }
}
