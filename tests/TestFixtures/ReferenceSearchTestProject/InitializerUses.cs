namespace ReferenceSearchTestProject;

// Most uses of InitSource below sit in an initializer or a base argument, outside any method or accessor body.

public sealed class InitTable
{
   public int this[int index] => index;
}

public static class InitSource
{
   public static int Value => 1;

   public static int Next() => Value + 1;
}

public class InitHolder(int start)
{
   public static int StaticArea { get; } = InitSource.Value * 2;

   public int Area { get; } = start + InitSource.Value;

   public int Tracked { get => field; set => field = value; } = InitSource.Next();

   public Func<int> Deferred { get; } = () => InitSource.Value;

   public static readonly int StaticCount = InitSource.Next();

   public readonly int Count = InitSource.Value;

   public event Func<int>? Changed = InitSource.Next;

   public static int? Head { get; } = Table?[0];

   public int Both { get => InitSource.Next(); } = InitSource.Next();

   private static InitTable? Table => null;

   public int Raise() => Changed?.Invoke() ?? 0;
}

public class InitDerived() : InitHolder(InitSource.Value);

public record InitRecord(int Start)
{
   public int Scaled { get; } = Start * InitSource.Value;
}
