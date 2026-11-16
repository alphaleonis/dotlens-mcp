namespace CSharp15TestProject;

/// <summary>
/// Calls <see cref="Ping"/>.
/// </summary>
public static class CrefSelf
{
   public static int Ping() => 1;
}

/// <summary>
/// Calls <see cref="Pong"/>.
/// </summary>
public static partial class CrefPartial
{
}

public static partial class CrefPartial
{
   public static int Pong() => 2;
}

public static class QualifiedSelf
{
   public static int First() => QualifiedSelf.Second();

   public static int Second() => 2;
}

public sealed class Chain
{
   public Chain? Next { get; set; }
}
