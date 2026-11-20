namespace UnusedCodeTestProject;

internal readonly record struct PatternedName(string Text, int At);

internal sealed record AssignedName(string Text, int At);

internal sealed record LoopedName(string Text, int At);

internal sealed record VarPatternedName(string Text, int At);

internal sealed record SwitchedName(string Text, int At);

internal sealed record GenericName<T>(T Value, int At);

internal sealed record UndeconstructedName(string Text, int At);

internal record BaseName(string Text);

internal sealed record DerivedName(string Text, int At) : BaseName(Text);

internal sealed class ManualName(string text, int at)
{
   public string Text { get; } = text;

   public int At { get; } = at;

   public void Deconstruct(out string text, out int at)
   {
      text = Text;
      at = At;
   }
}

internal sealed class UndeconstructedManualName(string text)
{
   public string Text { get; } = text;

   public void Deconstruct(out string text, out int at)
   {
      text = Text;
      at = 0;
   }
}

internal sealed class ExtendedName(string text)
{
   public string Text { get; } = text;
}

internal static class ExtendedNameExtensions
{
   public static void Deconstruct(this ExtendedName name, out string text, out int at)
   {
      text = name.Text;
      at = 0;
   }
}

internal static class DeconstructionUses
{
   [System.Runtime.CompilerServices.ModuleInitializer]
   internal static void Run()
   {
      var count = 0;

      PatternedName? patterned = new PatternedName("a", 1);
      if (patterned is (var patternedText, var patternedAt))
         count += patternedText.Length + patternedAt;

      var (assignedText, assignedAt) = new AssignedName("b", 2);
      count += assignedText.Length + assignedAt;

      foreach (var (loopedText, loopedAt) in new[] { new LoopedName("c", 3) })
         count += loopedText.Length + loopedAt;

      VarPatternedName? varPatterned = new VarPatternedName("d", 4);
      if (varPatterned is var (varText, varAt))
         count += varText.Length + varAt;

      object switched = new SwitchedName("e", 5);
      count += switched switch
      {
         SwitchedName (var switchedText, var switchedAt) => switchedText.Length + switchedAt,
         _ => 0,
      };

      object generic = new GenericName<string>("f", 6);
      if (generic is GenericName<string> (var genericValue, var genericAt))
         count += genericValue.Length + genericAt;

      count += new UndeconstructedName("g", 7).Text.Length;

      DerivedName? derived = new DerivedName("h", 8);
      if (derived is (var derivedText, var derivedAt))
         count += derivedText.Length + derivedAt;

      object manual = new ManualName("i", 9);
      if (manual is ManualName (var manualText, var manualAt))
         count += manualText.Length + manualAt;

      _ = new UndeconstructedManualName("j");

      object extended = new ExtendedName("k");
      if (extended is ExtendedName (var extendedText, var extendedAt))
         count += extendedText.Length + extendedAt;

      OuterName? outer = new OuterName("l", new NestedName("m", 10));
      if (outer is var (outerText, (nestedText, nestedAt)))
         count += outerText.Length + nestedText.Length + nestedAt;

      CustomDeconstructedName? custom = new CustomDeconstructedName("n", 11);
      if (custom is (var customText, var customAt))
         count += customText.Length + customAt;

      Console.WriteLine(count);
   }
}

internal sealed record OuterName(string Text, NestedName Inner);

internal sealed record NestedName(string Text, int At);

internal sealed record CustomDeconstructedName(string Text, int At)
{
   public void Deconstruct(out string Text, out int At)
   {
      Text = this.Text;
      At = 0;
   }
}
