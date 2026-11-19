namespace CoreTestProject.Analysis;

// Each comment gives the cognitive complexity increment of the construct on its line.
public static class CognitiveElseChains
{
   public static int Sign(int value)
   {
      if (value > 0) // +1
         return 1;
      else if (value < 0) // +1
         return -1;
      else // +1
         return 0;
   }

   public static string Classify(int value, bool strict)
   {
      if (value > 100) // +1
      {
         return "large";
      }
      else if (value > 10) // +1
      {
         if (strict) // +2
            return "medium-strict";
         return "medium";
      }
      else // +1
      {
         if (strict) // +2
            return "small-strict";
         return "small";
      }
   }
}
