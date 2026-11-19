using System.Runtime.CompilerServices;

namespace CSharp15TestProject;

public union Outcome<T>(T, string) where T : class;

public readonly union Quantity(int, double);

[Union]
public struct Voucher : IUnion
{
   public Voucher(int number) => Value = number;

   public Voucher(string code) => Value = code;

   public object? Value { get; }
}

[Union]
public class Reply : IUnion
{
   public Reply(int status) => Value = status;

   public object? Value { get; }
}
