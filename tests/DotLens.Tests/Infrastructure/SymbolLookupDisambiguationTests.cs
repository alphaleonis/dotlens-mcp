using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tests.Helpers;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DotLens.Tests.Infrastructure;

[Collection("TestFixtures")]
public class SymbolLookupDisambiguationTests : TestFixtureTestBase
{
   public SymbolLookupDisambiguationTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private SymbolLookupService _service = null!;

   public override ValueTask InitializeAsync()
   {
      _service = new SymbolLookupService(Workspace);
      return ValueTask.CompletedTask;
   }

   // ===========================================
   // Disambiguation by parameter types
   // ===========================================

   [Fact]
   public async Task FindAsync_ParameterTypes_FiltersSingleStringParam()
   {
      var lookup = new LookupByName
      {
         Name = "SampleClass.Format",
         ParameterTypes = ["string"]
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("Format", results[0].Symbol.Name);
      var parameter = Assert.Single(results[0].Symbol.Parameters);
      Assert.Equal("String", parameter.Type.Name);
   }

   [Fact]
   public async Task FindAsync_ParameterTypes_FiltersSingleIntParam()
   {
      var lookup = new LookupByName
      {
         Name = "SampleClass.Format",
         ParameterTypes = ["int"]
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("Format", results[0].Symbol.Name);
      var parameter = Assert.Single(results[0].Symbol.Parameters);
      Assert.Equal("Int32", parameter.Type.Name);
   }

   [Fact]
   public async Task FindAsync_ParameterTypes_FiltersTwoParams()
   {
      var lookup = new LookupByName
      {
         Name = "SampleClass.Format",
         ParameterTypes = ["string", "string"]
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("Format", results[0].Symbol.Name);
      Assert.Equal(2, results[0].Symbol.Parameters.Length);
      Assert.Equal("String", results[0].Symbol.Parameters[0].Type.Name);
      Assert.Equal("String", results[0].Symbol.Parameters[1].Type.Name);
   }

   [Fact]
   public async Task FindAsync_ParameterTypes_NoMatchReturnsEmpty()
   {
      var lookup = new LookupByName
      {
         Name = "SampleClass.Format",
         ParameterTypes = ["bool"]
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Empty(results);
   }

   [Fact]
   public async Task FindAsync_ParameterTypes_WrongCountReturnsEmpty()
   {
      var lookup = new LookupByName
      {
         Name = "SampleClass.Format",
         ParameterTypes = ["string", "string", "string"]
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Empty(results);
   }

   [Fact]
   public async Task FindAsync_ParameterTypes_EmptyArrayMatchesParameterless()
   {
      // OverloadedService.Reset() has no params, Reset(int) has one
      var lookup = new LookupByName
      {
         Name = "OverloadedService.Reset",
         ParameterTypes = []
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("Reset", results[0].Symbol.Name);
      Assert.Empty(results[0].Symbol.Parameters);
   }

   // ===========================================
   // Disambiguation by return type
   // ===========================================

   [Fact]
   public async Task FindAsync_ReturnType_FiltersStringReturn()
   {
      var lookup = new LookupByName
      {
         Name = "OverloadedService.Convert",
         ReturnType = "string"
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("Convert", results[0].Symbol.Name);
      Assert.Equal("String", results[0].Symbol.ReturnType.Name);
      Assert.Equal("Int32", results[0].Symbol.Parameters[0].Type.Name);
   }

   [Fact]
   public async Task FindAsync_ReturnType_FiltersIntReturn()
   {
      var lookup = new LookupByName
      {
         Name = "OverloadedService.Convert",
         ReturnType = "int"
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("Convert", results[0].Symbol.Name);
      Assert.Equal("Int32", results[0].Symbol.ReturnType.Name);
      Assert.Equal("String", results[0].Symbol.Parameters[0].Type.Name);
   }

   [Fact]
   public async Task FindAsync_ReturnType_VoidMatchesVoidMethods()
   {
      var lookup = new LookupByName
      {
         Name = "OverloadedService.Reset",
         ReturnType = "void"
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("Reset", results[0].Symbol.Name);
      Assert.True(results[0].Symbol.ReturnsVoid);
      Assert.Empty(results[0].Symbol.Parameters);
   }

   // ===========================================
   // Combined parameter types + return type
   // ===========================================

   [Fact]
   public async Task FindAsync_BothFilters_NarrowsToExactOverload()
   {
      var lookup = new LookupByName
      {
         Name = "OverloadedService.Convert",
         ParameterTypes = ["int"],
         ReturnType = "string"
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("Int32", results[0].Symbol.Parameters[0].Type.Name);
      Assert.Equal("String", results[0].Symbol.ReturnType.Name);
   }

   // ===========================================
   // FindSingleAsync with disambiguation
   // ===========================================

   [Fact]
   public async Task FindSingleAsync_WithParameterTypes_ResolvesAmbiguity()
   {
      // Without disambiguation, this would throw AmbiguousSymbol
      var lookup = new LookupByName
      {
         Name = "SampleClass.Format",
         ParameterTypes = ["int"]
      };

      var result = await _service.FindSingleAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Equal("Format", result.Symbol.Name);
      Assert.Equal("Int32", result.Symbol.Parameters[0].Type.Name);
   }

   [Fact]
   public async Task FindSingleAsync_WithReturnType_ResolvesAmbiguity()
   {
      var lookup = new LookupByName
      {
         Name = "OverloadedService.Convert",
         ReturnType = "int"
      };

      var result = await _service.FindSingleAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Equal("Convert", result.Symbol.Name);
      Assert.Equal("Int32", result.Symbol.ReturnType.Name);
   }

   // ===========================================
   // Partial type name matching
   // ===========================================

   [Fact]
   public async Task FindAsync_ParameterTypes_MatchesBySimpleName()
   {
      // "String" (simple name) should match System.String
      var lookup = new LookupByName
      {
         Name = "SampleClass.Format",
         ParameterTypes = ["String"]
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      var parameter = Assert.Single(results[0].Symbol.Parameters);
      Assert.Equal("String", parameter.Type.Name);
   }

   [Fact]
   public async Task FindAsync_ParameterTypes_MatchesByFullyQualifiedName()
   {
      // "System.String" should match string parameters
      var lookup = new LookupByName
      {
         Name = "SampleClass.Format",
         ParameterTypes = ["System.String"]
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      var parameter = Assert.Single(results[0].Symbol.Parameters);
      Assert.Equal("String", parameter.Type.Name);
   }

   [Fact]
   public async Task FindAsync_ParameterTypes_MatchesNonGenericFullyQualifiedType()
   {
      // "System.Uri" should match the Describe(System.Uri) overload
      var lookup = new LookupByName
      {
         Name = "OverloadedService.Describe",
         ParameterTypes = ["System.Uri"]
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("Describe", results[0].Symbol.Name);
      Assert.Equal("Uri", results[0].Symbol.Parameters[0].Type.Name);
   }

   [Fact]
   public async Task FindAsync_ReturnType_MatchesBySimpleName()
   {
      var lookup = new LookupByName
      {
         Name = "OverloadedService.Convert",
         ReturnType = "Int32"
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("Int32", results[0].Symbol.ReturnType.Name);
   }

   // ===========================================
   // Generic type matching
   // ===========================================

   [Fact]
   public async Task FindAsync_ParameterTypes_MatchesGenericWithKeyword()
   {
      // "List<string>" uses C# keyword - should match List<string>
      var lookup = new LookupByName
      {
         Name = "OverloadedService.Count",
         ParameterTypes = ["List<string>"]
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("Count", results[0].Symbol.Name);
      var paramType = Assert.IsAssignableFrom<INamedTypeSymbol>(results[0].Symbol.Parameters[0].Type);
      Assert.Equal("String", paramType.TypeArguments[0].Name);
   }

   [Fact]
   public async Task FindAsync_ParameterTypes_MatchesGenericWithIntKeyword()
   {
      var lookup = new LookupByName
      {
         Name = "OverloadedService.Count",
         ParameterTypes = ["List<int>"]
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("Count", results[0].Symbol.Name);
      var paramType = Assert.IsAssignableFrom<INamedTypeSymbol>(results[0].Symbol.Parameters[0].Type);
      Assert.Equal("Int32", paramType.TypeArguments[0].Name);
   }

   [Fact]
   public async Task FindAsync_ParameterTypes_MatchesGenericWithFullyQualifiedName()
   {
      // "System.Collections.Generic.List<string>" should also match
      var lookup = new LookupByName
      {
         Name = "OverloadedService.Count",
         ParameterTypes = ["System.Collections.Generic.List<string>"]
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("Count", results[0].Symbol.Name);
      var paramType = Assert.IsAssignableFrom<INamedTypeSymbol>(results[0].Symbol.Parameters[0].Type);
      Assert.Equal("String", paramType.TypeArguments[0].Name);
   }

   // ===========================================
   // Edge cases
   // ===========================================

   [Fact]
   public async Task FindAsync_ParameterTypes_OnNonMethodSymbol_ReturnsEmpty()
   {
      // SampleClass is a type, not a method - ParameterTypes should filter it out
      var lookup = new LookupByName
      {
         Name = "SampleClass",
         ParameterTypes = ["int"]
      };

      var results = await _service.FindAsync<ISymbol>(lookup, cancellationToken: CT);

      Assert.Empty(results);
   }

   [Fact]
   public async Task FindAsync_WithoutDisambiguation_StillReturnsAllOverloads()
   {
      // Verify existing behavior is preserved when no disambiguation is specified
      var lookup = new LookupByName { Name = "SampleClass.Format" };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Equal(3, results.Count);
   }

   // ===========================================
   // Parameter modifier disambiguation
   // ===========================================

   [Fact]
   public async Task FindAsync_ParameterTypes_RefModifierMatchesRefOverload()
   {
      // "ref int" should match only Process(ref int), not Process(int)
      var lookup = new LookupByName
      {
         Name = "ModifierOverloads.Process",
         ParameterTypes = ["ref int"]
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("Process", results[0].Symbol.Name);
      Assert.Equal(RefKind.Ref, results[0].Symbol.Parameters[0].RefKind);
   }

   [Fact]
   public async Task FindAsync_ParameterTypes_NoModifierMatchesBothRefAndByValue()
   {
      // "int" without modifier matches both Process(int) and Process(ref int)
      var lookup = new LookupByName
      {
         Name = "ModifierOverloads.Process",
         ParameterTypes = ["int"]
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Equal(2, results.Count);
      var refKinds = results.Select(r => r.Symbol.Parameters[0].RefKind).ToList();
      Assert.Equivalent(new[] { RefKind.None, RefKind.Ref }, refKinds);
   }

   [Fact]
   public async Task FindAsync_ParameterTypes_OutModifierMatches()
   {
      var lookup = new LookupByName
      {
         Name = "ModifierOverloads.Initialize",
         ParameterTypes = ["out int"]
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("Initialize", results[0].Symbol.Name);
      Assert.Equal(RefKind.Out, results[0].Symbol.Parameters[0].RefKind);
   }

   [Fact]
   public async Task FindAsync_ParameterTypes_InModifierMatches()
   {
      var lookup = new LookupByName
      {
         Name = "ModifierOverloads.Inspect",
         ParameterTypes = ["in int"]
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("Inspect", results[0].Symbol.Name);
      Assert.Equal(RefKind.In, results[0].Symbol.Parameters[0].RefKind);
   }

   [Fact]
   public async Task FindAsync_ParameterTypes_ParamsModifierMatches()
   {
      var lookup = new LookupByName
      {
         Name = "ModifierOverloads.Log",
         ParameterTypes = ["params string[]"]
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("Log", results[0].Symbol.Name);
      Assert.True(results[0].Symbol.Parameters[0].IsParams);
      Assert.Equal("string[]", results[0].Symbol.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
   }

   [Fact]
   public async Task FindAsync_ParameterTypes_RefReadonlyModifierMatches()
   {
      var lookup = new LookupByName
      {
         Name = "ModifierOverloads.ReadOnly",
         ParameterTypes = ["ref readonly int"]
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("ReadOnly", results[0].Symbol.Name);
      Assert.Equal(RefKind.RefReadOnlyParameter, results[0].Symbol.Parameters[0].RefKind);
   }

   [Fact]
   public async Task FindAsync_ParameterTypes_WrongModifierReturnsEmpty()
   {
      // "out int" should not match Process(int) or Process(ref int)
      var lookup = new LookupByName
      {
         Name = "ModifierOverloads.Process",
         ParameterTypes = ["out int"]
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Empty(results);
   }

   // ===========================================
   // Type-parameter counts and same full names
   // ===========================================

   /// <summary>
   /// Each name matches non-generic and generic symbols that differ in more than their type-parameter counts.
   /// </summary>
   [Theory]
   [InlineData("IRepository", new[] { "CoreTestProject.Types.IRepository<T>", "InheritanceTestProject.IRepository" })]
   [InlineData("InMemoryRepository", new[] { "CoreTestProject.Types.InMemoryRepository<T>", "InheritanceTestProject.InMemoryRepository" })]
   [InlineData("Convert", new[] { "CoreTestProject.Types.OverloadedService.Convert(int)", "CoreTestProject.Types.OverloadedService.Convert(string)", "CoreTestProject.Types.GenericMethods.Convert<TInput, TOutput>(TInput, System.Func<TInput, TOutput>)" })]
   public async Task FindAsync_NameWithoutTypeParameters_KeepsGenericSymbolsThatDifferOtherwise(string name, string[] expected)
   {
      var results = await _service.FindAsync<ISymbol>(new LookupByName { Name = name }, cancellationToken: CT);

      Assert.Equivalent(expected, results.Select(r => r.Symbol.ToDisplayString()), strict: true);
   }

   /// <summary>
   /// CoreTestProject and ConflictingTypesProject each declare CoreTestProject.Types.AmbiguousTestClass.
   /// </summary>
   [Theory]
   [InlineData("AmbiguousTestClass")]
   [InlineData("CoreTestProject.Types.AmbiguousTestClass")]
   public async Task FindAsync_SameFullNameInTwoProjects_FindsBoth(string name)
   {
      var lookup = new LookupByName { Name = name };

      var results = await _service.FindAsync<ISymbol>(lookup, cancellationToken: CT);
      var ex = await Assert.ThrowsAsync<ToolException>(() => _service.FindSingleAsync<ISymbol>(lookup, cancellationToken: CT));

      Assert.Equivalent(
         new[] { ("CoreTestProject", "CoreTestProject.Types.AmbiguousTestClass"), ("ConflictingTypesProject", "CoreTestProject.Types.AmbiguousTestClass") },
         results.Select(r => (r.Symbol.ContainingAssembly.Name, r.Symbol.ToDisplayString())),
         strict: true);
      Assert.Equal(ErrorCodes.AmbiguousSymbol, ex.Code);
      Assert.Equal(
         "Multiple symbols found (2): CoreTestProject.Types.AmbiguousTestClass at CoreTestProject/Types/Classes.cs:96:14; CoreTestProject.Types.AmbiguousTestClass at ConflictingTypesProject/ConflictingTypes.cs:14:14",
         ex.Message);
   }

   [Theory]
   [InlineData("IRepository", new[] { "InheritanceTestProject.IRepository", "CoreTestProject.Types.IRepository<T>" })]
   [InlineData("InMemoryRepository", new[] { "InheritanceTestProject.InMemoryRepository", "CoreTestProject.Types.InMemoryRepository<T>" })]
   [InlineData("Convert", new[] { "CoreTestProject.Types.GenericMethods.Convert<TInput, TOutput>(TInput, System.Func<TInput, TOutput>)", "CoreTestProject.Types.OverloadedService.Convert(int)", "CoreTestProject.Types.OverloadedService.Convert(string)" })]
   [InlineData("AmbiguousTestClass", new[] { "CoreTestProject.Types.AmbiguousTestClass at CoreTestProject/Types/Classes.cs:96:14", "CoreTestProject.Types.AmbiguousTestClass at ConflictingTypesProject/ConflictingTypes.cs:14:14" })]
   [InlineData("CoreTestProject.Types.AmbiguousTestClass", new[] { "CoreTestProject.Types.AmbiguousTestClass at CoreTestProject/Types/Classes.cs:96:14", "CoreTestProject.Types.AmbiguousTestClass at ConflictingTypesProject/ConflictingTypes.cs:14:14" })]
   public async Task FindSingleAsync_AmbiguousCandidates_EachListedCandidateFindsItself(string name, string[] expected)
   {
      await AmbiguityRoundTrip.AssertEachListedCandidateFindsItselfAsync(_service, new LookupByName { Name = name }, expected, CT);
   }

   /// <summary>
   /// CoreTestProject and ConflictingTypesProject each declare the namespace CoreTestProject.Types.
   /// </summary>
   [Theory]
   [InlineData("CoreTestProject.Types", "CoreTestProject.Types")]
   [InlineData("Types", "CoreTestProject.Types")]
   [InlineData("CoreTestProject", "CoreTestProject")]
   public async Task FindSingleAsync_NamespaceInTwoProjects_FindsItOnce(string name, string expected)
   {
      var result = await _service.FindSingleAsync<INamespaceSymbol>(new LookupByName { Name = name }, cancellationToken: CT);

      Assert.Equal(expected, result.Symbol.ToDisplayString());
   }
}
