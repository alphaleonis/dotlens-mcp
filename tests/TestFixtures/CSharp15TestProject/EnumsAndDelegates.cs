namespace CSharp15TestProject;

public enum Channel
{
   Email,
   Sms,
}

public delegate bool Filter<T>(T item);

public class Dispatcher
{
   public enum Priority
   {
      Low,
      High,
   }

   public delegate void Handler(Priority priority);
}
