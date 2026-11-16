namespace CSharp15TestProject;

public static class TextExtensions
{
   extension(string text)
   {
      public bool IsShout => text == text.ToUpperInvariant();

      public string Whisper() => text.ToLowerInvariant();

      public static string Blank() => "";
   }

   extension(int number)
   {
      public bool IsEven => number % 2 == 0;

      public int Doubled() => number * 2;
   }

   extension<T>(IEnumerable<T> items)
   {
      public bool IsEmpty => !items.Any();
   }
}

public static class TextUsage
{
   public static string Quiet(string text) => text.IsEmpty ? string.Blank() : text.IsShout ? text.Whisper() : text;
}
