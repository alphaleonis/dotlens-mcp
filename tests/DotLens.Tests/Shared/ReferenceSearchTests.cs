using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace DotLens.Tests.Shared;

/// <summary>
/// Covers which projects <see cref="ReferenceSearch"/> scans for null-conditional indexer uses.
/// </summary>
public class ReferenceSearchTests
{
   private static CancellationToken CT => TestContext.Current.CancellationToken;

   private const string LibrarySource = """
      namespace N;

      public sealed class Box
      {
         public int this[int index] { get => index; set { } }
      }

      public static class Library
      {
         public static void Use(Box? box, Box plain, System.Collections.Generic.List<int>? list)
         {
            box?[0] = 1;
            plain[1] = 2;
            list?[0] = 3;
         }
      }
      """;

   private static string UsageSource(string name) => $$"""
      namespace {{name}};

      public static class Caller
      {
         public static void Use(N.Box? box, N.Box plain, System.Collections.Generic.List<int>? list)
         {
            box?[0] = 1;
            plain[1] = 2;
            list?[0] = 3;
         }
      }
      """;

   /// <summary>
   /// Copy compiles against a built image of Library, with the same assembly name and no project reference.
   /// </summary>
   [Fact]
   public async Task FindReferences_SourceIndexer_ScansOnlyDefiningAndDependentProjects()
   {
      var solution = await CreateSolutionAsync();

      var references = await ReferenceSearch.FindReferencesAsync(await GetBoxIndexerAsync(solution), solution, CT);

      Assert.Equivalent(
         new[]
         {
            ("Library", 12, 11), ("Library", 13, 12),
            ("Direct", 7, 11), ("Direct", 8, 12),
            ("Transitive", 7, 11), ("Transitive", 8, 12),
         },
         references.Select(r => (r.Document.Project.Name, Line(r.Location), Column(r.Location))),
         strict: true);
   }

   [Fact]
   public async Task FindCallers_SourceIndexer_ScansOnlyDefiningAndDependentProjects()
   {
      var solution = await CreateSolutionAsync();

      var callers = await ReferenceSearch.FindCallersAsync(await GetBoxIndexerAsync(solution), solution, CT);

      Assert.Equivalent(
         new[]
         {
            ("N.Library.Use(N.Box?, N.Box, System.Collections.Generic.List<int>?)", 12, 11),
            ("N.Library.Use(N.Box?, N.Box, System.Collections.Generic.List<int>?)", 13, 12),
            ("Direct.Caller.Use(N.Box?, N.Box, System.Collections.Generic.List<int>?)", 7, 11),
            ("Direct.Caller.Use(N.Box?, N.Box, System.Collections.Generic.List<int>?)", 8, 12),
            ("Transitive.Caller.Use(N.Box?, N.Box, System.Collections.Generic.List<int>?)", 7, 11),
            ("Transitive.Caller.Use(N.Box?, N.Box, System.Collections.Generic.List<int>?)", 8, 12),
         },
         callers.SelectMany(c => c.Locations.Select(l => (c.CallingSymbol.ToDisplayString(), Line(l), Column(l)))),
         strict: true);
   }

   /// <summary>
   /// The indexer of <c>List&lt;T&gt;</c> comes from metadata, so every project that uses it counts.
   /// </summary>
   [Fact]
   public async Task FindReferences_MetadataIndexer_ScansEveryProject()
   {
      var solution = await CreateSolutionAsync();
      var library = solution.Projects.Single(p => p.Name == "Library");
      var compilation = await library.GetCompilationAsync(CT) ?? throw new InvalidOperationException("Library compiles");
      var list = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1") ?? throw new InvalidOperationException("List<T> resolves");
      var indexer = list.GetMembers().OfType<IPropertySymbol>().Single(p => p.IsIndexer);

      var references = await ReferenceSearch.FindReferencesAsync(indexer, solution, CT);

      Assert.Equivalent(
         new[] { ("Library", 14, 12), ("Direct", 9, 12), ("Transitive", 9, 12), ("Copy", 9, 12) },
         references.Select(r => (r.Document.Project.Name, Line(r.Location), Column(r.Location))),
         strict: true);
   }

