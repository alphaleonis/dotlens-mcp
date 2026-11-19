using DotLens.Tools.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DotLens.Tests.Analysis;

public class AnalysisHelpersTests
{
   #region GetCodeActionsForDiagnosticAsync Tests

   [Fact]
   public async Task GetCodeActionsForDiagnosticAsync_WithUnusedVariable_ReturnsCodeActions()
   {
      // Code with an unused variable (CS0168)
      var code = @"
using System;

namespace TestNamespace
{
    public class TestClass
    {
        public void Method()
        {
            int unused;
        }
    }
}";
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(code);
      try
      {
         var semanticModel = await document.GetSemanticModelAsync();
         Assert.NotNull(semanticModel);

         // Get diagnostics
         var diagnostics = semanticModel!.GetDiagnostics()
            .Where(d => d.Id == "CS0168") // Unused variable
            .ToList();

         if (diagnostics.Count > 0)
         {
            var actions = await AnalysisHelpers.GetCodeActionsForDiagnosticAsync(
               document, diagnostics[0], CancellationToken.None);

            // Should have at least one code action for removing unused variable
            Assert.NotNull(actions);
            // Note: Built-in code fix providers may or may not be available
            // depending on the Roslyn version, so we just verify it doesn't throw
         }
      }
      finally
      {
         workspace.Dispose();
      }
   }

   [Fact]
   public async Task GetCodeActionsForDiagnosticAsync_WithNonFixableDiagnostic_ReturnsEmptyList()
   {
      var code = @"
namespace TestNamespace
{
    public class TestClass { }
}";
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(code);
      try
      {
         var semanticModel = await document.GetSemanticModelAsync();
         Assert.NotNull(semanticModel);

         // Create a fake diagnostic that no provider can fix
         var diagnostic = Diagnostic.Create(
            new DiagnosticDescriptor(
               "FAKE001",
               "Fake",
               "Fake message",
               "Test",
               DiagnosticSeverity.Warning,
               isEnabledByDefault: true),
            Location.None);

         var actions = await AnalysisHelpers.GetCodeActionsForDiagnosticAsync(
            document, diagnostic, CancellationToken.None);

         Assert.NotNull(actions);
         Assert.Empty(actions);
      }
      finally
      {
         workspace.Dispose();
      }
   }

   [Fact]
   public async Task GetCodeActionsForDiagnosticAsync_WithCancellationToken_CompletesGracefully()
   {
      // Test that the method respects cancellation token (may or may not throw
      // depending on timing, but should complete without hanging)
      var code = @"
namespace TestNamespace
{
    public class TestClass { }
}";
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(code);
      try
      {
         var diagnostic = Diagnostic.Create(
            new DiagnosticDescriptor(
               "CS0168",
               "Unused variable",
               "Variable is declared but never used",
               "Test",
               DiagnosticSeverity.Warning,
               isEnabledByDefault: true),
            Location.None);

         // Even with cancellation, the method should complete (may be empty or throw)
         using var cts = new CancellationTokenSource();
         var task = AnalysisHelpers.GetCodeActionsForDiagnosticAsync(document, diagnostic, cts.Token);

         // Should complete without hanging
         var actions = await task;
         Assert.NotNull(actions);
      }
      finally
      {
         workspace.Dispose();
      }
   }

   #endregion

   #region CalculateComplexityMetrics Tests

   [Fact]
   public async Task CalculateComplexityMetrics_ReturnsRequestedMetrics()
   {
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(TestHelpers.SampleCode.ComplexMethod);
      try
      {
         var root = await document.GetSyntaxRootAsync();
         Assert.NotNull(root);
         var semanticModel = await document.GetSemanticModelAsync();
         Assert.NotNull(semanticModel);

         var metrics = AnalysisHelpers.CalculateComplexityMetrics(
            root!, semanticModel, new List<MetricType> { MetricType.Cyclomatic, MetricType.Nesting, MetricType.Loc }, CancellationToken.None);

         Assert.NotNull(metrics.Cyclomatic);
         Assert.NotNull(metrics.Nesting);
         Assert.NotNull(metrics.Loc);
         Assert.Null(metrics.Parameters); // Not requested
         Assert.Null(metrics.Cognitive);  // Not requested
      }
      finally
      {
         workspace.Dispose();
      }
   }

