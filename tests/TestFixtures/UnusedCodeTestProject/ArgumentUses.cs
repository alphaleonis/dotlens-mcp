namespace UnusedCodeTestProject;

internal sealed class AddedItem
{
   public AddedItem(int value)
   {
   }

   public AddedItem() => AddedInit();

   private static void AddedInit()
   {
   }
}

internal sealed class SelectedItem
{
   public SelectedItem(int value)
   {
   }

   public SelectedItem() => SelectedInit();

   public static SelectedItem Create(int value) => new(value);

   private static void SelectedInit()
   {
   }
}

internal sealed class ComparedItem
{
   public ComparedItem(int value)
   {
   }

   public ComparedItem() => ComparedInit();

   public static ComparedItem Create() => new(1);

   private static void ComparedInit()
   {
   }
}

internal static class Check
{
   public static bool Same<T>(T first, T second) => Equals(first, second);
}

internal static class ArgumentUses
{
   private static readonly List<AddedItem> Added = [];

   private static readonly int[] Numbers = [1, 2];

   public static void Run()
   {
      Added.Add(new AddedItem(1));
      _ = Numbers.Select(number => SelectedItem.Create(number)).ToList();
      _ = Check.Same(ComparedItem.Create(), ComparedItem.Create());
   }
}
