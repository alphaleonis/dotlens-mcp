using DotLens.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

   #region GetTypeMembers Tests

   [Fact]
   public async Task GetTypeMembers_IncludeInherited_WithBaseClass_ReturnsInheritedMembers()
   {
      var code = @"
public class Base
{
    public void BaseMethod() { }
    public int BaseProperty { get; set; }
}

public class Derived : Base
{
    public void DerivedMethod() { }
}";
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(code);
      try
      {
         var semanticModel = await document.GetSemanticModelAsync();
         Assert.NotNull(semanticModel);

         var compilation = semanticModel!.Compilation;
         var derivedType = compilation.GetTypeByMetadataName("Derived") as INamedTypeSymbol;
         Assert.NotNull(derivedType);

         var inheritedMembers = RoslynHelpers.GetTypeMembers(derivedType!, includeInherited: true).ToList();

         Assert.NotEmpty(inheritedMembers);
         Assert.Contains(inheritedMembers, m => m.Name == "BaseMethod");
         Assert.Contains(inheritedMembers, m => m.Name == "BaseProperty");
      }
      finally
      {
         workspace.Dispose();
      }
   }

   [Fact]
   public async Task GetTypeMembers_IncludeInherited_ExcludesPrivateMembers()
   {
      var code = @"
public class Base
{
    private void PrivateMethod() { }
    public void PublicMethod() { }
}

public class Derived : Base { }";
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(code);
      try
      {
         var semanticModel = await document.GetSemanticModelAsync();
         Assert.NotNull(semanticModel);

         var compilation = semanticModel!.Compilation;
         var derivedType = compilation.GetTypeByMetadataName("Derived") as INamedTypeSymbol;
         Assert.NotNull(derivedType);

         var inheritedMembers = RoslynHelpers.GetTypeMembers(derivedType!, includeInherited: true).ToList();

         Assert.DoesNotContain(inheritedMembers, m => m.Name == "PrivateMethod");
         Assert.Contains(inheritedMembers, m => m.Name == "PublicMethod");
      }
      finally
      {
         workspace.Dispose();
      }
   }

   [Fact]
   public async Task GetTypeMembers_IncludeInherited_IncludesStaticMembers()
   {
      var code = @"
public class Base
{
    public static void StaticMethod() { }
    public void InstanceMethod() { }
}

public class Derived : Base { }";
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(code);
      try
      {
         var semanticModel = await document.GetSemanticModelAsync();
         Assert.NotNull(semanticModel);

         var compilation = semanticModel!.Compilation;
         var derivedType = compilation.GetTypeByMetadataName("Derived") as INamedTypeSymbol;
         Assert.NotNull(derivedType);

         var inheritedMembers = RoslynHelpers.GetTypeMembers(derivedType!, includeInherited: true).ToList();

         Assert.Equal(["StaticMethod", "InstanceMethod"], inheritedMembers.Select(m => m.Name));
      }
      finally
      {
         workspace.Dispose();
      }
   }

   [Fact]
   public async Task GetTypeMembers_IncludeInherited_WithNoBaseClass_ReturnsEmpty()
   {
      var code = @"public class Test { }";
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(code);
      try
      {
         var semanticModel = await document.GetSemanticModelAsync();
         Assert.NotNull(semanticModel);

         var compilation = semanticModel!.Compilation;
         var testType = compilation.GetTypeByMetadataName("Test") as INamedTypeSymbol;
         Assert.NotNull(testType);

         var inheritedMembers = RoslynHelpers.GetTypeMembers(testType!, includeInherited: true).ToList();

         // Only base is System.Object which is excluded, and the implicit constructor is left out
         Assert.Empty(inheritedMembers);
      }
      finally
      {
         workspace.Dispose();
      }
   }

   #endregion

   /// <summary>
   /// Types compiled into a referenced assembly, where metadata lists an interface of the base class on a derived type
   /// only when the derived type implements it again.
   /// </summary>
   [Theory]
   [InlineData("Reimplementing", "public class Reimplementing : Base, IShape")]
   [InlineData("Inheriting", "public class Inheriting : Base")]
   [InlineData("Square", "public sealed class Square : IShape")]
   public void GetDeclaration_CompiledType_ListsReimplementedInterfaces(string typeName, string expected)
   {
      var library = CSharpCompilation.Create(
         "CompiledShapes",
         [CSharpSyntaxTree.ParseText("""
            public interface IArea { double Area(); }
            public interface IShape : IArea { }
            public class Base : IShape { public double Area() => 0; }
            public class Reimplementing : Base, IShape { double IArea.Area() => 1; }
            public class Inheriting : Base { }
            public sealed class Square : IShape, IArea { public double Area() => 4; }
            """, cancellationToken: TestContext.Current.CancellationToken)],
         TestHelpers.CoreReferences,
         new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
      using var image = new MemoryStream();
      var emitted = library.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
      Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
      var consumer = CSharpCompilation.Create("Consumer", references: [.. TestHelpers.CoreReferences, MetadataReference.CreateFromImage(image.ToArray())]);
      var type = consumer.GetTypeByMetadataName(typeName) ?? throw new InvalidOperationException($"{typeName} is not in the reference");

      Assert.Equal(expected, RoslynHelpers.GetDeclaration(type));
   }
}