   /// <summary>
   /// Client references only Contracts, so it calls the implementation in Implementation through the interface.
   /// </summary>
   [Fact]
   public async Task FindCallers_Implementation_ScansProjectsThatDependOnTheInterface()
   {
      var workspace = new AdhocWorkspace();
      var solution = AddProject(workspace.CurrentSolution, "Contracts", """
         namespace C;

         public interface IShelf
         {
            int this[int index] { get; set; }
         }
         """, []);
      var contractsId = solution.Projects.Single().Id;
      solution = AddProject(solution, "Implementation", """
         namespace I;

         public sealed class Shelf : C.IShelf
         {
            public int this[int index] { get => index; set { } }
         }
         """, [new ProjectReference(contractsId)]);
      solution = AddProject(solution, "Client", """
         namespace K;

         public static class Client
         {
            public static void Fill(C.IShelf? shelf) => shelf?[0] = 1;
         }
         """, [new ProjectReference(contractsId)]);
      var implementation = await solution.Projects.Single(p => p.Name == "Implementation").GetCompilationAsync(CT)
         ?? throw new InvalidOperationException("Implementation compiles");
      var shelf = implementation.GetTypeByMetadataName("I.Shelf") ?? throw new InvalidOperationException("I.Shelf resolves");

      var callers = await ReferenceSearch.FindCallersAsync(shelf.GetMembers().OfType<IPropertySymbol>().Single(), solution, CT);

      Assert.Equal(
         [("K.Client.Fill(C.IShelf?)", 5, 54)],
         callers.SelectMany(c => c.Locations.Select(l => (c.CallingSymbol.ToDisplayString(), Line(l), Column(l)))));
   }

   private const string LinkedLibrarySource = """
      namespace N;

      public sealed class Box
      {
         public int this[int index] { get => index; set { } }
      }

      public static class Library
      {
         public static void Use(Box? box) => box?[0] = 1;
      }
      """;

   private static string LinkedUsageSource(string name) => $$"""
      namespace {{name}};

      public static class Caller
      {
         public static void Use(N.Box? box) => box?[0] = 1;
      }
      """;

   // Lib(net10.0), Lib(net11.0) and Shared compile one library file, which holds one use.
   private static readonly (string Site, int Line, int Column)[] LinkedSites =
   [
      (LibraryFile, 10, 44),
      ("App10", 5, 46),
      ("App11", 5, 46),
      ("SharedApp", 5, 46),
   ];

   private const string LibraryFile = "(library file)";

   /// <summary>
   /// Find-references links the indexer across the projects that compile its file, so a search from any of them
   /// covers the dependents of all of them.
   /// </summary>
   [Theory]
   [InlineData("Lib(net10.0)")]
   [InlineData("Lib(net11.0)")]
   [InlineData("Shared")]
   public async Task FindReferences_LinkedIndexer_FindsEachNullConditionalUseOnce(string declaringProject)
   {
      var solution = await CreateLinkedSolutionAsync(LinkedLibrarySource, LinkedLibrarySource);

      var references = await ReferenceSearch.FindReferencesAsync(await GetIndexerAsync(solution, declaringProject, "N.Box", "int"), solution, CT);

      Assert.Equal(
         LinkedSites,
         references
            .Select(r => (Site(r.Document), Line(r.Location), Column(r.Location)))
            .OrderBy(site => site.Item1, StringComparer.Ordinal));
   }

