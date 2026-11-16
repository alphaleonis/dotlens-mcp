namespace CSharp15TestProject;

public class Figure
{
   public virtual double Area() => 0;

   public double Scale(int factor) => factor;

   public double Scale(double factor) => factor;

   public int Sides;

   public static Figure Unit() => new();

   public T Pick<T>(T value) => value;

   public T Pick<T>(T value, int count) => count > 0 ? value : value;

   private int Secret() => Sides;
}

public class Rhombus : Figure
{
   public override double Area() => 1;

   public new double Scale(int factor) => factor * 2;

   public new string Sides = "four";

   public new U Pick<U>(U value) => value;
}

public class Gauge
{
   public const int Max = 10;

   public static int Count;

   public static void Reset() { }

   public class Dial;

   public static class Tools;

   public void Turn(ref int steps) => steps++;

   public void Push(in int steps) => Count = steps;

   public int Level { get; set; }

   public int this[int index] { get => index; set => Count = value; }

   public int Mode { get; set; }

   public void Store(object value) => Count = value.GetHashCode();

   public void Pair((int First, int Second) pair) => Count = pair.First;

   public void Many(List<(int A, int B)> items) => Count = items.Count;

   public void Shift() => Count = 0;
}

public class Barometer : Gauge
{
   public void Turn(out int steps) => steps = 0;

   public void Push(ref int steps) => steps++;

   public new int Level => 1;

   public new int this[int index] => index;

   public new void Mode() => Count = 1;

   public new void Store(dynamic value) => Count = 2;

   public new void Pair((int Left, int Right) pair) => Count = pair.Left;

   public new void Many(List<(int C, int D)> items) => Count = items.Count;
}

public class Middle : Gauge
{
   public static new void Shift() => Count = 1;
}

public class Bottom : Middle;

public record Ticket(int Id)
{
   public override string ToString() => "ticket";
}

public record VipTicket(int Id, int Seat) : Ticket(Id);

public class Alarm
{
   public virtual event Action? Rang { add { } remove { } }
}

public class Siren : Alarm
{
   public override event Action? Rang;

   public void Ring() => Rang?.Invoke();
}
