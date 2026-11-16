namespace MultiTargetTestProject;

public static class InternalOnly
{
   public static int Outer() => Inner();

   private static int Inner() => 2;
}
