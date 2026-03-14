namespace CoreTestProject;

/// <summary>
/// Test class for AddNullChecksTool scenarios.
/// </summary>
public class NullCheckTestClass
{
   /// <summary>
   /// Method with non-nullable reference type parameter (in nullable-enabled context).
   /// Line 12: For testing includeNonNullable=true
   /// </summary>
   public void NonNullableParam(string value)
   {
      Console.WriteLine(value.Length);
   }

   /// <summary>
   /// Method with nullable reference type parameter.
   /// Line 21: For testing default behavior (includeNonNullable=false)
   /// </summary>
   public void NullableParam(string? value)
   {
      Console.WriteLine(value?.Length ?? 0);
   }

   /// <summary>
   /// Method with mixed parameters: one nullable, one non-nullable.
   /// Line 30: For testing both scenarios
   /// </summary>
   public void MixedParams(string nonNullable, string? nullable)
   {
      Console.WriteLine(nonNullable.Length + (nullable?.Length ?? 0));
   }

   /// <summary>
   /// Method with only value type parameters (no reference types).
   /// Line 39: Should fail for both includeNonNullable=true and false
   /// </summary>
   public void ValueTypeOnly(int x, double y)
   {
      Console.WriteLine(x + y);
   }
}
