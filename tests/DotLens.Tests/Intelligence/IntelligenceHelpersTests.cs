using DotLens.Tools.Intelligence;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DotLens.Tests.Intelligence;

public class IntelligenceHelpersTests
{
   #region IsBaseType Tests

   [Fact]
   public async Task IsBaseType_WithDirectBase_ReturnsTrue()
   {
      var code = @"
public class Base { }
public class Derived : Base { }
";
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(code);
      try
      {
         var semanticModel = await document.GetSemanticModelAsync();
         var root = await document.GetSyntaxRootAsync();
         Assert.NotNull(semanticModel);
         Assert.NotNull(root);

         var compilation = semanticModel!.Compilation;
         var baseType = compilation.GetTypeByMetadataName("Base");
         var derivedType = compilation.GetTypeByMetadataName("Derived");
         Assert.NotNull(baseType);
         Assert.NotNull(derivedType);

         var result = IntelligenceHelpers.IsBaseType(derivedType!, baseType!);
         Assert.True(result);
      }
      finally
      {
         workspace.Dispose();
      }
   }

   [Fact]
   public async Task IsBaseType_WithNoInheritance_ReturnsFalse()
   {
      var code = @"
public class TypeA { }
public class TypeB { }
";
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(code);
      try
      {
         var semanticModel = await document.GetSemanticModelAsync();
         Assert.NotNull(semanticModel);

         var compilation = semanticModel!.Compilation;
         var typeA = compilation.GetTypeByMetadataName("TypeA");
         var typeB = compilation.GetTypeByMetadataName("TypeB");
         Assert.NotNull(typeA);
         Assert.NotNull(typeB);

         var result = IntelligenceHelpers.IsBaseType(typeA!, typeB!);
         Assert.False(result);
      }
      finally
      {
         workspace.Dispose();
      }
   }

   [Fact]
   public async Task IsBaseType_WithMultiLevelInheritance_ReturnsTrue()
   {
      var code = @"
public class GrandBase { }
public class Base : GrandBase { }
public class Derived : Base { }
";
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(code);
      try
      {
         var semanticModel = await document.GetSemanticModelAsync();
         Assert.NotNull(semanticModel);

         var compilation = semanticModel!.Compilation;
         var grandBase = compilation.GetTypeByMetadataName("GrandBase");
         var derived = compilation.GetTypeByMetadataName("Derived");
         Assert.NotNull(grandBase);
         Assert.NotNull(derived);

         var result = IntelligenceHelpers.IsBaseType(derived!, grandBase!);
         Assert.True(result);
      }
      finally
      {
         workspace.Dispose();
      }
   }

   #endregion

   #region GetConversionKind Tests

   [Fact]
   public async Task GetConversionKind_WithIdentityConversion_ReturnsIdentity()
   {
      var code = @"
public class Test
{
    public void Method()
    {
        int x = 1;
        int y = x;
    }
}";
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(code);
      try
      {
         var semanticModel = await document.GetSemanticModelAsync();
         Assert.NotNull(semanticModel);

         var compilation = semanticModel!.Compilation;
         var intType = compilation.GetSpecialType(SpecialType.System_Int32);

         var conversion = compilation.ClassifyConversion(intType, intType);
         var kind = IntelligenceHelpers.GetConversionKind(conversion);

         Assert.Equal("Identity", kind);
      }
      finally
      {
         workspace.Dispose();
      }
   }

   [Fact]
   public async Task GetConversionKind_WithImplicitNumeric_ReturnsImplicitNumeric()
   {
      var code = "public class Test { }";
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(code);
      try
      {
         var semanticModel = await document.GetSemanticModelAsync();
         Assert.NotNull(semanticModel);

         var compilation = semanticModel!.Compilation;
         var intType = compilation.GetSpecialType(SpecialType.System_Int32);
         var longType = compilation.GetSpecialType(SpecialType.System_Int64);

         var conversion = compilation.ClassifyConversion(intType, longType);
         var kind = IntelligenceHelpers.GetConversionKind(conversion);

         Assert.Equal("ImplicitNumeric", kind);
      }
      finally
      {
         workspace.Dispose();
      }
   }

   [Fact]
   public async Task GetConversionKind_WithBoxing_ReturnsBoxing()
   {
      var code = "public class Test { }";
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(code);
      try
      {
         var semanticModel = await document.GetSemanticModelAsync();
         Assert.NotNull(semanticModel);

         var compilation = semanticModel!.Compilation;
         var intType = compilation.GetSpecialType(SpecialType.System_Int32);
         var objectType = compilation.GetSpecialType(SpecialType.System_Object);

         var conversion = compilation.ClassifyConversion(intType, objectType);
         var kind = IntelligenceHelpers.GetConversionKind(conversion);

         Assert.Equal("Boxing", kind);
      }
      finally
      {
         workspace.Dispose();
      }
   }

   #endregion

   #region AnalyzeImpact Tests

