using System.Text.Json;

namespace UnusedCodeTestProject;

internal sealed class Money : IEquatable<Money>
{
   public Money()
   {
   }

   public Money(int cents)
   {
      Init(cents);
   }

   public bool Equals(Money? other) => other is not null;

   private static void Init(int cents)
   {
   }
}

internal sealed class Svc
{
   private readonly List<Svc> _peers = [];

   public Svc()
   {
   }

   public Svc(int size)
   {
      Boot(size);
   }

   public int Work() => _peers.Count;

   private static void Boot(int size)
   {
   }
}

internal sealed class Outer
{
   public Outer()
   {
      OuterInit();
   }

   private static void OuterInit()
   {
   }

   internal sealed class Inner
   {
      public Inner()
      {
         InnerInit();
      }

      private static void InnerInit()
      {
      }
   }
}

internal sealed class Payload
{
   public Payload()
   {
      Fill();
   }

   private void Fill()
   {
   }
}

internal static class TypeArgumentUses
{
   public static void Run()
   {
      _ = new Money();
      _ = new Svc().Work();
      _ = typeof(Outer.Inner);
      _ = JsonSerializer.Deserialize<List<Payload>>("[]");
   }
}
