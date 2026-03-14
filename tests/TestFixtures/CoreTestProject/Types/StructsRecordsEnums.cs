namespace CoreTestProject.Types;

// ===========================================
// Structs
// ===========================================

/// <summary>
/// Simple value type struct.
/// </summary>
public struct Point
{
   public int X { get; set; }
   public int Y { get; set; }

   public Point(int x, int y)
   {
      X = x;
      Y = y;
   }

   public readonly double Distance => Math.Sqrt(X * X + Y * Y);

   public readonly override string ToString() => $"({X}, {Y})";
}

/// <summary>
/// Readonly struct for immutable value types.
/// </summary>
public readonly struct ImmutablePoint
{
   public int X { get; init; }
   public int Y { get; init; }

   public ImmutablePoint(int x, int y)
   {
      X = x;
      Y = y;
   }

   public ImmutablePoint WithX(int x) => new(x, Y);
   public ImmutablePoint WithY(int y) => new(X, y);
}

/// <summary>
/// Record struct (value type with record semantics).
/// </summary>
public record struct Money(decimal Amount, string Currency)
{
   public Money Add(Money other)
   {
      if (Currency != other.Currency)
         throw new InvalidOperationException("Currency mismatch");
      return this with { Amount = Amount + other.Amount };
   }
}

// ===========================================
// Records
// ===========================================

/// <summary>
/// Simple record with positional parameters.
/// </summary>
public record Person(string FirstName, string LastName)
{
   public string FullName => $"{FirstName} {LastName}";
}

/// <summary>
/// Record with additional members.
/// </summary>
public record Employee(string FirstName, string LastName, int EmployeeId) : Person(FirstName, LastName)
{
   public string Department { get; init; } = "Unknown";

   public bool IsManager { get; init; }

   public string GetEmployeeInfo()
   {
      return $"[{EmployeeId}] {FullName} - {Department}";
   }
}

/// <summary>
/// Record class (explicit reference type record).
/// </summary>
public record class Customer
{
   public required string Name { get; init; }
   public required string Email { get; init; }
   public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

// ===========================================
// Enums
// ===========================================

/// <summary>
/// Simple enum.
/// </summary>
public enum Status
{
   Unknown,
   Pending,
   Active,
   Completed,
   Cancelled
}

/// <summary>
/// Enum with explicit values.
/// </summary>
public enum Priority
{
   Low = 1,
   Normal = 5,
   High = 10,
   Critical = 100
}

/// <summary>
/// Flags enum for bitwise operations.
/// </summary>
[Flags]
public enum Permissions
{
   None = 0,
   Read = 1,
   Write = 2,
   Execute = 4,
   Delete = 8,
   Admin = Read | Write | Execute | Delete
}

/// <summary>
/// Enum with underlying byte type.
/// </summary>
public enum LogLevel : byte
{
   Trace = 0,
   Debug = 1,
   Info = 2,
   Warning = 3,
   Error = 4,
   Critical = 5
}

// ===========================================
// Delegates
// ===========================================

/// <summary>
/// Simple delegate type.
/// </summary>
public delegate void SimpleAction();

/// <summary>
/// Delegate with parameters.
/// </summary>
public delegate int Calculator(int a, int b);

/// <summary>
/// Generic delegate.
/// </summary>
public delegate TResult Transformer<in TInput, out TResult>(TInput input);

/// <summary>
/// Async delegate.
/// </summary>
public delegate Task<T> AsyncFactory<T>(CancellationToken cancellationToken);