   /// <summary>
   /// Find-callers follows the indexer into every project that compiles its file, as find-references does.
   /// </summary>
   [Theory]
   [InlineData("Lib(net10.0)")]
   [InlineData("Lib(net11.0)")]
   [InlineData("Shared")]
   public async Task FindCallers_LinkedIndexer_FindsEachNullConditionalCallOnce(string declaringProject)
   {
      var solution = await CreateLinkedSolutionAsync(LinkedLibrarySource, LinkedLibrarySource);

      var callers = await ReferenceSearch.FindCallersAsync(await GetIndexerAsync(solution, declaringProject, "N.Box", "int"), solution, CT);

      Assert.Equal(
         LinkedSites.Select(site => (site.Site == LibraryFile ? "N.Library.Use(N.Box?)" : $"{site.Site}.Caller.Use(N.Box?)", site.Site, site.Line, site.Column)),
         callers
            .SelectMany(c => c.Locations.Select(l => (c.CallingSymbol.ToDisplayString(), Site(solution.GetDocument(l.SourceTree)), Line(l), Column(l))))
            .OrderBy(site => site.Item2, StringComparer.Ordinal));
   }

   /// <summary>
   /// Shared links the file under a path that differs in letter case only, which the workspace treats as the same file.
   /// </summary>
   [Fact]
   public async Task FindCallers_LinkedIndexer_PathDiffersInLetterCase_FindsSharedCallsOnce()
   {
      var solution = await CreateLinkedSolutionAsync(LinkedLibrarySource, LinkedLibrarySource, sharedFileName: "LIBRARY.cs");

      var callers = await ReferenceSearch.FindCallersAsync(await GetIndexerAsync(solution, "Lib(net10.0)", "N.Box", "int"), solution, CT);

      Assert.Equal(
         LinkedSites.Select(site => site.Site),
         callers
            .SelectMany(c => c.Locations.Select(l => Site(solution.GetDocument(l.SourceTree))))
            .Order(StringComparer.Ordinal));
   }

   /// <summary>
   /// Returns the project of an app's document, which has no path, or <see cref="LibraryFile"/> for the library file.
   /// </summary>
   private static string Site(Document? document) => document switch
   {
      null => "(none)",
      { FilePath: null } => document.Project.Name,
      _ => LibraryFile,
   };

   /// <summary>
   /// The declaring file was changed in Lib(net10.0) only, so Lib(net11.0) and Shared still compile its old text, where
   /// <c>Box.this[int]</c> sits at the span that the searched indexer now has.
   /// </summary>
   [Theory]
   [InlineData(StaleOverloadSource, "N.Box", "long")]
   [InlineData(StaleOtherTypeSource, "N.Bag", "int")]
   [InlineData(StaleGenericTypeSource, "N.G`1", "int")]
   public async Task FindReferencesAndCallers_StaleLinkedCopy_DoesNotMatchAnotherIndexerAtTheSameSpan(
      string changedSource, string typeName, string parameterType)
   {
      var solution = await CreateLinkedSolutionAsync(changedSource, LinkedLibrarySource);
      var searched = await GetIndexerAsync(solution, "Lib(net10.0)", typeName, parameterType);
      var stale = await GetIndexerAsync(solution, "Lib(net11.0)", "N.Box", "int");
      Assert.Equal(stale.Locations.Single().SourceSpan, searched.Locations.Single().SourceSpan);

      var references = await ReferenceSearch.FindReferencesAsync(searched, solution, CT);
      var callers = await ReferenceSearch.FindCallersAsync(searched, solution, CT);

      Assert.Empty(references.Select(r => (r.Document.Project.Name, Line(r.Location), Column(r.Location))));
      Assert.Empty(callers.Select(c => c.CallingSymbol.ToDisplayString()));
   }

   private const string StaleOverloadSource = """
      namespace N;

      public sealed class Box
      {
         public int this[long index] { get => 0; set { } }
         public int this[int index] { get => index; set { } }
      }

      public static class Library
      {
         public static void Use(Box? box) => box?[0] = 1;
      }
      """;

   private const string StaleOtherTypeSource = """
      namespace N;

      public sealed class Bag
      {
         public int this[int index] { get => index; set { } }
      }

      public sealed class Box
      {
         public int this[int index] { get => index; set { } }
      }

      public static class Library
      {
         public static void Use(Box? box) => box?[0] = 1;
      }
      """;

