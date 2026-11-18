namespace MemberListsTestProject;

public interface IAddable<TSelf> where TSelf : IAddable<TSelf>
{
   static abstract TSelf operator +(TSelf left, TSelf right);

   static abstract explicit operator int(TSelf value);

   void Reset();
}

public struct Tally : IAddable<Tally>
{
   public int Value;

   static Tally IAddable<Tally>.operator +(Tally left, Tally right) => new() { Value = left.Value + right.Value };

   static explicit IAddable<Tally>.operator int(Tally value) => value.Value;

   void IAddable<Tally>.Reset() => Value = 0;

   public static Tally operator -(Tally left, Tally right) => new() { Value = left.Value - right.Value };

   public readonly int Twice() => Value * 2;
}
