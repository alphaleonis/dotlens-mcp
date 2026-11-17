namespace CSharp15TestProject;

public sealed class Locker
{
   private readonly int?[] _slots = new int?[4];

   public int? this[int index]
   {
      get => _slots[index];
      set => _slots[index] = value;
   }

   public Locker? Next { get; set; }
}

public sealed class Ward
{
   private readonly Locker[] _lockers = [new(), new()];

   public Locker this[int index] => _lockers[index];

   public Ward Self => this;

   public Ward? Parent { get; set; }
}

public sealed class Crate<T>
{
   private readonly T?[] _items = new T?[4];

   public T? this[int index]
   {
      get => _items[index];
      set => _items[index] = value;
   }

   public void Empty() => Array.Clear(_items);
}

public static class WardExtensions
{
   extension(Ward ward)
   {
      public int this[string name]
      {
         get => name.Length;
         set { }
      }
   }
}

public interface IStall
{
   int this[int index] { get; set; }
}

public sealed class Stall : IStall
{
   public int this[int index]
   {
      get => index;
      set { }
   }
}

public sealed class Kiosk : IStall
{
   public int this[int index]
   {
      get => index;
      set { }
   }
}

public static class NullConditionalIndexerUsage
{
   public static int? Use(Locker? locker, Ward? ward, Crate<string>? crate)
   {
      locker?[0] = 9;
      locker?[1] += 1;
      locker?[2] ??= 3;
      locker?.Next?[3] = 4;
      ward?[0][1] = 5;
      ward?.Parent?[1]?[2] = 6;
      ward?.Self[0][3] = 7;
      var hasValue = locker?[0].HasValue;
      var text = locker?[1].ToString();
      var cell = ward?[1][2];
      var row = ward?.Self[1];
      crate?[0] = "x";
      ward?["name"] = 8;
      var named = ward?["name"];
      var read = locker?[2];
      return read;
   }

   public static void Rent(IStall? stall, Stall? booth, Kiosk? kiosk)
   {
      stall?[0] = 1;
      booth?[1] = 2;
      kiosk?[2] = 3;
   }

   public static void RentDirectly(IStall stall, Stall booth, Kiosk kiosk)
   {
      stall[0] = 1;
      booth[1] = 2;
      kiosk[2] = 3;
   }
}
