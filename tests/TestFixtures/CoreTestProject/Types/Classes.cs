using System.Collections.Generic;

namespace CoreTestProject.Types;

// ===========================================
// Various class types for testing
// ===========================================

/// <summary>
/// A regular public class with various members.
/// </summary>
public class SampleClass
{
   // Fields
   private int _privateField;
   protected string _protectedField = "";
   internal bool _internalField;
   public readonly double PublicReadonlyField = 3.14;
   public const string ConstantField = "constant";

   // Properties
   public int Id { get; set; }
   public string Name { get; private set; } = "";
   public bool IsActive { get; init; }

   // Computed property
   public string DisplayName => $"{Name} ({Id})";

   // Constructors
   public SampleClass() { }

   public SampleClass(int id)
   {
      Id = id;
   }

   public SampleClass(int id, string name) : this(id)
   {
      Name = name;
   }

   // Methods
   public void DoSomething()
   {
      _privateField++;
   }

   public int Calculate(int a, int b)
   {
      return a + b;
   }

   // Overloaded methods
   public string Format(string value) => value.Trim();
   public string Format(int value) => value.ToString();
   public string Format(string value, string prefix) => prefix + value;

   // Static method
   public static SampleClass Create(int id) => new(id);

   // Private method
   private void PrivateMethod() { }

   // Protected method
   protected virtual void ProtectedMethod() { }
}

/// <summary>
/// Internal class for testing access modifiers.
/// </summary>
internal class InternalClass
{
   public string Value { get; set; } = "";

   internal void InternalMethod() { }
}

/// <summary>
/// Sealed class that cannot be inherited.
/// </summary>
public sealed class SealedClass
{
   public string Data { get; }

   public SealedClass(string data)
   {
      Data = data;
   }
}

/// <summary>
/// Class used for testing ambiguity detection.
/// This same FQN (CoreTestProject.Types.AmbiguousTestClass) is also defined
/// in ConflictingTypesProject to test the type lookup ambiguity handling.
/// </summary>
public class AmbiguousTestClass
{
   public string OriginalProjectMarker => "From CoreTestProject";
   public int OriginalId { get; set; }
   public void OriginalMethod() { }
}

/// <summary>
/// Static utility class with extension methods.
/// </summary>
public static class StringExtensions
{
   public static string Reverse(this string input)
   {
      var chars = input.ToCharArray();
      Array.Reverse(chars);
      return new string(chars);
   }

   public static bool IsNullOrEmpty(this string? input)
   {
      return string.IsNullOrEmpty(input);
   }

   public static string? ToUpperOrNull(this string? input)
   {
      return input?.ToUpper();
   }
}

/// <summary>
/// Nested class example.
/// </summary>
public class OuterClass
{
   public string OuterValue { get; set; } = "";

   public class NestedPublicClass
   {
      public string NestedValue { get; set; } = "";

      public void NestedMethod() { }
   }

   private class NestedPrivateClass
   {
      public int SecretValue { get; set; }
   }

   public NestedPublicClass CreateNested() => new();
}
