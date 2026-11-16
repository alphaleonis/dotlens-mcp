namespace CSharp15TestProject;

public sealed class Tape(int[] cells)
{
   public int[] Cells { get; } = cells;
}

public sealed class Reel(string[] labels)
{
   public string[] Labels { get; } = labels;
}

public static class SpoolExtensions
{
   extension(Tape tape)
   {
      public int this[int index] => tape.Cells[index];

      public int Length => tape.Cells.Length;

      public int Peek() => tape.Cells[0];
   }

   extension(Reel reel)
   {
      public string this[int index] => reel.Labels[index];

      public int Length => reel.Labels.Length;

      public string Peek() => reel.Labels[0];
   }

   extension(Tape tape)
   {
      public int this[string label] => tape.Cells[label.Length];
   }
}

public sealed class Catalog
{
   public int this[int index] => index;

   public int this[string key] => key.Length;
}

public sealed class Box<T>(T item)
{
   public T Get() => item;

   public T Get(int copies) => copies > 0 ? item : item;
}

public static class Mold
{
   public static int Make(int size) => size;

   public static T? Make<T>(int size) => size > 0 ? default : default;
}

public readonly struct Money(long cents)
{
   public long Cents { get; } = cents;

   public static implicit operator Money(int cents) => new(cents);

   public static implicit operator Money(long cents) => new(cents);

   public static explicit operator int(Money money) => (int)money.Cents;

   public static explicit operator long(Money money) => money.Cents;

   public static bool operator <(Money left, Money right) => left.Cents < right.Cents;

   public static bool operator >(Money left, Money right) => left.Cents > right.Cents;

   public static bool operator <(Money left, int right) => left.Cents < right;

   public static bool operator >(Money left, int right) => left.Cents > right;
}

public record Stamp(int Value);

public record Stamp<T>(int Value);
