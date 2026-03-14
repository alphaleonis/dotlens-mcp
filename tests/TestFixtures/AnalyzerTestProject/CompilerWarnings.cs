// This file intentionally contains code that triggers compiler warnings
// for testing roslyn_get_diagnostics baseline behavior

namespace AnalyzerTestProject;

public class CompilerWarnings
{
   // CS0168: Variable declared but never used
   public void UnusedVariable()
   {
      int unusedVar;
   }

   // CS0219: Variable is assigned but never used
   public void AssignedButUnused()
   {
      int assignedButUnused = 42;
   }

   // CS0414: Field is assigned but never used
   private int _unusedField = 0;

   // CS8618 will NOT fire because nullable is enabled and this is intentional
   // Let's use a different warning

   // Obsolete method usage (CS0612)
   [Obsolete]
   public void DeprecatedMethod() { }

   public void CallsDeprecated()
   {
      DeprecatedMethod(); // CS0612: 'DeprecatedMethod' is obsolete
   }
}
