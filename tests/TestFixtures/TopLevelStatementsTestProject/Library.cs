namespace TopLevelStatementsTestProject;

public static class Library
{
   public static int Ping() => 1;
}

public sealed class Table
{
   public int this[int index] => index;
}

public static class Entry
{
   public static void Main() => Library.Ping();
}
