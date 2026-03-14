using DotLens.Infrastructure;
using Xunit;

namespace DotLens.Tests.Infrastructure;

public class RoslynHelpersTests
{
   [Fact]
   public void CompactSourceOutput_WithNull_ReturnsNull()
   {
      var result = RoslynHelpers.CompactSourceOutput(null!);
      Assert.Null(result);
   }

   [Fact]
   public void CompactSourceOutput_WithEmpty_ReturnsEmpty()
   {
      var result = RoslynHelpers.CompactSourceOutput("");
      Assert.Equal("", result);
   }

   [Fact]
   public void CompactSourceOutput_WithNoIndentation_ReturnsUnchanged()
   {
      var source = "public void Foo()\n{\n    return;\n}";
      var result = RoslynHelpers.CompactSourceOutput(source);
      Assert.Equal(source, result);
   }

   [Fact]
   public void CompactSourceOutput_WithUniformIndentation_StripsIndent()
   {
      var source = "   public void Foo()\n   {\n      return;\n   }";
      var expected = "public void Foo()\n{\n   return;\n}";
      var result = RoslynHelpers.CompactSourceOutput(source);
      Assert.Equal(expected, result);
   }

   [Fact]
   public void CompactSourceOutput_PreservesRelativeIndentation()
   {
      var source = "      public void Foo()\n      {\n         if (true)\n         {\n            return;\n         }\n      }";
      var result = RoslynHelpers.CompactSourceOutput(source);

      // Should strip 6-space common indentation, preserving relative structure
      Assert.StartsWith("public void Foo()", result);
      Assert.Contains("   if (true)", result);
      Assert.Contains("      return;", result);
   }

   [Fact]
   public void CompactSourceOutput_HandlesEmptyLines()
   {
      var source = "   public void Foo()\n   {\n\n      return;\n   }";
      var result = RoslynHelpers.CompactSourceOutput(source);

      // Empty lines should not affect minimum indentation calculation
      Assert.StartsWith("public void Foo()", result);
   }

   [Fact]
   public void CompactSourceOutput_HandlesTabs()
   {
      // Tab counts as 3 spaces in the implementation
      var source = "\t\tpublic void Foo()\n\t\t{\n\t\t\treturn;\n\t\t}";
      var result = RoslynHelpers.CompactSourceOutput(source);

      // Should strip common tab indentation
      Assert.StartsWith("public void Foo()", result);
   }
}
