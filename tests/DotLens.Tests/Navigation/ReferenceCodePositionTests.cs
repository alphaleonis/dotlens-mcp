using DotLens.Tools.Navigation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DotLens.Tests.Navigation;

/// <summary>
/// Covers which reference positions find_references treats as code, where a reference to an operator, a conversion or a
/// constructor is a call.
/// </summary>
public class ReferenceCodePositionTests
{
   [Theory]
   [InlineData("var s = \"~M:N.C.#ctor\";", "#ctor", false)]
   [InlineData("var s = @\"~M:N.C.#ctor\";", "#ctor", false)]
   [InlineData("var s = \"\"\"~M:N.C.#ctor\"\"\";", "#ctor", false)]
   [InlineData("var s = \"\"\"\n   ~M:N.C.#ctor\n   \"\"\";", "#ctor", false)]
   [InlineData("var s = $\"~M:N.C.#ctor{1}\";", "#ctor", false)]
   [InlineData("var s = $\"\"\"~M:N.C.#ctor{1}\"\"\";", "#ctor", false)]
   [InlineData("var s = \"~M:N.C.#ctor\"u8;", "#ctor", false)]
   [InlineData("var s = \"\"\"~M:N.C.#ctor\"\"\"u8;", "#ctor", false)]
   [InlineData("// C.#ctor\nvar s = 1;", "#ctor", false)]
   [InlineData("/* C.#ctor */ var s = 1;", "#ctor", false)]
   [InlineData("var s = $\"{new C()}\";", "C()", true)]
   [InlineData("var s = new C();", "C()", true)]
   [InlineData("var s = a + b;", "+", true)]
   [InlineData("var s = (int)c;", "(int)", true)]
   [InlineData("C s = new();", "new", true)]
   public void IsInCode_PositionInStatement_ReturnsWhetherItIsOutsideStringsAndComments(string statement, string marker, bool expected)
   {
      var text = $"class D\n{{\n   void M()\n   {{\n      {statement}\n   }}\n}}\n";
      var root = CSharpSyntaxTree.ParseText(text, CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Parse), cancellationToken: TestContext.Current.CancellationToken)
         .GetRoot(TestContext.Current.CancellationToken);

      Assert.Equal(expected, FindReferencesTool.IsInCode(root, text.IndexOf(marker, StringComparison.Ordinal)));
   }

   [Fact]
   public void IsInCode_PositionInCref_ReturnsFalse()
   {
      const string text = "/// <see cref=\"C(int)\"/>\nclass C\n{\n   public C(int value) { }\n}\n";
      var root = CSharpSyntaxTree.ParseText(text, CSharpParseOptions.Default.WithDocumentationMode(DocumentationMode.Parse), cancellationToken: TestContext.Current.CancellationToken)
         .GetRoot(TestContext.Current.CancellationToken);

      Assert.False(FindReferencesTool.IsInCode(root, text.IndexOf("C(int)", StringComparison.Ordinal)));
   }

   [Fact]
   public void IsInCode_PositionOfBaseInitializer_ReturnsTrue()
   {
      const string text = "class D : B\n{\n   public D() : base(1) { }\n}\n";
      var root = CSharpSyntaxTree.ParseText(text, cancellationToken: TestContext.Current.CancellationToken).GetRoot(TestContext.Current.CancellationToken);

      Assert.True(FindReferencesTool.IsInCode(root, text.IndexOf("base", StringComparison.Ordinal)));
   }
}
