namespace CoreTestProject.Types;

/// <summary>
/// Test fixture for various method kinds.
/// DO NOT MODIFY - line numbers are referenced in tests.
/// </summary>
public class MethodKindsFixture
{
   private static bool _initialized;

   // Static constructor - line 12
   static MethodKindsFixture()
   {
      _initialized = true;
   }

   // Instance constructor - line 18
   public MethodKindsFixture()
   {
   }

   // Destructor/Finalizer - line 23
   ~MethodKindsFixture()
   {
      _initialized = false;
   }

   // Implicit conversion operator - line 29
   public static implicit operator string(MethodKindsFixture m)
   {
      return m.ToString() ?? "";
   }

   // Explicit conversion operator - line 35
   public static explicit operator int(MethodKindsFixture m)
   {
      return m.GetHashCode();
   }

   // User-defined operator - line 41
   public static MethodKindsFixture operator +(MethodKindsFixture a, MethodKindsFixture b)
   {
      return new MethodKindsFixture();
   }

   // Ordinary method with local function - line 47
   public int MethodWithLocalFunction(int x)
   {
      return LocalDouble(x);

      // Local function - line 52
      int LocalDouble(int n) => n * 2;
   }
}

/// <summary>
/// Interface for explicit implementation testing.
/// </summary>
public interface IMethodKinds
{
   void DoWork();
}

/// <summary>
/// Class with explicit interface implementation.
/// </summary>
public class ExplicitMethodKinds : IMethodKinds
{
   // Explicit interface implementation - line 70
   void IMethodKinds.DoWork()
   {
      // Explicit implementation body
   }
}
