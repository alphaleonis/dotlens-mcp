using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tests.Helpers;
using DotLens.Tools.Editing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace DotLens.Tests.Infrastructure;

/// <summary>
/// Covers which name-lookup results count as one symbol when several projects compile one file, declare the same full
/// name, or reference another project's assembly, in a copy of the fixture solution.
/// </summary>
public class SymbolLookupIdentityTests : IsolatedProjectTestBase
{
   private const string LinkedSource = """
      namespace LinkedProbe;

      public class LinkedType
      {
         public void Run() { }
      }

      public record LinkedRecord(int Value);

      """;

   /// <summary>
   /// A linked Compile item adds one file to both projects, under a path in the same or another letter case.
   /// </summary>
   [Theory]
   [InlineData("Shared/LinkedProbe.cs")]
   [InlineData("SHARED/linkedprobe.CS")]
   public async Task LookupByName_FileCompiledIntoTwoProjects_FindsEachSymbolOnce(string secondPath)
   {
      await LinkAsync(("CoreTestProject", "Shared/LinkedProbe.cs"), ("InheritanceTestProject", secondPath));
      var service = new SymbolLookupService(WorkspaceManager);

      Assert.Equal(
         [
            "LinkedProbe.LinkedType",
            "LinkedProbe.LinkedType",
            "LinkedProbe.LinkedType.Run()",
            "LinkedProbe.LinkedType.Run()",
            "LinkedProbe.LinkedRecord.ToString()",
            "LinkedProbe.LinkedRecord.Value",
         ],
         [
            await FindSingleAsync(service, "LinkedProbe.LinkedType"),
            await FindSingleAsync(service, "LinkedType"),
            await FindSingleAsync(service, "Run"),
            await FindSingleAsync(service, "LinkedProbe.LinkedType.Run"),
            await FindSingleAsync(service, "LinkedRecord.ToString"),
            await FindSingleAsync(service, "LinkedRecord.Value"),
         ]);
   }

   /// <summary>
   /// CoreTestProject and InheritanceTestProject each declare Twin.Pair in a file of their own. The compiler's
   /// ToString has no position to look up, so the project tells those apart.
   /// </summary>
   [Theory]
   [InlineData("Twin.Pair", new[] { "Twin.Pair at CoreTestProject/Twin/Pair.cs:3:15", "Twin.Pair at InheritanceTestProject/Twin/Pair.cs:3:15" })]
   [InlineData("Pair.ToString", new[] { "Twin.Pair.ToString() in project CoreTestProject", "Twin.Pair.ToString() in project InheritanceTestProject" })]
   [InlineData("Twin.Pair.Value", new[] { "Twin.Pair.Value at CoreTestProject/Twin/Pair.cs:3:24", "Twin.Pair.Value at InheritanceTestProject/Twin/Pair.cs:3:24" })]
   public async Task LookupByName_SameNameDeclaredInTwoProjects_EachListedCandidateFindsItself(string name, string[] expected)
   {
      const string source = """
         namespace Twin;

         public record Pair(int Value);

         """;
      var writer = new WriteFileTool(WorkspaceManager);
      Assert.True((await writer.WriteFileAsync(GetCoreTestProjectPath("Twin/Pair.cs"), source, CT)).Data.IsNewFile);
      Assert.True((await writer.WriteFileAsync(GetInheritanceTestProjectPath("Twin/Pair.cs"), source, CT)).Data.IsNewFile);

      await AmbiguityRoundTrip.AssertEachListedCandidateFindsItselfAsync(
         new SymbolLookupService(WorkspaceManager), new LookupByName { Name = name }, expected, CT);
   }

   /// <summary>
   /// CoreTestProject references an assembly named as InheritanceTestProject's, which declares the same interface.
   /// </summary>
   [Fact]
   public async Task LookupByName_ProjectAlsoReferencedAsAssembly_FindsTheSourceSymbolOnce()
   {
      await AddReferenceAsync("InheritanceTestProject", "namespace InheritanceTestProject { public interface IRepository { } }");
      var service = new SymbolLookupService(WorkspaceManager);

      var result = await service.FindSingleAsync<ISymbol>(new LookupByName { Name = "InheritanceTestProject.IRepository" }, cancellationToken: CT);

      var source = Assert.IsType<SourceSymbolResult<ISymbol>>(result);
      Assert.Equal("InheritanceTestProject.IRepository", source.Symbol.ToDisplayString());
      Assert.Equal(GetInheritanceTestProjectPath("InheritanceFixtures.cs"), source.Document.FilePath);
   }

   private static async Task<string> FindSingleAsync(SymbolLookupService service, string name)
   {
      var result = await service.FindSingleAsync<ISymbol>(new LookupByName { Name = name }, cancellationToken: TestContext.Current.CancellationToken);
      return result.Symbol.ToDisplayString();
   }

   /// <summary>
   /// Writes <see cref="LinkedSource"/> to the first path and adds it to each project under its path.
   /// </summary>
   private async Task LinkAsync(params (string Project, string Path)[] links)
   {
      var file = GetAbsolutePath(links[0].Path);
      Directory.CreateDirectory(Path.GetDirectoryName(file) ?? throw new InvalidOperationException($"No directory in {file}"));
      await File.WriteAllTextAsync(file, LinkedSource, CT);

      using (await WorkspaceManager.WriteLock.AcquireAsync(CT))
      {
         var solution = WorkspaceManager.EnsureSolutionLoaded();
         foreach (var (projectName, path) in links)
         {
            var project = solution.Projects.Single(p => p.Name == projectName);
            solution = project.AddDocument(Path.GetFileName(path), SourceText.From(LinkedSource), filePath: GetAbsolutePath(path)).Project.Solution;
         }

         WorkspaceManager.UpdateSolution(solution);
      }
   }

   /// <summary>
   /// Adds to CoreTestProject a reference to an assembly with the name, compiled from the source.
   /// </summary>
   private async Task AddReferenceAsync(string assemblyName, string source)
   {
      using (await WorkspaceManager.WriteLock.AcquireAsync(CT))
      {
         var project = WorkspaceManager.EnsureSolutionLoaded().Projects.Single(p => p.Name == "CoreTestProject");
         var library = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, cancellationToken: CT)],
            project.MetadataReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
         using var image = new MemoryStream();
         var emitted = library.Emit(image, cancellationToken: CT);
         Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));

         WorkspaceManager.UpdateSolution(project.AddMetadataReference(MetadataReference.CreateFromImage(image.ToArray())).Solution);
      }
   }
}
