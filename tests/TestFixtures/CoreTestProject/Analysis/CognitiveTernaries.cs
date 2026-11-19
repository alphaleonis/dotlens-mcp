namespace CoreTestProject.Analysis;

// Each comment gives the cognitive complexity increments of the constructs on its line.
public static class CognitiveTernaries
{
   public static int Compare(int a, int b)
      => a < b ? -1 : a > b ? 1 : 0; // +1 for the outer ?:, +2 for the nested one
}
