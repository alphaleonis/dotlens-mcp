namespace CoreTestProject.Types;

/// <summary>
/// Test fixture for reference kind classification (read/write/call).
/// Each method creates clear, unambiguous reference patterns.
/// </summary>
public class ReferenceKindTarget
{
   public int Value { get; set; }
   public string Name { get; set; } = "";

   public void DoWork() { }

   public int Calculate(int x) => x * 2;

   public static void ReadExamples(ReferenceKindTarget target)
   {
      var v = target.Value;
      var n = target.Name;
      Console.WriteLine(v + n);
   }

   public static void WriteExamples(ReferenceKindTarget target)
   {
      target.Value = 42;
      target.Name = "test";
      target.Value += 10;
   }

   public int Counter;

   public static void TakeRef(ref int value) { value++; }

   public static void CallExamples(ReferenceKindTarget target)
   {
      target.DoWork();
      target.Calculate(5);
      var t = new ReferenceKindTarget();
      t.DoWork();
   }

   public static void RefExamples(ReferenceKindTarget target)
   {
      TakeRef(ref target.Counter);
   }
}
