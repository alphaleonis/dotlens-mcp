namespace CoreTestProject.Patterns;

// ===========================================
// String literals for FindStringLiterals tests
// ===========================================

/// <summary>
/// Class with various string literals for testing string search.
/// </summary>
public class StringLiteralExamples
{
   // Simple string literals
   public const string AppName = "TestApplication";
   public const string Version = "1.0.0";
   public const string Author = "Test Author";

   // String literal in field
   private readonly string _connectionString = "Server=localhost;Database=TestDb;";

   // String literal in property
   public string DefaultMessage => "Hello, World!";

   // Method with string literals
   public string GetGreeting(string name)
   {
      if (string.IsNullOrEmpty(name))
      {
         return "Hello, Guest!";
      }

      return $"Hello, {name}!";
   }

   // Method with multiple string literals
   public string GetErrorMessage(int errorCode)
   {
      return errorCode switch
      {
         400 => "Bad Request",
         401 => "Unauthorized",
         403 => "Forbidden",
         404 => "Not Found",
         500 => "Internal Server Error",
         502 => "Bad Gateway",
         503 => "Service Unavailable",
         _ => "Unknown Error"
      };
   }

   // Verbatim string literal
   public string GetSqlQuery()
   {
      return @"
         SELECT Id, Name, Email
         FROM Users
         WHERE IsActive = 1
         ORDER BY Name";
   }

   // Raw string literal (C# 11+)
   public string GetJsonTemplate()
   {
      return """
         {
            "name": "Test User",
            "email": "test@example.com",
            "roles": ["admin", "user"]
         }
         """;
   }

   // String interpolation
   public string FormatUserInfo(string name, int age, string city)
   {
      return $"User: {name}, Age: {age}, Location: {city}";
   }

   // URLs and paths
   public string GetApiUrl(string endpoint)
   {
      const string baseUrl = "https://api.example.com/v1";
      return $"{baseUrl}/{endpoint}";
   }

   // Log messages
   public void LogMessages()
   {
      Console.WriteLine("Application starting...");
      Console.WriteLine("Loading configuration...");
      Console.WriteLine("Connecting to database...");
      Console.WriteLine("Application started successfully.");
   }

   // Error messages
   public void ThrowExamples()
   {
      throw new InvalidOperationException("Operation not allowed in current state");
   }

   public void ValidateInput(string input)
   {
      if (string.IsNullOrEmpty(input))
         throw new ArgumentException("Input cannot be null or empty", nameof(input));

      if (input.Length > 100)
         throw new ArgumentException("Input exceeds maximum length of 100 characters", nameof(input));
   }

   // Regex patterns as strings
   public bool IsValidEmail(string email)
   {
      const string pattern = @"^[\w\.-]+@[\w\.-]+\.\w+$";
      return System.Text.RegularExpressions.Regex.IsMatch(email, pattern);
   }

   public bool IsValidPhone(string phone)
   {
      const string pattern = @"^\d{3}-\d{3}-\d{4}$";
      return System.Text.RegularExpressions.Regex.IsMatch(phone, pattern);
   }

   // HTML/XML strings
   public string GetHtmlTemplate()
   {
      return """
         <!DOCTYPE html>
         <html>
         <head>
            <title>Test Page</title>
         </head>
         <body>
            <h1>Welcome</h1>
            <p>This is a test page.</p>
         </body>
         </html>
         """;
   }

   // Special characters
   public string GetSpecialStrings()
   {
      var tab = "Column1\tColumn2\tColumn3";
      var newline = "Line1\nLine2\nLine3";
      var quote = "He said \"Hello\"";
      var unicode = "Unicode: \u00A9 \u2022 \u263A";

      return $"{tab}\n{newline}\n{quote}\n{unicode}";
   }
}

/// <summary>
/// Class with culture-specific strings.
/// </summary>
public static class LocalizableStrings
{
   public const string WelcomeMessage = "Welcome to the application";
   public const string GoodbyeMessage = "Thank you for using our service";
   public const string ErrorTitle = "An error occurred";
   public const string SuccessTitle = "Operation successful";
   public const string ConfirmDelete = "Are you sure you want to delete this item?";
   public const string SaveChanges = "Do you want to save your changes?";
}
