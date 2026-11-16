namespace CSharp15TestProject;

public sealed class Row(int[] cells)
{
   public int Length => cells.Length;

   public int this[int index] => cells[index];

   public Row Slice(int start, int length) => new(cells[start..(start + length)]);
}

public sealed class Score(int total)
{
   public int Total => total;

   public static Score operator +(Score left, Score right) => new(left.Total + right.Total);

   public Score Add(int amount, int times) => new(total + amount * times);
}

public static class RowUsage
{
   public static int Read(Row row, int index, int[] cells)
   {
      var direct = row[index];
      var fromEnd = row[^1];
      var middle = row[1..3];
      var maybe = Find(row)?[0];
      var raw = cells[index];
      return direct + fromEnd + middle.Length + (maybe ?? 0) + raw;
   }

   public static Score Combine(Score score)
   {
      var bigger = score.Add(1, 2);
      var fresh = new Score(3);
      var sum = bigger + fresh;
      return sum;
   }

   private static Row? Find(Row row) => row.Length > 0 ? row : null;
}
