namespace CoreTestProject.Types;

/// <summary>
/// Third part of PartialService in a separate file for testing cross-file partial classes.
/// </summary>
public partial class PartialService
{
   public bool IsEnabled { get; set; }

   public void Reset()
   {
      Name = "Default";
      Count = 0;
      IsEnabled = false;
   }
}
