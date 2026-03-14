namespace CoreTestProject.Types;

/// <summary>
/// Test class for parameter modifier disambiguation (ref/out/in/params).
/// </summary>
public class ModifierOverloads
{
   // Overloads that differ only by ref modifier
   public void Process(int x) { }
   public void Process(ref int x) { x *= 2; }

   // Method with out parameter
   public void Initialize(out int value) { value = 0; }

   // Method with in parameter
   public void Inspect(in int value) { }

   // Method with params parameter
   public void Log(params string[] messages) { }

   // Method with ref readonly parameter
   public void ReadOnly(ref readonly int value) { }
}
