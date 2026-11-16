namespace CSharp15TestProject;

public record Point(int X, int Y);

public class Logger(string name);

public struct Size(int width, int height)
{
   public int Area => width * height;
}

public record struct Pair(int Left, int Right);

public class Counter(int start) : Tally(start);

public class Tally(int initial)
{
   public int Initial { get; } = initial;
}

public interface IMeasurable
{
   int Measure();
}
