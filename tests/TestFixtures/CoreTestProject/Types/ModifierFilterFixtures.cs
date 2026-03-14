using System.Runtime.InteropServices;

namespace CoreTestProject.Types;

/// <summary>
/// Fixtures for testing modifier filter combinations.
/// </summary>
public static class ModifierUtils
{
   public static async Task<int> StaticAsyncMethod()
   {
      await Task.Delay(1);
      return 42;
   }
}

/// <summary>
/// Fixture for volatile field modifier.
/// </summary>
public class VolatileFieldHolder
{
   public volatile int VolatileCounter;
}

/// <summary>
/// Fixture for extern method modifier.
/// </summary>
public class ExternMethodHolder
{
   [DllImport("kernel32.dll")]
   public static extern int GetCurrentThreadId();
}
