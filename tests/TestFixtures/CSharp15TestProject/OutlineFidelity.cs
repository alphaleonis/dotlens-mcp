namespace CSharp15TestProject;

public interface IDial
{
   int Read();

   int Level { get; }

   event EventHandler? Changed;

   int this[int index] { get; }
}

public interface IScale
{
   int Read();
}

public interface ISummable<TSelf> where TSelf : ISummable<TSelf>
{
   static abstract TSelf operator +(TSelf left, TSelf right);

   static abstract explicit operator int(TSelf value);
}

public sealed class Dial : IDial, IScale, ISummable<Dial>
{
   int IDial.Read() => 1;

   int IScale.Read() => 2;

   int IDial.Level => 0;

   event EventHandler? IDial.Changed { add { } remove { } }

   int IDial.this[int index] => index;

   static Dial ISummable<Dial>.operator +(Dial left, Dial right) => left;

   static explicit ISummable<Dial>.operator int(Dial value) => 0;
}

public readonly record struct Credit(decimal Amount)
{
   public static Credit operator -(Credit value) => new(-value.Amount);

   public static Credit operator checked -(Credit value) => new(checked(-value.Amount));

   public static explicit operator int(Credit value) => (int)value.Amount;

   public static explicit operator checked int(Credit value) => checked((int)value.Amount);
}

public record class Receipt(string Number);

public unsafe struct Packet
{
   public fixed int Data[4];

   public fixed byte Header[2], Footer[8];
}

[Serializable]
public class Archived
{
   private const string Unit = "cm";

   /// <summary>Keeps the archive.</summary>
   [Obsolete("Use Store")]
   /// <summary>Saves the archive.</summary>
   public void Save() { }

   public void Store([System.ComponentModel.Description(@"first
second ""quoted""")] string note = """
      line one
        line two
      """) { }

   public void Measure(string label = $"""
      {Unit} wide
      """, string verbatim = $@"{Unit}
{{tall}}") { }

   public void Label(string text = $$"""
      {{{Unit}}}

        {x}
      """) { }
}

public enum Tone
{
   [Obsolete]
   Low,
   /// <summary>The upper tone.</summary>
   High,
}
