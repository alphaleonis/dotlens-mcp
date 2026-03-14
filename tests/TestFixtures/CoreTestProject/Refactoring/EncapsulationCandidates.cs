namespace CoreTestProject.Refactoring;

// ===========================================
// Code suitable for Encapsulate Field refactoring
// ===========================================

/// <summary>
/// Class with public fields that should be encapsulated.
/// </summary>
public class PersonWithFields
{
   // These public fields should be encapsulated as properties
   public string FirstName;
   public string LastName;
   public int Age;
   public DateTime BirthDate;

   // Internal field
   internal decimal Salary;

   // Protected field
   protected string Department = "";

   public PersonWithFields()
   {
      FirstName = "";
      LastName = "";
   }

   public PersonWithFields(string firstName, string lastName)
   {
      FirstName = firstName;
      LastName = lastName;
   }

   public string GetFullName()
   {
      return $"{FirstName} {LastName}";
   }
}

/// <summary>
/// Class with mixed fields and properties.
/// </summary>
public class MixedFieldsClass
{
   // Public field (should be encapsulated)
   public int Counter;

   // Private field with manual getter/setter (already encapsulated pattern)
   private string _name = "";
   public string Name
   {
      get => _name;
      set => _name = value?.Trim() ?? "";
   }

   // Public field with validation logic needed (good candidate for encapsulation)
   public decimal Price;

   // Read-only field (might want to encapsulate with getter only)
   public readonly string Id;

   public MixedFieldsClass(string id)
   {
      Id = id;
   }

   public void IncrementCounter()
   {
      Counter++;
   }
}

// ===========================================
// Code suitable for Extract Interface refactoring
// ===========================================

/// <summary>
/// Class with public methods that could form an interface.
/// </summary>
public class EmailSender
{
   private readonly string _smtpServer;

   public EmailSender(string smtpServer)
   {
      _smtpServer = smtpServer;
   }

   // These public methods could be extracted to IEmailSender
   public void Send(string to, string subject, string body)
   {
      Console.WriteLine($"Sending to {to}: {subject}");
   }

   public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
   {
      await Task.Delay(100, cancellationToken);
      Console.WriteLine($"Sent to {to}: {subject}");
   }

   public bool Validate(string emailAddress)
   {
      return emailAddress.Contains('@');
   }

   // Private helper - would not be part of extracted interface
   private string FormatMessage(string subject, string body)
   {
      return $"Subject: {subject}\n\n{body}";
   }
}

/// <summary>
/// Another class suitable for interface extraction.
/// </summary>
public class FileStorage
{
   private readonly string _basePath;

   public FileStorage(string basePath)
   {
      _basePath = basePath;
   }

   public void Save(string key, byte[] data)
   {
      File.WriteAllBytes(Path.Combine(_basePath, key), data);
   }

   public byte[]? Load(string key)
   {
      var path = Path.Combine(_basePath, key);
      return File.Exists(path) ? File.ReadAllBytes(path) : null;
   }

   public void Delete(string key)
   {
      var path = Path.Combine(_basePath, key);
      if (File.Exists(path))
         File.Delete(path);
   }

   public bool Exists(string key)
   {
      return File.Exists(Path.Combine(_basePath, key));
   }

   public IEnumerable<string> ListKeys()
   {
      return Directory.GetFiles(_basePath).Select(Path.GetFileName)!;
   }
}

// ===========================================
// Code suitable for Generate Constructor refactoring
// ===========================================

/// <summary>
/// Class without constructors (use Generate Constructor).
/// </summary>
public class ConfigurationSettings
{
   public string DatabaseConnectionString { get; set; } = "";
   public string ApiEndpoint { get; set; } = "";
   public int TimeoutSeconds { get; set; }
   public bool EnableLogging { get; set; }
   public LogLevel LogLevel { get; set; }
}

public enum LogLevel { Debug, Info, Warning, Error }

/// <summary>
/// Class with fields that need constructor initialization.
/// </summary>
public class ServiceConfiguration
{
   private readonly string _serviceName;
   private readonly int _port;
   private readonly bool _useSsl;

   // No constructor yet - Generate Constructor would create one
   // that initializes these readonly fields

   public string ServiceName => _serviceName;
   public int Port => _port;
   public bool UseSsl => _useSsl;

   // Manual constructor for reference
   public ServiceConfiguration(string serviceName, int port, bool useSsl)
   {
      _serviceName = serviceName;
      _port = port;
      _useSsl = useSsl;
   }
}

// ===========================================
// Code suitable for Generate Equality Members refactoring
// ===========================================

/// <summary>
/// Class without equality members (use Generate Equality).
/// </summary>
public class Coordinate
{
   public double Latitude { get; set; }
   public double Longitude { get; set; }

   // No Equals, GetHashCode, or == operator defined
   // Generate Equality Members would add these
}

/// <summary>
/// Another class suitable for equality generation.
/// </summary>
public class OrderKey
{
   public int OrderId { get; }
   public DateTime OrderDate { get; }
   public string CustomerId { get; }

   public OrderKey(int orderId, DateTime orderDate, string customerId)
   {
      OrderId = orderId;
      OrderDate = orderDate;
      CustomerId = customerId;
   }

   // No equality members - good candidate for generation
}
