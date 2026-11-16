namespace CSharp15TestProject;

public static class WordExtensions
{
   public static int Vowels(this string word) => word.Count(c => "aeiou".Contains(c));

   public static string Shout(this string word) => word.ToUpperInvariant();
}

public static class StaleExtensions
{
   public static int Tripled(this int number) => number * 3;

   extension(long number)
   {
      public long Halved => number / 2;
   }
}

public static class LoopExtensions
{
   public static int Squared(this int number) => number.Times(number);

   public static int Times(this int number, int factor) => number * factor;
}

public static class SeededExtensions
{
   private static readonly int Seed;

   static SeededExtensions() => Seed = 7;

   public static int Salted(this int number) => number + Seed;
}

public static class ContainerUsage
{
   public static int Score(string word) => word.Vowels() + word.Length.Salted();
}
