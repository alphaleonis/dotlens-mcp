using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DotLens.Tests.Infrastructure;

[Collection("TestFixtures")]
public class SymbolLookupServiceTests : TestFixtureTestBase
{
   public SymbolLookupServiceTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private SymbolLookupService _service = null!;

   public override async ValueTask InitializeAsync()
   {
      _service = new SymbolLookupService(Workspace);
   }

   // ===========================================
   // Position-based lookup tests
   // ===========================================

   [Fact]
   public async Task FindAsync_ByPosition_FindsType()
   {
      // SampleClass is at line 12, column 14 in Classes.cs
      var lookup = new LookupByPosition
      {
         FilePath = GetCoreTestProjectPath("Types/Classes.cs"),
         Line = 12,
         Column = 14
      };

      var results = await _service.FindAsync<INamedTypeSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("SampleClass", results[0].Symbol.Name);
      Assert.IsType<SourceSymbolResult<INamedTypeSymbol>>(results[0]);
   }

   [Fact]
   public async Task FindAsync_ByPosition_FindsMethod()
   {
      // DoSomething method is at line 43, column 16 in Classes.cs
      var lookup = new LookupByPosition
      {
         FilePath = GetCoreTestProjectPath("Types/Classes.cs"),
         Line = 43,
         Column = 16
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("DoSomething", results[0].Symbol.Name);
   }

   [Fact]
   public async Task FindAsync_ByPosition_NoMatchReturnsEmptyList()
   {
      // Position on an empty line or non-symbol location
      var lookup = new LookupByPosition
      {
         FilePath = GetCoreTestProjectPath("Types/Classes.cs"),
         Line = 1,  // using statement line
         Column = 1
      };

      var results = await _service.FindAsync<IFieldSymbol>(lookup, cancellationToken: CT);

      Assert.Empty(results);
   }

   [Fact]
   public async Task FindAsync_ByPosition_WrongTypeReturnsEmptyList()
   {
      // SampleClass is a type, not a method
      var lookup = new LookupByPosition
      {
         FilePath = GetCoreTestProjectPath("Types/Classes.cs"),
         Line = 12,
         Column = 14
      };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Empty(results);
   }

   // ===========================================
   // Name-based lookup tests
   // ===========================================

   [Fact]
   public async Task FindAsync_ByName_FindsTypeBySimpleName()
   {
      var lookup = new LookupByName { Name = "SampleClass" };

      var results = await _service.FindAsync<INamedTypeSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("SampleClass", results[0].Symbol.Name);
      Assert.IsType<SourceSymbolResult<INamedTypeSymbol>>(results[0]);
   }

   [Fact]
   public async Task FindAsync_ByName_FindsMemberByPath()
   {
      var lookup = new LookupByName { Name = "SampleClass.Calculate" };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("Calculate", results[0].Symbol.Name);
   }

   [Fact]
   public async Task FindAsync_ByName_FindsOverloads()
   {
      // SampleClass has 3 Format overloads
      var lookup = new LookupByName { Name = "SampleClass.Format" };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Equal(3, results.Count);
      Assert.All(results, r => Assert.Equal("Format", r.Symbol.Name));
   }

   [Fact]
   public async Task FindAsync_ByName_TypeNotFound_ReturnsEmptyList()
   {
      var lookup = new LookupByName { Name = "NonExistentClass" };

      var results = await _service.FindAsync<INamedTypeSymbol>(lookup, cancellationToken: CT);

      Assert.Empty(results);
   }

   [Fact]
   public async Task FindAsync_ByName_MemberNotFound_ReturnsEmptyList()
   {
      var lookup = new LookupByName { Name = "SampleClass.NonExistentMethod" };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Empty(results);
   }

   // ===========================================
   // FindSingleAsync tests
   // ===========================================

   [Fact]
   public async Task FindSingleAsync_WithSingleMatch_ReturnsSymbol()
   {
      var lookup = new LookupByName { Name = "SampleClass" };

      var result = await _service.FindSingleAsync<INamedTypeSymbol>(lookup, cancellationToken: CT);

      Assert.Equal("SampleClass", result.Symbol.Name);
   }

   [Fact]
   public async Task FindSingleAsync_NoMatch_ThrowsToolException()
   {
      var lookup = new LookupByName { Name = "NonExistentClass" };

      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _service.FindSingleAsync<INamedTypeSymbol>(lookup, cancellationToken: CT));

      Assert.Equal(ErrorCodes.SymbolNotFound, ex.Code);
   }

