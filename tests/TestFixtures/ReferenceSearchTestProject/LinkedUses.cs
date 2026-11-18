namespace ReferenceSearchTestProject;

public sealed class Counter
{
   public int this[int index] { get => index; set { } }

   public int Total { get; set; }

   public void Add(int amount) => Total += amount;
}

public static class CounterUsage
{
   public static int Use(Counter counter, Counter? maybe)
   {
      counter.Add(1);
      counter[0] = 2;
      maybe?[1] = 3;
      return counter[2] + counter.Total;
   }
}
