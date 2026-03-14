namespace CoreTestProject.Patterns;

// ===========================================
// Custom attributes for testing GetAttributes
// ===========================================

/// <summary>
/// Custom attribute for marking classes.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public class ServiceAttribute : Attribute
{
   public string Name { get; }
   public bool IsSingleton { get; set; }

   public ServiceAttribute(string name)
   {
      Name = name;
   }
}

/// <summary>
/// Custom attribute for methods.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class LogAttribute : Attribute
{
   public string Level { get; }
   public string? Message { get; set; }

   public LogAttribute(string level)
   {
      Level = level;
   }
}

/// <summary>
/// Custom attribute for properties.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public class ValidateAttribute : Attribute
{
   public bool Required { get; set; }
   public int MinLength { get; set; }
   public int MaxLength { get; set; } = int.MaxValue;
   public string? Pattern { get; set; }
}

/// <summary>
/// Custom attribute for parameters.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public class NotNullAttribute : Attribute { }

/// <summary>
/// Attribute with enum property.
/// </summary>
[AttributeUsage(AttributeTargets.All)]
public class PriorityAttribute : Attribute
{
   public Types.Priority Priority { get; }

   public PriorityAttribute(Types.Priority priority)
   {
      Priority = priority;
   }
}

// ===========================================
// Classes using attributes
// ===========================================

[Service("UserService", IsSingleton = true)]
[Priority(Types.Priority.High)]
public class UserService
{
   [Validate(Required = true, MinLength = 1, MaxLength = 100)]
   public string Username { get; set; } = "";

   [Validate(Required = true, Pattern = @"^[\w\.-]+@[\w\.-]+\.\w+$")]
   public string Email { get; set; } = "";

   [Validate(MinLength = 8)]
   private string _password = "";

   [Log("Info")]
   public void CreateUser([NotNull] string username)
   {
      Username = username;
   }

   [Log("Debug", Message = "Getting user by ID")]
   [Log("Info", Message = "User retrieved")]
   public string GetUserById(int id)
   {
      return $"User_{id}";
   }

   [Obsolete("Use CreateUser instead")]
   public void AddUser(string name) { }
}

[Service("DataService")]
[Serializable]
public class DataService
{
   [NonSerialized]
   private int _cachedValue;

   [field: NonSerialized]
   public int TransientProperty { get; set; }

   public void Process() { }
}

/// <summary>
/// Class demonstrating built-in attributes.
/// </summary>
[System.Diagnostics.DebuggerDisplay("Value={Value}")]
public class DebugDisplayClass
{
   public int Value { get; set; }

   [System.Diagnostics.Conditional("DEBUG")]
   public void DebugOnlyMethod()
   {
      Console.WriteLine("Debug only");
   }

   [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
   public int FastMethod(int x) => x * 2;
}
