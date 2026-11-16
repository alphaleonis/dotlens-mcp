namespace CSharp15TestProject;

public struct Meter
{
   public int Reading;
}

public static class MeterExtensions
{
   extension(Meter meter)
   {
      public int Copy() => meter.Reading;
   }

   extension(ref Meter meter)
   {
      public void Advance() => meter.Reading++;
   }

   extension(in Meter meter)
   {
      public int Peek() => meter.Reading;
   }

   extension(ref readonly Meter meter)
   {
      public int Look() => meter.Reading;
   }

   extension(scoped ref Meter meter)
   {
      public void Reset() => meter.Reading = 0;
   }

   extension(scoped Span<int> span)
   {
      public int Size() => span.Length;
   }

   extension<T>(ref T value) where T : struct
   {
      public void Clear() => value = default;
   }
}
