namespace UnusedCodeTestProject;

internal interface IHandler
{
   void Handle();
}

internal sealed class ScannedHandler : IHandler
{
   public void Handle() => Help();

   private void Help()
   {
   }
}

internal static class HandlerScan
{
   public static void Run()
   {
      foreach (var type in typeof(HandlerScan).Assembly.GetTypes())
      {
         if (type.IsClass && Activator.CreateInstance(type) is IHandler handler)
            handler.Handle();
      }
   }
}
