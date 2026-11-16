namespace CSharp15TestProject;

public static class DelegateSignatures
{
   public delegate int Selector(ref int first, out int second, in int third, params int[] rest);

   internal delegate string? Formatter<T>(T? value, string? format = "G", int width = 0) where T : struct;
}
