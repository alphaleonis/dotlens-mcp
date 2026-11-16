namespace CSharp15TestProject;

public sealed class Grid(int[,] cells)
{
   public int[,] Cells { get; } = cells;
}

public static class GridExtensions
{
   extension(Grid grid)
   {
      public int this[int row, int column] => grid.Cells[row, column];
   }
}

public static class GridUsage
{
   public static int SumThreeCorners(Grid grid)
   {
      var topLeft = grid[0, 0];
      var topRight = grid[0, 1];
      var bottomLeft = grid[1, 0];
      return topLeft + topRight + bottomLeft;
   }
}