   [Theory]
   [InlineData(ChangeType.Rename, 0, true)]
   [InlineData(ChangeType.Rename, 10, true)]
   [InlineData(ChangeType.ChangeType, 0, true)]
   [InlineData(ChangeType.ChangeType, 5, false)]
   [InlineData(ChangeType.AddParameter, 0, true)]
   [InlineData(ChangeType.AddParameter, 3, false)]
   [InlineData(ChangeType.RemoveParameter, 0, true)]
   [InlineData(ChangeType.RemoveParameter, 2, false)]
   [InlineData(ChangeType.ChangeAccessibility, 0, true)]
   [InlineData(ChangeType.Delete, 0, true)]
   [InlineData(ChangeType.Delete, 1, false)]
   public async Task AnalyzeImpact_ReturnsCorrectSafety(ChangeType changeType, int referenceCount, bool expectedSafe)
   {
      var code = @"
public class Test
{
    public void Method() { }
}";
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(code);
      try
      {
         var semanticModel = await document.GetSemanticModelAsync();
         Assert.NotNull(semanticModel);

         var compilation = semanticModel!.Compilation;
         var testType = compilation.GetTypeByMetadataName("Test");
         Assert.NotNull(testType);

         var method = testType!.GetMembers("Method").FirstOrDefault();
         Assert.NotNull(method);

         var (isSafe, _, _) = IntelligenceHelpers.AnalyzeImpact(method!, changeType, null, referenceCount);
         Assert.Equal(expectedSafe, isSafe);
      }
      finally
      {
         workspace.Dispose();
      }
   }

   [Fact]
   public async Task AnalyzeImpact_WithBreakingChange_ReturnsSuggestions()
   {
      var code = @"
public class Test
{
    public void Method() { }
}";
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(code);
      try
      {
         var semanticModel = await document.GetSemanticModelAsync();
         Assert.NotNull(semanticModel);

         var compilation = semanticModel!.Compilation;
         var testType = compilation.GetTypeByMetadataName("Test");
         var method = testType!.GetMembers("Method").FirstOrDefault();

         var (_, breakingReason, suggestions) = IntelligenceHelpers.AnalyzeImpact(method!, ChangeType.AddParameter, null, 5);

         Assert.NotNull(breakingReason);
         Assert.NotEmpty(suggestions);
      }
      finally
      {
         workspace.Dispose();
      }
   }

   #endregion

   #region GetOutgoingCallsForMethodAsync Tests

   [Fact]
   public async Task GetOutgoingCallsForMethodAsync_WithMethodCalls_ReturnsCalls()
   {
      var code = @"
using System;

public class Test
{
    public void Caller()
    {
        Console.WriteLine(""Hello"");
        Helper();
    }

    private void Helper() { }
}";
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(code);
      try
      {
         var semanticModel = await document.GetSemanticModelAsync();
         Assert.NotNull(semanticModel);

         var compilation = semanticModel!.Compilation;
         var testType = compilation.GetTypeByMetadataName("Test");
         Assert.NotNull(testType);

         var callerMethod = testType!.GetMembers("Caller").OfType<IMethodSymbol>().FirstOrDefault();
         Assert.NotNull(callerMethod);

         var calls = await IntelligenceHelpers.GetOutgoingCallsForMethodAsync(
            callerMethod!, workspace.CurrentSolution, 50, CancellationToken.None);

         Assert.NotEmpty(calls);
      }
      finally
      {
         workspace.Dispose();
      }
   }

   [Fact]
   public async Task GetOutgoingCallsForMethodAsync_WithNoSyntaxReference_ReturnsEmpty()
   {
      var code = "public class Test { }";
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(code);
      try
      {
         var semanticModel = await document.GetSemanticModelAsync();
         Assert.NotNull(semanticModel);

         var compilation = semanticModel!.Compilation;
         // Get a method without syntax reference (like from metadata)
         var objectType = compilation.GetSpecialType(SpecialType.System_Object);
         var toStringMethod = objectType.GetMembers("ToString").OfType<IMethodSymbol>().FirstOrDefault();
         Assert.NotNull(toStringMethod);

         var calls = await IntelligenceHelpers.GetOutgoingCallsForMethodAsync(
            toStringMethod!, workspace.CurrentSolution, 50, CancellationToken.None);

         Assert.Empty(calls);
      }
      finally
      {
         workspace.Dispose();
      }
   }

   [Fact]
   public async Task GetOutgoingCallsForMethodAsync_RespectsMaxCalls()
   {
      var code = @"
using System;

public class Test
{
    public void ManyCallsMethod()
    {
        Console.WriteLine(""1"");
        Console.WriteLine(""2"");
        Console.WriteLine(""3"");
        Console.WriteLine(""4"");
        Console.WriteLine(""5"");
    }
}";
      var (workspace, document) = TestHelpers.CreateWorkspaceWithCode(code);
      try
      {
         var semanticModel = await document.GetSemanticModelAsync();
         Assert.NotNull(semanticModel);

         var compilation = semanticModel!.Compilation;
         var testType = compilation.GetTypeByMetadataName("Test");
         var method = testType!.GetMembers("ManyCallsMethod").OfType<IMethodSymbol>().FirstOrDefault();
         Assert.NotNull(method);

         var calls = await IntelligenceHelpers.GetOutgoingCallsForMethodAsync(
            method!, workspace.CurrentSolution, 2, CancellationToken.None);

         // Should respect max (but deduplication may reduce it further)
         Assert.True(calls.Count <= 2);
      }
      finally
      {
         workspace.Dispose();
      }
   }

   #endregion
}
