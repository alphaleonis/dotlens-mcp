namespace DotLens.Infrastructure;

/// <summary>
/// Controls the level of server instructions provided to AI clients.
/// </summary>
public enum InstructionLevel
{
   /// <summary>
   /// No instructions provided.
   /// </summary>
   None,

   /// <summary>
   /// Brief instructions (~150 tokens) with core guidance.
   /// </summary>
   Brief,

   /// <summary>
   /// Detailed instructions (~400 tokens) with comprehensive guidance.
   /// </summary>
   Detailed
}
