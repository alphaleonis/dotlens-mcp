namespace CSharp15TestProject;

public sealed class Badge
{
   public string Label { get; init; } = "";

   [System.Runtime.CompilerServices.IndexerName("Slot")]
   public int this[int index] => index;
}
