using System.Collections.Generic;

namespace CoreTestProject.Types;

/// <summary>
/// Class with various overloads for testing disambiguation by parameter types and return type.
/// </summary>
public class OverloadedService
{
   // Overloads with different return types
   public string Convert(int value) => value.ToString();
   public int Convert(string value) => int.Parse(value);

   // Overloads with generic parameter types
   public int Count(List<string> items) => items.Count;
   public int Count(List<int> items) => items.Count;

   // Method with void return
   public void Reset() { }
   public int Reset(int seed) => seed;

   // Method with fully qualified type parameter
   public string Describe(System.Uri uri) => uri.ToString();
}
