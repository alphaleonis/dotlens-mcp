namespace UnusedCodeTestedLibrary;

internal static class Ping
{
   internal static void Send() => Pong.Reply();
}

internal static class Pong
{
   internal static void Reply() => Ping.Send();
}
