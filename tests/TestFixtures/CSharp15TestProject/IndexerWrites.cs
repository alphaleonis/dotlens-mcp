namespace CSharp15TestProject;

public sealed class Slots
{
   private readonly int?[] _values = new int?[4];

   public int? this[int index]
   {
      get => _values[index];
      set => _values[index] = value;
   }

   public int Count { get; set; }
}

public sealed class RefSlots
{
   private readonly int[] _values = new int[4];

   public ref int this[int index] => ref _values[index];
}

public sealed class Shelf
{
   private readonly Slots[] _rows = [new(), new()];

   public Slots this[int index] => _rows[index];

   public Slots Front { get; } = new();
}

public sealed class Board(int[,] cells)
{
   public int[,] Cells { get; } = cells;
}

public static class BoardExtensions
{
   extension(Board board)
   {
      public int this[int row, int column]
      {
         get => board.Cells[row, column];
         set => board.Cells[row, column] = value;
      }
   }
}

public static class IndexerWriteUsage
{
   public static int? WriteSlots(Slots slots)
   {
      var read = slots[0];
      slots[1] = 5;
      slots[2] += 3;
      slots[3] ??= 7;
      slots[0]++;
      slots[1]--;
      ++slots[2];
      --slots[3];
      (slots[0], slots[1]) = (1, 2);
      ((slots[2], read), slots[3]) = ((3, slots[0]), 4);
      (slots[1]) = 6;
      var fresh = new Slots { [0] = 1, Count = 2 };
      (slots.Count, fresh.Count) = (3, 4);
      slots?.Count = 9;
      return read;
   }

   public static Shelf FillShelf() => new() { [0] = { [1] = 4 }, Front = { [2] = 6 } };

   public static int PassRefs(RefSlots slots)
   {
      slots[0] = 1;
      Fill(out slots[1]);
      Bump(ref slots[2]);
      return Peek(in slots[3]);
   }

   public static int WriteBoard(Board board)
   {
      var read = board[0, 0];
      board[0, 1] = 5;
      board[1, 0] += 3;
      board[1, 1]++;
      --board[0, 0];
      (board[0, 1], board[1, 0]) = (1, 2);
      return read;
   }

   private static void Fill(out int value) => value = 0;

   private static void Bump(ref int value) => value++;

   private static int Peek(in int value) => value;
}

public sealed class Ledger
{
   public int Total { get; set; }

   public int Reset(Ledger other)
   {
      var spare = 1;
      (Total, other.Total) = (0, spare);
      (spare, Total) = (Total, 2);
      Move(ref spare, out spare);
      return Echo(spare);
   }

   private static void Move(ref int source, out int target) => target = source;

   private static int Echo(int value) => value;
}
