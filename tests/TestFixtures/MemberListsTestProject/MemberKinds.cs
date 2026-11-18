namespace MemberListsTestProject;

public class Gadget : IDisposable
{
   static Gadget() { }

   public Gadget() { }

   ~Gadget() { }

   public int Size { get; set; }

   public event Action? Changed;

   public int Weight;

   public void Run() => Changed?.Invoke();

   void IDisposable.Dispose() { }

   public static Gadget operator -(Gadget gadget) => gadget;

   public static explicit operator int(Gadget gadget) => gadget.Weight;
}