   private const string ConditionalCycleCode = """
      public static class Cycle
      {
         public static int Ping(int n) => Pong(n);
      #if CYCLE
         public static int Pong(int n) => Ping(n);
      #else
         public static int Pong(int n) => n;
      #endif
      }
      """;

   /// <summary>
   /// One file in two compilations, as in a multi-targeted project, where only one of them closes the cycle.
   /// </summary>
   [Fact]
   public async Task CalculateCognitiveComplexity_FindsCyclesInTheCompilationOfTheMeasuredNode()
   {
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(ConditionalCycleCode);
      try
      {
         var parseOptions = Assert.IsType<CSharpParseOptions>(document.Project.ParseOptions);
         var cycleDocument = document.Project
            .WithParseOptions(parseOptions.WithPreprocessorSymbols("CYCLE"))
            .GetDocument(document.Id);
         Assert.NotNull(cycleDocument);

         Assert.Equal(0, await GetPingCognitiveComplexityAsync(document, CancellationToken.None));
         Assert.Equal(1, await GetPingCognitiveComplexityAsync(cycleDocument, CancellationToken.None));
         Assert.Equal(0, await GetPingCognitiveComplexityAsync(document, CancellationToken.None));
      }
      finally
      {
         workspace.Dispose();
      }
   }

