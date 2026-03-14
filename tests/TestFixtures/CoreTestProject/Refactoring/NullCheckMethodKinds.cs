namespace CoreTestProject.Refactoring;

/// <summary>
/// Test fixture for AddNullChecksTool - tests various method kinds.
/// DO NOT MODIFY - line numbers are referenced in tests.
/// </summary>
public class NullCheckMethodKinds
{
   private readonly string _value;

   // Constructor with nullable parameter - line 12
   public NullCheckMethodKinds(string? value)
   {
      _value = value ?? "";
   }

   // Constructor with non-nullable parameter - line 18
   public NullCheckMethodKinds(string value, int count)
   {
      _value = value;
   }

   // Operator with reference type parameter - line 24
   public static NullCheckMethodKinds operator +(NullCheckMethodKinds left, string? right)
   {
      return new NullCheckMethodKinds(left._value + right);
   }

   // Property with setter that has nullable value - line 30
   public string? Name
   {
      get => _value;
      set { _ = value; }  // Line 33 - setter body
   }

   // Indexer with nullable parameter - line 37
   public string? this[string? key]
   {
      get => _value;
      set { _ = key; _ = value; }  // Line 40 - setter body
   }

   // Event with add/remove accessors - line 45
   private event EventHandler? _myEvent;
   public event EventHandler? MyEvent
   {
      add { _myEvent += value; }     // Line 47 - add accessor
      remove { _myEvent -= value; }  // Line 48 - remove accessor
   }

   // Static method for comparison - line 52
   public static void StaticMethod(string? value)
   {
      _ = value;
   }

   // Method with local function - line 58
   public void MethodWithLocalFunction()
   {
      LocalFunc("test");

      void LocalFunc(string? param)  // Line 62 - local function
      {
         _ = param;
      }
   }

   // Expression-bodied method (should fail gracefully) - line 69
   public string ExpressionBodied(string? value) => value ?? "";

   // Abstract method equivalent via interface - no body, should fail
}

/// <summary>
/// Class with multiple constructors for overload testing.
/// </summary>
public class MultiConstructorClass
{
   // Constructor 1 - line 80
   public MultiConstructorClass(string? name)
   {
      _ = name;
   }

   // Constructor 2 - line 86
   public MultiConstructorClass(string? name, int age)
   {
      _ = name;
   }

   // Constructor 3 - line 92
   public MultiConstructorClass(object? obj)
   {
      _ = obj;
   }
}
