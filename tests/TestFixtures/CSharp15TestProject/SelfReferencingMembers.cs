namespace CSharp15TestProject;

public static class SelfCitedMembers
{
   /// <summary>
   /// Returns one, as <see cref="Echo"/> always does.
   /// </summary>
   public static int Echo() => 1;

   /// <summary>
   /// The seed that <see cref="Stored"/> holds.
   /// </summary>
   public static int Stored = 3;

   public static int Countdown(int count) => count <= 0 ? 0 : Countdown(count - 1);

   /// <summary>
   /// Returns four, one less than <see cref="Cited"/>.
   /// </summary>
   public static int Citer() => 4;

   public static int Cited() => 5;
}

public sealed class Token(int value)
{
   public int Value => value;

   public static Token Create() => new(1);
}

public static class TokenUsage
{
   public static int Read() => Token.Create().Value;
}