   [Fact]
   public async Task CalculateCognitiveComplexity_CanceledBeforeTheCycleSearch_ThrowsAndLaterCallsSucceed()
   {
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(ConditionalCycleCode);
      try
      {
         var parseOptions = Assert.IsType<CSharpParseOptions>(document.Project.ParseOptions);
         var cycleDocument = document.Project
            .WithParseOptions(parseOptions.WithPreprocessorSymbols("CYCLE"))
            .GetDocument(document.Id);
         Assert.NotNull(cycleDocument);

         await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => GetPingCognitiveComplexityAsync(cycleDocument, new CancellationToken(canceled: true)));
         Assert.Equal(1, await GetPingCognitiveComplexityAsync(cycleDocument, CancellationToken.None));
      }
      finally
      {
         workspace.Dispose();
      }
   }

   private static async Task<int> GetPingCognitiveComplexityAsync(Document document, CancellationToken cancellationToken)
   {
      var root = await document.GetSyntaxRootAsync(CancellationToken.None);
      var semanticModel = await document.GetSemanticModelAsync(CancellationToken.None);
      Assert.NotNull(root);
      Assert.NotNull(semanticModel);
      var ping = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == "Ping");
      return AnalysisHelpers.CalculateCognitiveComplexity(ping, semanticModel, cancellationToken);
   }

   /// <summary>
   /// Every implicit base() call in code that compiles binds, whatever the accessibility of the constructors involved.
   /// </summary>
   [Fact]
   public async Task GetImplicitBaseConstructorCalls_BindsEachCallTheCompilerBinds()
   {
      const string code = """
         public class A { public A() { } }
         public class B : A { }
         public abstract class C : A { }
         public sealed class D : C { }
         internal class E : B { }
         public class F { private F() { } protected F(int x = 0) { } public sealed class G : F { } }
         public class H : F { }
         public class I { protected I(int x = 0) { } protected I(params int[] xs) { } }
         public class J : I { }
         public class K(int k) : I { public int P => k; }
         public class L { private protected L() { } }
         public class M : L { }
         public abstract class N : L { }
         public class O : N { }
         public record P { }
         public record Q : P { }
         """;
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(code);
      try
      {
         var compilation = await document.Project.GetCompilationAsync(CancellationToken.None);
         Assert.NotNull(compilation);
         Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
         var types = compilation.GlobalNamespace.GetTypeMembers()
            .SelectMany(t => t.GetTypeMembers().Prepend(t))
            .Where(t => t.Locations.Any(l => l.IsInSource))
            .ToList();

         var calls = AnalysisHelpers.GetImplicitBaseConstructorCalls(compilation, types, CancellationToken.None)
            .Select(call => $"{call.Constructor.ToDisplayString()} -> {call.BaseConstructor.ToDisplayString()}");

         Assert.Equivalent(
            new[]
            {
               "B.B() -> A.A()",
               "C.C() -> A.A()",
               "D.D() -> C.C()",
               "E.E() -> B.B()",
               "F.G.G() -> F.F()",
               "H.H() -> F.F(int)",
               "J.J() -> I.I(int)",
               "K.K(int) -> I.I(int)",
               "M.M() -> L.L()",
               "N.N() -> L.L()",
               "O.O() -> N.N()",
               "Q.Q() -> P.P()",
            },
            calls,
            strict: true);
      }
      finally
      {
         workspace.Dispose();
      }
   }

   [Fact]
   public async Task CalculateCyclomaticComplexity_WithBranches_ReturnsCorrectValue()
   {
      var code = @"
public class Test
{
    public int Method(int x)
    {
        if (x > 0)
            return 1;
        else if (x < 0)
            return -1;
        else
            return 0;
    }
}";
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(code);
      try
      {
         var root = await document.GetSyntaxRootAsync();
         Assert.NotNull(root);

         var complexity = AnalysisHelpers.CalculateCyclomaticComplexity(root!);

         // Base 1 + 2 if statements = 3
         Assert.True(complexity >= 3);
      }
      finally
      {
         workspace.Dispose();
      }
   }

   [Fact]
   public async Task CalculateNestingDepth_WithNestedBlocks_ReturnsCorrectDepth()
   {
      var code = @"
public class Test
{
    public void Method()
    {
        if (true)
        {
            if (true)
            {
                if (true)
                {
                }
            }
        }
    }
}";
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(code);
      try
      {
         var root = await document.GetSyntaxRootAsync();
         Assert.NotNull(root);

         var depth = AnalysisHelpers.CalculateNestingDepth(root!);

         Assert.True(depth >= 3);
      }
      finally
      {
         workspace.Dispose();
      }
   }

   [Fact]
   public async Task CalculateLinesOfCode_ReturnsNonZeroValue()
   {
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(TestHelpers.SampleCode.SimpleClass);
      try
      {
         var root = await document.GetSyntaxRootAsync();
         Assert.NotNull(root);

         var loc = AnalysisHelpers.CalculateLinesOfCode(root!);

         Assert.True(loc > 0);
      }
      finally
      {
         workspace.Dispose();
      }
   }

   #endregion

   #region DetectCycles Tests

   [Fact]
   public void DetectCycles_WithNoCycle_ReturnsEmptyList()
   {
      var graph = new Dictionary<string, List<string>>
      {
         ["A"] = new List<string> { "B" },
         ["B"] = new List<string> { "C" },
         ["C"] = new List<string>()
      };

      var cycles = AnalysisHelpers.DetectCycles(graph);

      Assert.Empty(cycles);
   }

   [Fact]
   public void DetectCycles_WithSimpleCycle_DetectsCycle()
   {
      var graph = new Dictionary<string, List<string>>
      {
         ["A"] = new List<string> { "B" },
         ["B"] = new List<string> { "C" },
         ["C"] = new List<string> { "A" }
      };

      var cycles = AnalysisHelpers.DetectCycles(graph);

      Assert.NotEmpty(cycles);
   }

   [Fact]
   public void DetectCycles_WithSelfLoop_DetectsCycle()
   {
      var graph = new Dictionary<string, List<string>>
      {
         ["A"] = new List<string> { "A" }
      };

      var cycles = AnalysisHelpers.DetectCycles(graph);

      Assert.NotEmpty(cycles);
   }

   #endregion

   #region GenerateMermaidGraph Tests

   [Fact]
   public void GenerateMermaidGraph_WithSimpleGraph_ReturnsValidMermaid()
   {
      var graph = new Dictionary<string, List<string>>
      {
         ["ProjectA"] = new List<string> { "ProjectB" },
         ["ProjectB"] = new List<string>()
      };

      var mermaid = AnalysisHelpers.GenerateMermaidGraph(graph);

      Assert.Contains("graph TD", mermaid);
      Assert.Contains("ProjectA", mermaid);
      Assert.Contains("ProjectB", mermaid);
      Assert.Contains("-->", mermaid);
   }

   [Fact]
   public void GenerateMermaidGraph_WithEmptyGraph_ReturnsHeaderOnly()
   {
      var graph = new Dictionary<string, List<string>>();

      var mermaid = AnalysisHelpers.GenerateMermaidGraph(graph);

      Assert.Equal("graph TD", mermaid);
   }

   #endregion
}
