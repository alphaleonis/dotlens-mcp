using System.ComponentModel;

namespace CSharp15TestProject;

public static class AttributedFields
{
   [Description(nameof(Tag))]
   public static int Tag;

   [Description(nameof(Changed))]
   public static event Action? Changed;

   [Description(nameof(Left))]
   public static int Left, Right;

   public static int Base = 1, Derived = Base;

   [Description(nameof(Label))]
   public static string Label => "";
}
