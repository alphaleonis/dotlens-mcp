using DotLens.Infrastructure;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
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

         var inheritedMembers = RoslynHelpers.GetTypeMembers(derivedType!, compilation, includeInherited: true).ToList();

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

         var inheritedMembers = RoslynHelpers.GetTypeMembers(derivedType!, compilation, includeInherited: true).ToList();

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

         var inheritedMembers = RoslynHelpers.GetTypeMembers(derivedType!, compilation, includeInherited: true).ToList();

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

         var inheritedMembers = RoslynHelpers.GetTypeMembers(testType!, compilation, includeInherited: true).ToList();

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
   public async Task GetDeclaration_CompiledType_ListsReimplementedInterfaces(string typeName, string expected)
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

      using var workspace = new AdhocWorkspace();
      Assert.Equal(expected, await RoslynHelpers.GetDeclarationAsync(type, workspace.CurrentSolution, TestContext.Current.CancellationToken));
   }

   /// <summary>
   /// The consumer imports all metadata, so the internal and private protected members of the referenced base load too.
   /// </summary>
   [Fact]
   public void TypeMembers_IncludeInherited_Accessibility_MetadataBase()
   {
      var reference = EmitLibrary("CompiledLedger", """
         public class Ledger
         {
            public void Post() { }
            protected void Audit() { }
            internal void Reconcile() { }
            protected internal void Close() { }
            private protected void Seal() { }
            private void Shred() { }
            internal int Pending;
         }
         """);
      var consumer = CSharpCompilation.Create(
         "Consumer",
         [CSharpSyntaxTree.ParseText("public class Account : Ledger { public void Deposit() { } }", cancellationToken: TestContext.Current.CancellationToken)],
         [.. TestHelpers.CoreReferences, reference],
         new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary).WithMetadataImportOptions(MetadataImportOptions.All));
      var account = consumer.GetTypeByMetadataName("Account") ?? throw new InvalidOperationException("Account is not in the source");
      var ledger = account.BaseType ?? throw new InvalidOperationException("Account has a base class");
      Assert.Equal(["Pending", "Post", "Audit", "Reconcile", "Close", "Seal", "Shred", ".ctor"], ledger.GetMembers().Select(m => m.Name));

      var members = RoslynHelpers.GetTypeMembers(account, consumer, includeInherited: true);

      Assert.Equal(["Account.Deposit()", "Ledger.Post()", "Ledger.Audit()", "Ledger.Close()"], members.Select(m => m.ToDisplayString()));
   }

   /// <summary>
   /// Records compiled into a referenced assembly, whose metadata lists the IEquatable of the record itself whether or not
   /// its source wrote it. Metadata does not mark a record struct, so Pair reads as the struct that Roslyn sees.
   /// </summary>
   [Theory]
   [InlineData("Point", "public record Point")]
   [InlineData("Labeled", "public record Labeled : Point")]
   [InlineData("Tagged", "public record Tagged : IEquatable<string>")]
   [InlineData("Declared", "public record Declared")]
   [InlineData("Pair", "public struct Pair : IEquatable<Pair>")]
   public async Task FullDeclaration_RecordEquatable_MetadataRecord(string typeName, string expected)
   {
      var reference = EmitLibrary("CompiledRecords", """
         using System;
         public record Point(int X);
         public record Labeled(int X, string Label) : Point(X);
         public record Tagged(int X) : IEquatable<string> { public bool Equals(string? other) => false; }
         public record Declared(int X) : IEquatable<Declared>;
         public record struct Pair(int A);
         """);
      var consumer = CSharpCompilation.Create("Consumer", references: [.. TestHelpers.CoreReferences, reference]);
      var type = consumer.GetTypeByMetadataName(typeName) ?? throw new InvalidOperationException($"{typeName} is not in the reference");

      using var workspace = new AdhocWorkspace();
      Assert.Equal(expected, await RoslynHelpers.GetDeclarationAsync(type, workspace.CurrentSolution, TestContext.Current.CancellationToken));
   }

   /// <summary>
   /// Partial records of one project seen through a referencing project whose core library differs, so the referencing
   /// compilation retargets their symbols.
   /// </summary>
   [Theory]
   [InlineData("Written", "public record Written(int X) : IEquatable<Written>, IDisposable")]
   [InlineData("Other", "public record Other(int X) : IEquatable<string>, IDisposable")]
   [InlineData("Unwritten", "public record Unwritten(int X) : IDisposable, IComparable<Unwritten>")]
   [InlineData("Derived", "public record Derived(int X) : Written(X), IDisposable")]
   public async Task FullDeclaration_RecordEquatable_RetargetedRecord(string typeName, string expected)
   {
      var cancellationToken = TestContext.Current.CancellationToken;
      using var workspace = new AdhocWorkspace();
      var declaringId = ProjectId.CreateNewId();
      var referencingId = ProjectId.CreateNewId();
      var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);
      var solution = workspace.CurrentSolution
         .AddProject(ProjectInfo.Create(declaringId, VersionStamp.Create(), "DeclaringRecords", "DeclaringRecords", LanguageNames.CSharp,
            compilationOptions: options, metadataReferences: ReferencePackReferences()))
         .AddDocument(DocumentId.CreateNewId(declaringId), "Records.cs", """
            using System;
            public partial record Written(int X) : IEquatable<Written>;
            public partial record Written : IDisposable { public void Dispose() { } }
            public partial record Other(int X) : IEquatable<string> { public bool Equals(string? other) => false; }
            public partial record Other : IDisposable { public void Dispose() { } }
            public partial record Unwritten(int X) : IDisposable { public void Dispose() { } }
            public partial record Unwritten : IComparable<Unwritten> { public int CompareTo(Unwritten? other) => 0; }
            public partial record Derived(int X) : Written(X);
            public partial record Derived : IDisposable { public new void Dispose() { } }
            """)
         .AddProject(ProjectInfo.Create(referencingId, VersionStamp.Create(), "Referencing", "Referencing", LanguageNames.CSharp,
            compilationOptions: options, metadataReferences: TestHelpers.CoreReferences, projectReferences: [new ProjectReference(declaringId)]));
      var declaring = await GetCompilationAsync(solution, declaringId, cancellationToken);
      Assert.Empty(declaring.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));
      var referencing = await GetCompilationAsync(solution, referencingId, cancellationToken);
      var type = referencing.GetTypeByMetadataName(typeName) ?? throw new InvalidOperationException($"{typeName} is not in the reference");
      Assert.False(type.ContainingAssembly is ISourceAssemblySymbol, "The referencing compilation retargets the record");
      Assert.Equal(2, type.DeclaringSyntaxReferences.Length);

      Assert.Equal(expected, await RoslynHelpers.GetDeclarationAsync(type, solution, cancellationToken));
   }

   /// <summary>
   /// A partial record in a file that two projects link, seen through a project that references the second of them, so
   /// the declaration binds in the project that holds the record's own syntax trees.
   /// </summary>
   [Fact]
   public async Task FullDeclaration_RecordEquatable_RetargetedLinkedRecord()
   {
      var cancellationToken = TestContext.Current.CancellationToken;
      using var workspace = new AdhocWorkspace();
      var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);
      var filePath = Path.Combine(Path.GetTempPath(), "RoslynHelpersTests", "Records.cs");
      var source = SourceText.From("""
         using System;
         public partial record Unwritten(int X) : IDisposable { public void Dispose() { } }
         public partial record Unwritten : IComparable<Unwritten> { public int CompareTo(Unwritten? other) => 0; }
         """);
      var solution = workspace.CurrentSolution;
      var linkedIds = new List<ProjectId>();
      foreach (var name in new[] { "LinkedFirst", "LinkedSecond" })
      {
         var id = ProjectId.CreateNewId();
         linkedIds.Add(id);
         solution = solution
            .AddProject(ProjectInfo.Create(id, VersionStamp.Create(), name, name, LanguageNames.CSharp,
               compilationOptions: options, metadataReferences: ReferencePackReferences()))
            .AddDocument(DocumentId.CreateNewId(id), "Records.cs", source, filePath: filePath);
      }
      var referencingId = ProjectId.CreateNewId();
      solution = solution.AddProject(ProjectInfo.Create(referencingId, VersionStamp.Create(), "Referencing", "Referencing", LanguageNames.CSharp,
         compilationOptions: options, metadataReferences: TestHelpers.CoreReferences, projectReferences: [new ProjectReference(linkedIds[1])]));
      var referencing = await GetCompilationAsync(solution, referencingId, cancellationToken);
      var type = referencing.GetTypeByMetadataName("Unwritten") ?? throw new InvalidOperationException("Unwritten is not in the reference");
      Assert.False(type.ContainingAssembly is ISourceAssemblySymbol, "The referencing compilation retargets the record");
      Assert.Equal("LinkedSecond", type.ContainingAssembly.Name);
      Assert.Equal("LinkedSecond", solution.GetDocument(type.DeclaringSyntaxReferences[0].SyntaxTree)?.Project.Name);

      Assert.Equal(
         "public record Unwritten(int X) : IDisposable, IComparable<Unwritten>",
         await RoslynHelpers.GetDeclarationAsync(type, solution, cancellationToken));
   }

   /// <summary>
   /// Unions compiled into a referenced assembly. Metadata holds a union declaration and a custom union struct as the same
   /// struct with the Union attribute.
   /// </summary>
   [Theory]
   [InlineData("Shape", "public struct Shape : IUnion")]
   [InlineData("Custom", "public struct Custom : IUnion")]
   public async Task Declaration_MetadataUnionKeepsStructKeyword(string typeName, string expected)
   {
      var references = ReferencePackReferences();
      var reference = EmitLibrary("CompiledUnions", """
         using System.Runtime.CompilerServices;
         public record Circle(double Radius);
         public record Square(double Side);
         public union Shape(Circle, Square);
         [Union] public struct Custom : IUnion { public Custom(int number) => Value = number; public object? Value { get; } }
         """, references);
      var consumer = CSharpCompilation.Create("Consumer", references: [.. references, reference]);
      var type = consumer.GetTypeByMetadataName(typeName) ?? throw new InvalidOperationException($"{typeName} is not in the reference");
      Assert.Empty(type.DeclaringSyntaxReferences);

      using var workspace = new AdhocWorkspace();
      Assert.Equal(expected, await RoslynHelpers.GetDeclarationAsync(type, workspace.CurrentSolution, TestContext.Current.CancellationToken));
      Assert.Equal(SymbolModifiers.Sealed | SymbolModifiers.Union, RoslynHelpers.GetSymbolModifiers(type));
   }

   /// <summary>
   /// A partial union with its case list on the second part, a nested union, and unions with a base list, the readonly
   /// modifier or an attribute.
   /// </summary>
   [Theory]
   [InlineData("Split", "public union Split(int, string)")]
   [InlineData("Outer+Nested", "public union Nested(int, string)")]
   [InlineData("Measured", "public union Measured(int, string) : IMeasured")]
   [InlineData("Frozen", "public readonly union Frozen(int, string)")]
   [InlineData("Flagged", "public union Flagged(int, string)")]
   public async Task Declaration_SourceUnionPrintsUnionKeyword(string typeName, string expected)
   {
      var cancellationToken = TestContext.Current.CancellationToken;
      using var workspace = new AdhocWorkspace();
      var projectId = ProjectId.CreateNewId();
      var solution = workspace.CurrentSolution
         .AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), "Unions", "Unions", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable),
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
            metadataReferences: ReferencePackReferences()))
         .AddDocument(DocumentId.CreateNewId(projectId), "Unions.cs", """
            public interface IMeasured { }
            public partial union Split { public int Size => 0; }
            public partial union Split(int, string);
            public class Outer { public union Nested(int, string); }
            public union Measured(int, string) : IMeasured;
            public readonly union Frozen(int, string);
            [System.Obsolete] public union Flagged(int, string);
            """);
      var compilation = await GetCompilationAsync(solution, projectId, cancellationToken);
      Assert.Empty(compilation.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));
      var type = compilation.GetTypeByMetadataName(typeName) ?? throw new InvalidOperationException($"{typeName} is not in the source");

      Assert.Equal(expected, await RoslynHelpers.GetDeclarationAsync(type, solution, cancellationToken));
   }

   /// <summary>
   /// List of T implements the indexer of IList explicitly, and metadata reports its accessors as explicit implementations.
   /// </summary>
   [Fact]
   public void TypeMembers_MemberKinds_MetadataExplicitImplementations()
   {
      var consumer = CSharpCompilation.Create("Consumer", references: TestHelpers.CoreReferences);
      var list = consumer.GetTypeByMetadataName("System.Collections.Generic.List`1") ?? throw new InvalidOperationException("List is in the core library");
      string[] names = ["System.Collections.IList.get_Item", "System.Collections.IList.set_Item", "System.Collections.IList.Add"];
      var methods = names.Select(name => list.GetMembers(name).OfType<IMethodSymbol>().Single()).ToList();
      Assert.All(methods, m => Assert.Equal(MethodKind.ExplicitInterfaceImplementation, m.MethodKind));

      Assert.Equal(
         [("Accessor", false), ("Accessor", false), ("Method", true)],
         methods.Select(m => (RoslynHelpers.GetMemberKindString(m), RoslynHelpers.MatchesMemberKind(m, MemberKindFilter.Method))));
   }

   /// <summary>
   /// Returns the reference assemblies of the newest .NET reference pack, whose core library is System.Runtime rather than the
   /// System.Private.CoreLib of <see cref="TestHelpers.CoreReferences"/>.
   /// </summary>
   private static MetadataReference[] ReferencePackReferences()
   {
      var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location) ?? throw new InvalidOperationException("The runtime has a directory");
      var dotnetRoot = Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", ".."));
      var packDirectory = Directory.EnumerateDirectories(Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref"))
         .Select(directory => (Directory: directory, Version: Version.TryParse(Path.GetFileName(directory).Split('-')[0], out var version) ? version : null))
         .Where(pack => pack.Version is not null)
         .OrderByDescending(pack => pack.Version)
         .SelectMany(pack => Directory.EnumerateDirectories(Path.Combine(pack.Directory, "ref")))
         .First(directory => File.Exists(Path.Combine(directory, "System.Runtime.dll")));
      return [.. Directory.EnumerateFiles(packDirectory, "*.dll").Select(path => MetadataReference.CreateFromFile(path))];
   }

   private static async Task<Compilation> GetCompilationAsync(Solution solution, ProjectId projectId, CancellationToken cancellationToken)
   {
      var project = solution.GetProject(projectId) ?? throw new InvalidOperationException($"{projectId} is not in the solution");
      return await project.GetCompilationAsync(cancellationToken) ?? throw new InvalidOperationException($"{project.Name} has no compilation");
   }

   private static MetadataReference EmitLibrary(string assemblyName, string source, MetadataReference[]? references = null)
   {
      var library = CSharpCompilation.Create(
         assemblyName,
         [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview), cancellationToken: TestContext.Current.CancellationToken)],
         references ?? TestHelpers.CoreReferences,
         new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
      using var image = new MemoryStream();
      var emitted = library.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
      Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
      return MetadataReference.CreateFromImage(image.ToArray());
   }
}
