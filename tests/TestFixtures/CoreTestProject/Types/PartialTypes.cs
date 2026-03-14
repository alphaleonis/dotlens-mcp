namespace CoreTestProject.Types;

/// <summary>
/// First part of a partial class for testing allLocations in go_to_definition.
/// </summary>
public partial class PartialService
{
   public string Name { get; set; } = "Default";

   public void Initialize()
   {
      Name = "Initialized";
   }
}

/// <summary>
/// Second part of PartialService in the same file.
/// </summary>
public partial class PartialService
{
   public int Count { get; set; }

   public void Process()
   {
      Count++;
   }
}

/// <summary>
/// A non-partial class for comparison in tests.
/// </summary>
public class NonPartialService
{
   public string Value { get; set; } = "";
}
