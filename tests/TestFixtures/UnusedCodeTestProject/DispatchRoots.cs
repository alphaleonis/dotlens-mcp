namespace UnusedCodeTestProject;

internal sealed class Handle : IDisposable
{
   public void Dispose() => Release();

   private void Release()
   {
   }

   public override string ToString() => Describe();

   private string Describe() => "handle";
}

internal sealed class DeadHandle : IDisposable
{
   public void Dispose() => Release();

   private void Release()
   {
   }

   public override string ToString() => Describe();

   private string Describe() => "dead";
}
