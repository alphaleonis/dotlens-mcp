namespace CSharp15TestProject;

public delegate ref readonly int SlotReader<T>(T[] items, string? label) where T : struct;

public interface IProducer<in TInput, out TOutput> where TOutput : class
{
   TOutput Produce(TInput input);
}

public class Inventory<TItem> where TItem : notnull, IComparable<TItem>
{
   private int _count;

   public ref int Count() => ref _count;

   public ref readonly int Peek() => ref _count;

   public string? Describe(string? prefix, string suffix, int? width = null) => prefix ?? suffix;

   public void Schedule(DayOfWeek day = DayOfWeek.Friday, decimal rate = 2.5m, float scale = 1.5f, double limit = double.PositiveInfinity, FileShare share = FileShare.Read | FileShare.Delete) { }

   public TResult Convert<TResult>(TItem item) where TResult : struct => default;
}

#nullable disable
public static class ObliviousEcho
{
   public static string Echo(string text) => text;
}
#nullable restore

public static class Doubling
{
   public static int Twice(this int value) => value * 2;
}

public interface IFirstRunner
{
   void Run();
}

public interface ISecondRunner
{
   void Run();
}

public sealed class DualRunner : IFirstRunner, ISecondRunner
{
   void IFirstRunner.Run() { }

   void ISecondRunner.Run() { }
}

public unsafe struct SampleBuffer
{
   public fixed int Samples[4];
}

public interface IConvertsTo<TSelf> where TSelf : IConvertsTo<TSelf>
{
   static abstract explicit operator int(TSelf value);

   static abstract explicit operator checked int(TSelf value);

   static abstract implicit operator long(TSelf value);
}

public sealed class Reading : IConvertsTo<Reading>
{
   static explicit IConvertsTo<Reading>.operator int(Reading value) => 0;

   static explicit IConvertsTo<Reading>.operator checked int(Reading value) => 0;

   static implicit IConvertsTo<Reading>.operator long(Reading value) => 0;
}

public partial class SplitLedger : IDisposable
{
   public void Dispose() { }
}

public partial class SplitLedger : ICloneable, IDisposable
{
   public object Clone() => this;
}

public enum Shade : byte
{
   Light,
   Dark,
}

public static class Guards
{
   public static ArgumentException Missing() => new ArgumentNullException("value");
}
