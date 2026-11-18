namespace MemberListsTestProject;

public class Money
{
   public decimal Amount;

   public static Money operator +(Money left, Money right) => new() { Amount = left.Amount + right.Amount };

   public static bool operator true(Money money) => money.Amount != 0;

   public static bool operator false(Money money) => money.Amount == 0;

   public static implicit operator decimal(Money money) => money.Amount;

   public static explicit operator Money(decimal amount) => new() { Amount = amount };

   public void operator +=(Money other) => Amount += other.Amount;
}

public class Wage : Money
{
   public int Hours;

   public static Wage operator +(Wage left, Wage right) => new() { Hours = left.Hours + right.Hours };
}