   [Fact]
   public async Task FindSingleAsync_MultipleMatches_ThrowsToolException()
   {
      // SampleClass has 3 Format overloads
      var lookup = new LookupByName { Name = "SampleClass.Format" };

      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _service.FindSingleAsync<IMethodSymbol>(lookup, cancellationToken: CT));

      Assert.Equal(ErrorCodes.AmbiguousSymbol, ex.Code);
   }

   // ===========================================
   // Source vs Metadata result tests
   // ===========================================

   [Fact]
   public async Task FindAsync_SourceSymbol_ReturnsSourceSymbolResult()
   {
      var lookup = new LookupByName { Name = "SampleClass" };

      var results = await _service.FindAsync<INamedTypeSymbol>(lookup, cancellationToken: CT);

      var result = Assert.Single(results);
      var sourceResult = Assert.IsType<SourceSymbolResult<INamedTypeSymbol>>(result);
      Assert.NotNull(sourceResult.Document);
      Assert.Contains("Classes.cs", sourceResult.Document.FilePath);
   }

   // ===========================================
   // Project filter tests
   // ===========================================

   [Fact]
   public async Task FindAsync_WithProjectFilter_LimitsSearch()
   {
      // Search only in CoreTestProject
      var lookup = new LookupByName
      {
         Name = "SampleClass",
         Project = "CoreTestProject"
      };

      var results = await _service.FindAsync<INamedTypeSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      Assert.Equal("SampleClass", results[0].Symbol.Name);
   }

   [Fact]
   public async Task FindAsync_WithNonExistentProject_ReturnsEmptyList()
   {
      var lookup = new LookupByName
      {
         Name = "SampleClass",
         Project = "NonExistentProject"
      };

      var results = await _service.FindAsync<INamedTypeSymbol>(lookup, cancellationToken: CT);

      Assert.Empty(results);
   }

   // ===========================================
   // FindEnclosingSymbolAsync tests
   // ===========================================

   [Fact]
   public async Task FindEnclosingSymbolAsync_InsideMethodBody_FindsEnclosingMethod()
   {
      // Line 50 is inside the Calculate method body: "return a + b;"
      var lookup = new LookupByPosition
      {
         FilePath = GetCoreTestProjectPath("Types/Classes.cs"),
         Line = 50,
         Column = 10
      };

      var result = await _service.FindEnclosingSymbolAsync<IMethodSymbol>(lookup, CT);

      Assert.Equal("Calculate", result.Symbol.Name);
      Assert.NotNull(result.Document);
      Assert.NotNull(result.Syntax);
   }

   [Fact]
   public async Task FindEnclosingSymbolAsync_OnField_FindsEnclosingType()
   {
      // Line 15 is the _privateField declaration inside SampleClass
      var lookup = new LookupByPosition
      {
         FilePath = GetCoreTestProjectPath("Types/Classes.cs"),
         Line = 15,
         Column = 15
      };

      var result = await _service.FindEnclosingSymbolAsync<INamedTypeSymbol>(lookup, CT);

      Assert.Equal("SampleClass", result.Symbol.Name);
      Assert.IsType<SourceSymbolResult<INamedTypeSymbol>>(result);
   }

   [Fact]
   public async Task FindEnclosingSymbolAsync_NoMatchingAncestor_ThrowsToolException()
   {
      // Position inside a method but look for IFieldSymbol (which won't be an ancestor)
      var lookup = new LookupByPosition
      {
         FilePath = GetCoreTestProjectPath("Types/Classes.cs"),
         Line = 50,  // Inside Calculate method
         Column = 10
      };

      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _service.FindEnclosingSymbolAsync<IFieldSymbol>(lookup, CT));

      Assert.Equal(ErrorCodes.SymbolNotFound, ex.Code);
   }

   [Fact]
   public async Task FindEnclosingSymbolAsync_FileNotFound_ThrowsToolException()
   {
      var lookup = new LookupByPosition
      {
         FilePath = GetCoreTestProjectPath("NonExistent.cs"),
         Line = 1,
         Column = 1
      };

      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _service.FindEnclosingSymbolAsync<IMethodSymbol>(lookup, CT));

      Assert.Equal(ErrorCodes.FileNotInSolution, ex.Code);
   }

   // ===========================================
   // FindSourceSymbolsAsync tests
   // ===========================================

   [Fact]
   public async Task FindSourceSymbolsAsync_ReturnsOnlySourceSymbols()
   {
      var lookup = new LookupByName { Name = "SampleClass" };

      var results = await _service.FindSourceSymbolsAsync<INamedTypeSymbol>(lookup, CT);

      Assert.Single(results);
      Assert.Equal("SampleClass", results[0].Symbol.Name);
      Assert.NotNull(results[0].Document);
   }

   [Fact]
   public async Task FindSourceSymbolsAsync_FiltersOutMetadataSymbols()
   {
      // String is a metadata type from System.Runtime
      var lookup = new LookupByName { Name = "String" };

      var results = await _service.FindSourceSymbolsAsync<INamedTypeSymbol>(lookup, CT);

      // Should return empty because String is only in metadata
      Assert.Empty(results);
   }

   [Fact]
   public async Task FindSourceSymbolsAsync_WithMultipleOverloads_ReturnsAll()
   {
      // SampleClass has 3 Format overloads
      var lookup = new LookupByName { Name = "SampleClass.Format" };

      var results = await _service.FindSourceSymbolsAsync<IMethodSymbol>(lookup, CT);

      Assert.Equal(3, results.Count);
      Assert.All(results, r => Assert.Equal("Format", r.Symbol.Name));
      Assert.All(results, r => Assert.NotNull(r.Document));
   }

   // ===========================================
   // FindSingleSourceSymbolAsync tests
   // ===========================================

   [Fact]
   public async Task FindSingleSourceSymbolAsync_WithSingleMatch_ReturnsSymbol()
   {
      var lookup = new LookupByName { Name = "SampleClass" };

      var result = await _service.FindSingleSourceSymbolAsync<INamedTypeSymbol>(lookup, CT);

      Assert.Equal("SampleClass", result.Symbol.Name);
      Assert.NotNull(result.Document);
   }

   [Fact]
   public async Task FindSingleSourceSymbolAsync_NoMatch_ThrowsSymbolNotFound()
   {
      var lookup = new LookupByName { Name = "NonExistentClass" };

      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _service.FindSingleSourceSymbolAsync<INamedTypeSymbol>(lookup, CT));

      Assert.Equal(ErrorCodes.SymbolNotFound, ex.Code);
   }

   [Fact]
   public async Task FindSingleSourceSymbolAsync_MultipleMatches_ThrowsAmbiguousSymbol()
   {
      // SampleClass has 3 Format overloads
      var lookup = new LookupByName { Name = "SampleClass.Format" };

      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _service.FindSingleSourceSymbolAsync<IMethodSymbol>(lookup, CT));

      Assert.Equal(ErrorCodes.AmbiguousSymbol, ex.Code);
   }

   // Note: SYMBOL_IN_METADATA path in FindSingleSourceSymbolAsync is currently unreachable
   // because FindByNameAsync only searches source types. When metadata symbol lookup is
   // implemented (tracked in nibs: dotlens-1h7n, dotlens-8c94), tests should be added here.

   // ===========================================
   // Nested type lookup tests (progressive resolution)
   // ===========================================

   [Fact]
   public async Task FindAsync_ByName_FindsNestedTypeByPath()
   {
      // OuterClass.NestedPublicClass should find the nested type, not a member
      var lookup = new LookupByName { Name = "OuterClass.NestedPublicClass" };

      var results = await _service.FindAsync<INamedTypeSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      var nestedType = results[0].Symbol;
      Assert.Equal("NestedPublicClass", nestedType.Name);
      Assert.Equal("OuterClass", nestedType.ContainingType?.Name);
   }

   [Fact]
   public async Task FindAsync_ByName_FindsNestedTypeMemberByPath()
   {
      // OuterClass.NestedPublicClass.NestedMethod should find the method in nested type
      var lookup = new LookupByName { Name = "OuterClass.NestedPublicClass.NestedMethod" };

      var results = await _service.FindAsync<IMethodSymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      var method = results[0].Symbol;
      Assert.Equal("NestedMethod", method.Name);
      Assert.Equal("NestedPublicClass", method.ContainingType?.Name);
      Assert.Equal("OuterClass", method.ContainingType?.ContainingType?.Name);
   }

   [Fact]
   public async Task FindAsync_ByName_FindsNestedTypePropertyByPath()
   {
      // OuterClass.NestedPublicClass.NestedValue should find the property in nested type
      var lookup = new LookupByName { Name = "OuterClass.NestedPublicClass.NestedValue" };

      var results = await _service.FindAsync<IPropertySymbol>(lookup, cancellationToken: CT);

      Assert.Single(results);
      var prop = results[0].Symbol;
      Assert.Equal("NestedValue", prop.Name);
      Assert.Equal("NestedPublicClass", prop.ContainingType?.Name);
   }

   // ===========================================
   // Error handling tests
   // ===========================================

   [Fact]
   public async Task FindAsync_ByPosition_FileNotFound_ThrowsToolException()
   {
      var lookup = new LookupByPosition
      {
         FilePath = GetCoreTestProjectPath("NonExistent.cs"),
         Line = 1,
         Column = 1
      };

      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _service.FindAsync<ISymbol>(lookup, cancellationToken: CT));

      Assert.Equal(ErrorCodes.FileNotInSolution, ex.Code);
   }

   [Theory]
   [InlineData(".Foo")]           // Leading dot
   [InlineData("Foo.")]           // Trailing dot
   [InlineData("Foo..Bar")]       // Consecutive dots
   [InlineData("...")]            // All dots
   [InlineData(".Foo.Bar.")]      // Leading and trailing dots
   public async Task FindAsync_ByName_InvalidDottedName_ThrowsToolException(string invalidName)
   {
      var lookup = new LookupByName { Name = invalidName };

      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _service.FindAsync<ISymbol>(lookup, cancellationToken: CT));

      Assert.Equal(ErrorCodes.InvalidParameter, ex.Code);
   }

   [Theory]
   [InlineData("")]               // Empty string
   [InlineData("   ")]            // Whitespace only
   [InlineData("\t")]             // Tab only
   public async Task FindAsync_ByName_EmptyOrWhitespaceName_ThrowsToolException(string invalidName)
   {
      var lookup = new LookupByName { Name = invalidName };

      var ex = await Assert.ThrowsAsync<ToolException>(
         () => _service.FindAsync<ISymbol>(lookup, cancellationToken: CT));

      Assert.Equal(ErrorCodes.InvalidParameter, ex.Code);
   }

   /// <summary>
   /// The second part's offset, above 100, would sort before the first part's as text.
   /// </summary>
   [Fact]
   public void GetFirstDeclaration_PartialTypeInOneFile_ComparesOffsetsAsNumbers()
   {
      var source = "partial class P { }\n// " + new string('x', 100) + "\npartial class P { }\n";
      var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(source, path: "work/P.cs", cancellationToken: CT);
      var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("FirstDeclaration", [tree]);
      var type = compilation.GetTypeByMetadataName("P") ?? throw new InvalidOperationException("P not declared");

      var declaration = SymbolLookupService.GetFirstDeclaration(type);

      Assert.Equal([14, source.LastIndexOf("P {", StringComparison.Ordinal)], type.Locations.Select(l => l.SourceSpan.Start).Order());
      Assert.Equal("WORK/P.CS:14", declaration);
   }
}