   // The indexer line has one space less, so its this keyword lines up with Box.this[int] in the old text.
   private const string StaleGenericTypeSource = """
      namespace N;

      public sealed class G<T>
      {
        public int this[int index] { get => index; set { } }
      }

      public sealed class Box
      {
         public int this[int index] { get => index; set { } }
      }

      public static class Library
      {
         public static void Use(Box? box) => box?[0] = 1;
      }
      """;

   private const string PingSource = """
      namespace N;

      public static class Library
      {
         public static int Ping() => 1;
      }
      """;

   /// <summary>
   /// The local functions share the documentation ID of each other and of the method L, as Roslyn builds a local
   /// function's ID from its type and its name.
   /// </summary>
   [Fact]
   public async Task FindCallers_SameNamedLocalFunctionsAndMethod_ListsEachAsItsOwnCaller()
   {
      var callers = await FindPingCallersAsync(("App", "App", "C.cs", """
         namespace D;

         public sealed class C
         {
            public int A() { int L() => N.Library.Ping(); return L(); }
            public int B() { int L() => N.Library.Ping(); return L(); }
            public int S(bool flag)
            {
               if (flag) { int M() => N.Library.Ping(); return M(); }
               else { int M() => N.Library.Ping(); return M(); }
            }
            public static int L() => N.Library.Ping();
         }
         """));

      Assert.Equivalent(
         new[]
         {
            "L() C.cs:5:25 <- C.cs:5:42",
            "L() C.cs:6:25 <- C.cs:6:42",
            "M() C.cs:9:23 <- C.cs:9:40",
            "M() C.cs:10:18 <- C.cs:10:35",
            "D.C.L() C.cs:12:22 <- C.cs:12:39",
         },
         callers,
         strict: true);
   }

   /// <summary>
   /// AppA and AppB share an assembly name and declare the same member in files of their own.
   /// </summary>
   [Fact]
   public async Task FindCallers_SameMemberInProjectsWithOneAssemblyName_ListsEachAsItsOwnCaller()
   {
      const string source = """
         namespace App;

         public static class Caller
         {
            public static int Use() => N.Library.Ping();
         }
         """;

      var callers = await FindPingCallersAsync(("AppA", "App", Path.Combine("a", "Caller.cs"), source), ("AppB", "App", Path.Combine("b", "Caller.cs"), source));

      Assert.Equivalent(
         new[] { "App.Caller.Use() a/Caller.cs:5:22 <- a/Caller.cs:5:41", "App.Caller.Use() b/Caller.cs:5:22 <- b/Caller.cs:5:41" },
         callers,
         strict: true);
   }

   /// <summary>
   /// LinkA and LinkB compile one file under their own assembly names, so each holds a copy of Reader.ReadBoth.
   /// </summary>
   [Fact]
   public async Task FindCallers_FileLinkedIntoProjectsWithOtherAssemblyNames_ListsTheMemberOnce()
   {
      const string source = """
         namespace L;

         public interface ISource { int Read(); }

         public sealed class Source : ISource { public int Read() => N.Library.Ping(); }

         public static class Reader
         {
            public static int ReadBoth(Source source) => ((ISource)source).Read() + source.Read() + N.Library.Ping();
         }
         """;
      var solution = await CreatePingSolutionAsync(("LinkA", "LinkA", "Linked.cs", source), ("LinkB", "LinkB", "Linked.cs", source));
      var compilation = await solution.Projects.Single(p => p.Name == "LinkA").GetCompilationAsync(CT) ?? throw new InvalidOperationException("LinkA compiles");
      var read = compilation.GetTypeByMetadataName("L.Source")?.GetMembers("Read").Single() ?? throw new InvalidOperationException("LinkA declares L.Source.Read");

      var callers = await ReferenceSearch.FindCallersAsync(read, solution, CT);

      Assert.Equal(["L.Reader.ReadBoth(L.Source) Linked.cs:9:22 <- Linked.cs:9:67 Linked.cs:9:83"], callers.Select(Describe));
   }

