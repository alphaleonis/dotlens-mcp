using static CSharp15TestProject.Imported;

namespace CSharp15TestProject;

public static class Imported
{
   public static int Answer() => 42;
}

public static class NamedOnly
{
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class MarkerAttribute : Attribute;

[Marker]
public static class ImplicitTypeUsage
{
   public static int Use() => Answer() + nameof(NamedOnly).Length;
}
