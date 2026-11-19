namespace CoreTestProject.Analysis;

// Each comment gives the cognitive complexity increment of the construct on its line.
public static class CognitiveSwitchExpressions
{
   public static string DescribeSign(int value) => value switch // +1
   {
      < 0 => "negative",
      0 => "zero",
      _ => "positive",
   };

   public static string DescribeAll(int[] values)
   {
      var parts = new List<string>();
      foreach (var value in values) // +1
      {
         parts.Add(value switch // +2
         {
            > 0 => value > 100 ? "big" : "positive", // +3
            _ => "other",
         });
      }

      return string.Join(",", parts);
   }
}