   /// <summary>
   /// Find-callers reports the two calls in ReadBoth from either copy, in an order that varies between runs, so the
   /// copies are compared directly.
   /// </summary>
   [Fact]
   public async Task DeclarationComparer_CopiesOfOneFileInProjectsWithOtherAssemblyNames_AreEqual()
   {
      const string source = """
         namespace L;

         public static class Reader
         {
            public static int Read() => N.Library.Ping();
         }
         """;
      var solution = await CreatePingSolutionAsync(("LinkA", "LinkA", "Linked.cs", source), ("LinkB", "LinkB", "Linked.CS", source));
      var copies = new List<ISymbol>();
      foreach (var name in new[] { "LinkA", "LinkB" })
      {
         var compilation = await solution.Projects.Single(p => p.Name == name).GetCompilationAsync(CT) ?? throw new InvalidOperationException($"{name} compiles");
         copies.Add(compilation.GetTypeByMetadataName("L.Reader")?.GetMembers("Read").Single() ?? throw new InvalidOperationException($"{name} declares L.Reader.Read"));
      }

      Assert.False(SymbolEqualityComparer.Default.Equals(copies[0], copies[1]));
      Assert.True(ReferenceSearch.DeclarationComparer.Instance.Equals(copies[0], copies[1]));
      Assert.Equal(ReferenceSearch.DeclarationComparer.Instance.GetHashCode(copies[0]), ReferenceSearch.DeclarationComparer.Instance.GetHashCode(copies[1]));
   }

   [Fact]
   public async Task FindCallers_OverloadsAndGenericMethodOfGenericType_ListsEachAsItsOwnCaller()
   {
      var callers = await FindPingCallersAsync(("App", "App", "G.cs", """
         namespace D;

         public sealed class G<T>
         {
            public int M() => N.Library.Ping();
            public int M<U>() => N.Library.Ping();
            public int M(int value) => N.Library.Ping();
         }
         """));

      Assert.Equivalent(
         new[]
         {
            "D.G<T>.M() G.cs:5:15 <- G.cs:5:32",
            "D.G<T>.M<U>() G.cs:6:15 <- G.cs:6:35",
            "D.G<T>.M(int) G.cs:7:15 <- G.cs:7:41",
         },
         callers,
         strict: true);
   }

   /// <summary>
   /// The getter and the initializer of the implementing part of a partial property belong to that part.
   /// </summary>
   [Fact]
   public async Task FindCallers_PartialMembers_ListsTheImplementingPartOnce()
   {
      var callers = await FindPingCallersAsync(("App", "App", "P.cs", """
         namespace D;

         public partial class P
         {
            public partial int Value { get; }
            public partial int Run();
         }

         public partial class P
         {
            public partial int Value { get => field + N.Library.Ping(); } = N.Library.Ping();
            public partial int Run() => N.Library.Ping();
         }
         """));

      Assert.Equivalent(
         new[] { "D.P.Value P.cs:11:23 <- P.cs:11:56 P.cs:11:78", "D.P.Run() P.cs:12:23 <- P.cs:12:42" },
         callers,
         strict: true);
   }

   private static async Task<List<string>> FindPingCallersAsync(params (string Name, string AssemblyName, string FileName, string Source)[] projects)
   {
      var solution = await CreatePingSolutionAsync(projects);
      var library = await solution.Projects.Single(p => p.Name == "Library").GetCompilationAsync(CT) ?? throw new InvalidOperationException("Library compiles");
      var ping = library.GetTypeByMetadataName("N.Library")?.GetMembers("Ping").Single() ?? throw new InvalidOperationException("Library declares N.Library.Ping");

      var callers = await ReferenceSearch.FindCallersAsync(ping, solution, CT);

      return callers.Select(Describe).ToList();
   }

