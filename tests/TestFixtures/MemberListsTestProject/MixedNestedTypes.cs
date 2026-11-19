namespace MemberListsTestProject;

public static class Quay
{
   public const int Depth = 12;

   public class Crane;

   public struct Bollard;

   public interface IRope;

   public enum Swell { Calm, Rough }

   public delegate void Horn();

   public record struct Ticket(int Count);

   extension(string text)
   {
      public int Knots => text.Length;
   }

   public static void Moor() { }
}
