namespace CoreTestProject.Analysis;

// Each comment gives the cognitive complexity increments of the constructs on its line.
public static class CognitiveIndirectRecursion
{
   public static bool IsEvenCount(int n)
      => n == 0 || IsOddCount(n - 1); // +1 for ||, +1 for the cycle with IsOddCount

   public static bool IsOddCount(int n)
      => n != 0 && IsEvenCount(n - 1); // +1 for &&, +1 for the cycle with IsEvenCount

   public static bool IsEvenEntry(int n)
      => IsEvenCount(n); // Calls into a cycle without being in it

   public static int StepA(int n)
      => n <= 0 ? 0 : StepB(n - 1); // +1 for ?:, +1 for the cycle through StepB and StepC

   public static int StepB(int n)
      => StepC(n); // +1 for the cycle

   public static int StepC(int n)
      => StepA(n); // +1 for the cycle

   public static int Walk(int n)
   {
      if (n <= 0) // +1
         return 0;
      return Walk(n - 1) + Wander(n - 1); // +1 once for the self-call and the cycle with Wander
   }

   public static int Wander(int n)
      => Walk(n); // +1 for the cycle with Walk

   public static int Bounce(int n)
   {
      return Serve(n);

      int Serve(int k)
         => k <= 0 ? 0 : Volley(k - 1); // +2 for ?: in Bounce, +1 alone; +1 for the cycle with Volley

      int Volley(int k)
         => Serve(k); // +1 for the cycle with Serve
   }

   public static int Launch(int n)
      => PassOn(n, Launch); // A method group is not a call

   private static int PassOn(int n, Func<int, int> next)
      => n <= 0 ? 0 : next(n - 1); // +1 for ?:; a delegate invocation is not a call to Launch
}

public interface ICognitiveRunner
{
   int Proceed(int n);
}

public sealed class CognitiveRunner : ICognitiveRunner
{
   public int Proceed(int n)
      => Relay(n);

   private int Relay(int n)
      => ((ICognitiveRunner)this).Proceed(n); // Binds to the interface method, which has no body
}