   /// <summary>
   /// Creates Library from PingSource and one project for each entry that references it, with each file under one
   /// temporary directory.
   /// </summary>
   private static async Task<Solution> CreatePingSolutionAsync(params (string Name, string AssemblyName, string FileName, string Source)[] projects)
   {
      var directory = Path.Combine(Path.GetTempPath(), "ReferenceSearchTests", "Callers");
      var solution = AddProject(new AdhocWorkspace().CurrentSolution, "Library", PingSource, [], filePath: Path.Combine(directory, "Library.cs"));
      var libraryId = solution.Projects.Single().Id;
      foreach (var project in projects)
      {
         solution = AddProject(
            solution, project.Name, project.Source, [new ProjectReference(libraryId)], assemblyName: project.AssemblyName, filePath: Path.Combine(directory, project.FileName));
      }

      foreach (var project in solution.Projects)
      {
         var compilation = await project.GetCompilationAsync(CT) ?? throw new InvalidOperationException($"{project.Name} compiles");
         Assert.Empty(compilation.GetDiagnostics(CT).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{project.Name}: {d}"));
      }

      return solution;
   }

   /// <summary>
   /// Describes a caller as its display string and declaration, then its references, each as a path under the
   /// temporary directory of <see cref="CreatePingSolutionAsync"/> with '/' separators.
   /// </summary>
   private static string Describe(SymbolCallers caller)
   {
      var declaration = caller.CallingSymbol?.Locations.First() ?? throw new InvalidOperationException("The sources have no top-level statements");
      return $"{caller.CallingSymbol.ToDisplayString()} {Site(declaration)} <- {string.Join(" ", caller.Locations.Select(Site))}";

      static string Site(Location location) =>
         $"{Path.GetRelativePath(Path.Combine(Path.GetTempPath(), "ReferenceSearchTests", "Callers"), location.SourceTree?.FilePath ?? "").Replace(Path.DirectorySeparatorChar, '/')}:{Line(location)}:{Column(location)}";
   }

   /// <summary>
   /// Creates Lib(net10.0) from <paramref name="net10Source"/>, and Lib(net11.0) and Shared from
   /// <paramref name="otherSource"/>, all three at one file path. Shared has its own assembly name. Each has an app.
   /// </summary>
   private static async Task<Solution> CreateLinkedSolutionAsync(string net10Source, string otherSource, string sharedFileName = "Library.cs")
   {
      var libraryPath = Path.Combine(Path.GetTempPath(), "ReferenceSearchTests", "Library.cs");
      var solution = new AdhocWorkspace().CurrentSolution;
      solution = AddProject(solution, "Lib(net10.0)", net10Source, [], assemblyName: "Lib", filePath: libraryPath);
      solution = AddProject(solution, "Lib(net11.0)", otherSource, [], assemblyName: "Lib", filePath: libraryPath);
      solution = AddProject(solution, "Shared", otherSource, [], filePath: Path.Combine(Path.GetTempPath(), "ReferenceSearchTests", sharedFileName));
      var lib10 = solution.Projects.Single(p => p.Name == "Lib(net10.0)").Id;
      var lib11 = solution.Projects.Single(p => p.Name == "Lib(net11.0)").Id;
      var shared = solution.Projects.Single(p => p.Name == "Shared").Id;
      solution = AddProject(solution, "App10", LinkedUsageSource("App10"), [new ProjectReference(lib10)]);
      solution = AddProject(solution, "App11", LinkedUsageSource("App11"), [new ProjectReference(lib11)]);
      solution = AddProject(solution, "SharedApp", LinkedUsageSource("SharedApp"), [new ProjectReference(shared)]);

      foreach (var project in solution.Projects)
      {
         var compilation = await project.GetCompilationAsync(CT) ?? throw new InvalidOperationException($"{project.Name} compiles");
         Assert.Empty(compilation.GetDiagnostics(CT).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{project.Name}: {d}"));
      }

      return solution;
   }

   private static async Task<IPropertySymbol> GetIndexerAsync(Solution solution, string projectName, string typeName, string parameterType)
   {
      var project = solution.Projects.Single(p => p.Name == projectName);
      var compilation = await project.GetCompilationAsync(CT) ?? throw new InvalidOperationException($"{projectName} compiles");
      var type = compilation.GetTypeByMetadataName(typeName) ?? throw new InvalidOperationException($"{typeName} resolves");
      return type.GetMembers()
         .OfType<IPropertySymbol>()
         .Single(p => p.IsIndexer && p.Parameters.Single().Type.ToDisplayString() == parameterType);
   }

   private static int Line(Location location) => location.GetLineSpan().StartLinePosition.Line + 1;

   private static int Column(Location location) => location.GetLineSpan().StartLinePosition.Character + 1;

   private static async Task<IPropertySymbol> GetBoxIndexerAsync(Solution solution, string projectName = "Library")
   {
      var library = solution.Projects.Single(p => p.Name == projectName);
      var compilation = await library.GetCompilationAsync(CT) ?? throw new InvalidOperationException($"{projectName} compiles");
      var box = compilation.GetTypeByMetadataName("N.Box") ?? throw new InvalidOperationException("N.Box resolves");
      return box.GetMembers().OfType<IPropertySymbol>().Single(p => p.IsIndexer);
   }

   /// <summary>
   /// Creates Library, Direct referencing Library, Transitive referencing Direct and Library, and Copy referencing an
   /// emitted image of Library.
   /// </summary>
   private static async Task<Solution> CreateSolutionAsync()
   {
      var workspace = new AdhocWorkspace();
      var library = AddProject(workspace.CurrentSolution, "Library", LibrarySource, []);
      var libraryId = library.Projects.Single().Id;

      var compilation = await library.Projects.Single().GetCompilationAsync(CT) ?? throw new InvalidOperationException("Library compiles");
      using var image = new MemoryStream();
      var emitted = compilation.Emit(image, cancellationToken: CT);
      Assert.True(emitted.Success, string.Join("; ", emitted.Diagnostics));

      var direct = AddProject(library, "Direct", UsageSource("Direct"), [new ProjectReference(libraryId)]);
      var directId = direct.Projects.Single(p => p.Name == "Direct").Id;
      var transitive = AddProject(direct, "Transitive", UsageSource("Transitive"), [new ProjectReference(directId), new ProjectReference(libraryId)]);
      var copy = AddProject(transitive, "Copy", UsageSource("Copy"), [], [MetadataReference.CreateFromImage(image.ToArray())]);

      foreach (var project in copy.Projects)
      {
         var projectCompilation = await project.GetCompilationAsync(CT) ?? throw new InvalidOperationException($"{project.Name} compiles");
         Assert.Empty(projectCompilation.GetDiagnostics(CT).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => $"{project.Name}: {d}"));
      }

      return copy;
   }

   private static Solution AddProject(
      Solution solution,
      string name,
      string source,
      ProjectReference[] projectReferences,
      MetadataReference[]? extraReferences = null,
      string? assemblyName = null,
      string? filePath = null)
   {
      var projectId = ProjectId.CreateNewId();
      var info = ProjectInfo.Create(
         projectId,
         VersionStamp.Create(),
         name,
         assemblyName ?? name,
         LanguageNames.CSharp,
         compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            .WithNullableContextOptions(NullableContextOptions.Enable),
         parseOptions: new CSharpParseOptions(LanguageVersion.Preview),
         metadataReferences: [.. TestHelpers.CoreReferences, .. extraReferences ?? []],
         projectReferences: projectReferences);

      var document = DocumentInfo.Create(
         DocumentId.CreateNewId(projectId),
         $"{name}.cs",
         loader: TextLoader.From(TextAndVersion.Create(SourceText.From(source), VersionStamp.Create())),
         filePath: filePath);
      return solution.AddProject(info).AddDocument(document);
   }
}
