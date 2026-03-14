namespace CoreTestProject.Patterns;

// ===========================================
// Control flow patterns for testing analysis
// ===========================================

/// <summary>
/// Class with various control flow patterns.
/// </summary>
public class ControlFlowExamples
{
   // Simple if-else
   public string Categorize(int value)
   {
      if (value < 0)
      {
         return "Negative";
      }
      else if (value == 0)
      {
         return "Zero";
      }
      else if (value < 10)
      {
         return "Small";
      }
      else if (value < 100)
      {
         return "Medium";
      }
      else
      {
         return "Large";
      }
   }

   // Switch expression
   public string GetDayType(DayOfWeek day) => day switch
   {
      DayOfWeek.Saturday or DayOfWeek.Sunday => "Weekend",
      DayOfWeek.Monday => "Start of week",
      DayOfWeek.Friday => "End of week",
      _ => "Weekday"
   };

   // Switch statement
   public int GetDaysInMonth(int month, int year)
   {
      switch (month)
      {
         case 1:
         case 3:
         case 5:
         case 7:
         case 8:
         case 10:
         case 12:
            return 31;

         case 4:
         case 6:
         case 9:
         case 11:
            return 30;

         case 2:
            return DateTime.IsLeapYear(year) ? 29 : 28;

         default:
            throw new ArgumentOutOfRangeException(nameof(month));
      }
   }

   // For loop
   public int SumRange(int start, int end)
   {
      int sum = 0;
      for (int i = start; i <= end; i++)
      {
         sum += i;
      }
      return sum;
   }

   // While loop
   public int CountDigits(int number)
   {
      if (number == 0) return 1;

      number = Math.Abs(number);
      int count = 0;

      while (number > 0)
      {
         count++;
         number /= 10;
      }

      return count;
   }

   // Do-while loop
   public int FindNextPowerOfTwo(int value)
   {
      int power = 1;

      do
      {
         power *= 2;
      } while (power < value);

      return power;
   }

   // Foreach loop
   public int SumList(List<int> numbers)
   {
      int sum = 0;
      foreach (var number in numbers)
      {
         sum += number;
      }
      return sum;
   }

   // Nested loops
   public int[,] CreateMultiplicationTable(int size)
   {
      var table = new int[size, size];

      for (int i = 0; i < size; i++)
      {
         for (int j = 0; j < size; j++)
         {
            table[i, j] = (i + 1) * (j + 1);
         }
      }

      return table;
   }

   // Loop with break and continue
   public int? FindFirstDivisibleBy(List<int> numbers, int divisor)
   {
      foreach (var number in numbers)
      {
         if (number == 0)
            continue;

         if (number % divisor == 0)
            return number;

         if (number < 0)
            break;
      }

      return null;
   }

   // Try-catch-finally
   public int ParseOrDefault(string input, int defaultValue)
   {
      try
      {
         return int.Parse(input);
      }
      catch (FormatException)
      {
         return defaultValue;
      }
      catch (OverflowException)
      {
         return input.StartsWith('-') ? int.MinValue : int.MaxValue;
      }
      finally
      {
         // Cleanup would go here
      }
   }

   // Multiple catch blocks
   public string ProcessFile(string path)
   {
      try
      {
         return File.ReadAllText(path);
      }
      catch (FileNotFoundException ex)
      {
         return $"File not found: {ex.FileName}";
      }
      catch (UnauthorizedAccessException)
      {
         return "Access denied";
      }
      catch (IOException ex)
      {
         return $"IO error: {ex.Message}";
      }
   }

   // Using statement
   public string ReadFirstLine(string path)
   {
      using var reader = new StreamReader(path);
      return reader.ReadLine() ?? "";
   }

   // Using declaration (C# 8+)
   public byte[] ReadBytes(string path)
   {
      using var stream = File.OpenRead(path);
      using var memory = new MemoryStream();

      stream.CopyTo(memory);
      return memory.ToArray();
   }

   // Pattern matching in switch
   public string DescribeObject(object? obj) => obj switch
   {
      null => "null",
      int i when i > 0 => $"positive int: {i}",
      int i when i < 0 => $"negative int: {i}",
      int => "zero",
      string s when s.Length > 10 => $"long string: {s[..10]}...",
      string s => $"string: {s}",
      IEnumerable<int> list => $"int list with {list.Count()} items",
      _ => $"unknown: {obj.GetType().Name}"
   };

   // Early return pattern
   public decimal CalculateDiscount(decimal price, int quantity, bool isMember)
   {
      if (price <= 0)
         return 0;

      if (quantity <= 0)
         return 0;

      decimal discount = 0;

      if (quantity >= 100)
         discount = 0.20m;
      else if (quantity >= 50)
         discount = 0.15m;
      else if (quantity >= 10)
         discount = 0.10m;
      else if (quantity >= 5)
         discount = 0.05m;

      if (isMember)
         discount += 0.05m;

      return price * quantity * discount;
   }
}
