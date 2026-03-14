// ===========================================
// Fixable Diagnostics for CodeFix tests
// ===========================================
// This file contains code with INTENTIONAL compiler diagnostics
// that have available code fixes. Used by GetCodeFixesTool tests.
//
// IMPORTANT: Do NOT fix these issues - they are test fixtures!
//
// Expected diagnostics with available fixes:
// - CS0168: Variable declared but never used (line 27)
// - CS1998: Async method lacks await (line 35)
// ===========================================

#pragma warning disable IDE0059 // Unnecessary assignment
#pragma warning disable IDE0060 // Unused parameter

namespace CoreTestProject.Analysis;

public class FixableDiagnostics
{
   /// <summary>
   /// Method with CS0168: Variable 'unusedVariable' is declared but never used.
   /// Fix available: "Remove unused variable"
   /// </summary>
   public void MethodWithUnusedVariable()
   {
      int unusedVariable; // CS0168 at line 27
      Console.WriteLine("Hello");
   }

   /// <summary>
   /// Method with CS1998: Async method lacks 'await' operators.
   /// Fix available: "Remove 'async' modifier"
   /// </summary>
   public async Task AsyncMethodWithoutAwait() // CS1998 at line 35
   {
      Console.WriteLine("No await here");
   }

   /// <summary>
   /// Method with proper async/await for comparison (no diagnostics).
   /// </summary>
   public async Task AsyncMethodWithAwait()
   {
      await Task.Delay(1);
   }
}
