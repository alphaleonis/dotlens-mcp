namespace UnusedCodeTestProject;

public class Parser
{
   private static readonly int First = Second + 1;

   private static readonly int Second = First + 1;

   public int Parse(string text) => ParseList(text, 0);

   private int ParseList(string text, int depth) => depth > 3 ? 0 : ParseItem(text, depth);

   private int ParseItem(string text, int depth) => ParseList(text, depth + 1);

   private bool IsEven(int number) => number == 0 || IsOdd(number - 1);

   private bool IsOdd(int number) => number != 0 && IsEven(number - 1);

   private int Seed => Next();

   private int Next()
   {
      int Local() => Seed + 1;
      return Local();
   }
}

internal static class Ping
{
   public static void Send() => Pong.Reply();
}

internal static class Pong
{
   public static void Reply() => Ping.Send();
}

internal static class Startup
{
   public static void Run()
   {
      new Parser().Parse("text");
      Loop.Start();
   }
}

internal static class Loop
{
   public static void Start() => Again();

   private static void Again() => Start();
}
