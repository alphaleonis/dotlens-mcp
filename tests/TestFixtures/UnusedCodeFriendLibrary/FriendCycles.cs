namespace UnusedCodeFriendLibrary;

internal static class Ping
{
   internal static void Send() => Pong.Reply();
}

internal static class Pong
{
   internal static void Reply() => Ping.Send();
}

public class Counter
{
   private int Up(int count) => Down(count + 1);

   private int Down(int count) => Up(count - 1);
}
