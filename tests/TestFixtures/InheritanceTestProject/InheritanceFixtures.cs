namespace InheritanceTestProject;

// ===========================================
// Scenario 1: Abstract base class with virtual/abstract methods
// ===========================================

public abstract class Animal
{
    // Virtual method with default implementation
    public virtual string Speak() => "...";

    // Abstract method - must be overridden
    public abstract void Move();

    // Virtual property
    public virtual int Legs { get; } = 0;

    // Non-virtual method - cannot be overridden
    public string GetTypeName() => base.GetType().Name;
}

public class Dog : Animal
{
    public override string Speak() => "Woof!";

    public override void Move() => Console.WriteLine("Running on four legs");

    public override int Legs => 4;
}

public class Cat : Animal
{
    public override string Speak() => "Meow!";

    public override void Move() => Console.WriteLine("Prowling silently");

    public override int Legs => 4;
}

public class Bird : Animal
{
    public override string Speak() => "Tweet!";

    public override void Move() => Console.WriteLine("Flying through the air");

    public override int Legs => 2;
}

// ===========================================
// Scenario 2: Multi-level override chain (Base -> Override1 -> Override2)
// ===========================================

public class Vehicle
{
    public virtual void Start() => Console.WriteLine("Starting vehicle");

    public virtual int MaxSpeed => 100;
}

public class Car : Vehicle
{
    public override void Start() => Console.WriteLine("Starting car engine");

    public override int MaxSpeed => 200;
}

public class SportsCar : Car
{
    public override void Start()
    {
        Console.WriteLine("Starting sports car with turbo");
    }

    public override int MaxSpeed => 350;
}

// ===========================================
// Scenario 3: Interface with implementations (implicit)
// ===========================================

public interface IRepository
{
    void Save(object entity);
    object? FindById(int id);
    int Count { get; }
}

public class InMemoryRepository : IRepository
{
    private readonly List<object> _items = new();

    // Implicit interface implementation
    public void Save(object entity) => _items.Add(entity);

    // Implicit interface implementation
    public object? FindById(int id) => _items.ElementAtOrDefault(id);

    // Implicit interface implementation (property)
    public int Count => _items.Count;
}

public class SqlRepository : IRepository
{
    public void Save(object entity) => throw new NotImplementedException();

    public object? FindById(int id) => throw new NotImplementedException();

    public int Count => 0;
}

// ===========================================
// Scenario 4: Explicit interface implementation
// ===========================================

public interface IFormatter
{
    string Format(string input);
}

public interface IValidator
{
    bool Validate(string input);
}

public class TextProcessor : IFormatter, IValidator
{
    // Explicit interface implementation
    string IFormatter.Format(string input) => input.Trim();

    // Explicit interface implementation
    bool IValidator.Validate(string input) => !string.IsNullOrEmpty(input);

    // Regular method (not an interface implementation)
    public string Process(string input) => input.ToUpper();
}

// ===========================================
// Scenario 5: Method that both overrides AND implements interface
// ===========================================

public interface IDrawable
{
    void Draw();
}

public abstract class Shape : IDrawable
{
    // This method both: 1) implements IDrawable.Draw, and 2) is virtual
    public virtual void Draw() => Console.WriteLine("Drawing shape");

    public abstract double Area { get; }
}

public class Circle : Shape
{
    public double Radius { get; set; }

    // This method both: 1) overrides Shape.Draw, and 2) implicitly implements IDrawable.Draw
    public override void Draw() => Console.WriteLine($"Drawing circle with radius {Radius}");

    public override double Area => Math.PI * Radius * Radius;
}

public class Rectangle : Shape
{
    public double Width { get; set; }
    public double Height { get; set; }

    public override void Draw() => Console.WriteLine($"Drawing rectangle {Width}x{Height}");

    public override double Area => Width * Height;
}

// ===========================================
// Scenario 6: Sealed override (cannot be further overridden)
// ===========================================

public class BaseLogger
{
    public virtual void Log(string message) => Console.WriteLine(message);
}

public class FileLogger : BaseLogger
{
    public sealed override void Log(string message)
    {
        File.AppendAllText("log.txt", message + Environment.NewLine);
    }
}

// Cannot override Log in this class because FileLogger sealed it
public class TimestampedFileLogger : FileLogger
{
    // This would cause a compile error if uncommented:
    // public override void Log(string message) { }
}

// ===========================================
// Scenario 7: Non-virtual method (for negative testing)
// ===========================================

public static class StringUtilities
{
    // Static method - cannot be virtual
    public static string Reverse(string input)
    {
        var chars = input.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }

    public static bool IsPalindrome(string input)
    {
        var cleaned = input.ToLower().Replace(" ", "");
        return cleaned == Reverse(cleaned);
    }
}

public class Calculator
{
    // Non-virtual instance method
    public int Add(int a, int b) => a + b;

    // Non-virtual property
    public string Name => "Calculator";
}

// ===========================================
// Scenario 8: User-defined conversion operators
// ===========================================

/// <summary>
/// Type with user-defined implicit conversion operator.
/// Used to test that ClassifyConversion correctly detects user-defined conversions
/// when the compilation context matters.
/// </summary>
public class Celsius
{
    public double Temperature { get; }

    public Celsius(double temperature) => Temperature = temperature;

    // User-defined implicit conversion from Celsius to Fahrenheit
    public static implicit operator Fahrenheit(Celsius c) => new Fahrenheit(c.Temperature * 9 / 5 + 32);
}

public class Fahrenheit
{
    public double Temperature { get; }

    public Fahrenheit(double temperature) => Temperature = temperature;

    // User-defined explicit conversion from Fahrenheit to Celsius
    public static explicit operator Celsius(Fahrenheit f) => new Celsius((f.Temperature - 32) * 5 / 9);
}
