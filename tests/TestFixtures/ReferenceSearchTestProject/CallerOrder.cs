namespace ReferenceSearchTestProject;

public interface IOrderSource
{
   int Read();
}

public sealed class OrderSource : IOrderSource
{
   public int Read() => 1;
}

public static class OrderReader
{
   public static int ReadBoth(OrderSource source) => ((IOrderSource)source).Read() + source.Read();
}
