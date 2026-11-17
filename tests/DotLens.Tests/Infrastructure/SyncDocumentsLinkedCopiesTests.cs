using DotLens.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DotLens.Tests.Infrastructure;

/// <summary>
/// Covers sync_documents on a file that several projects compile: a project with two target frameworks, and a
/// second project that links the file.
/// </summary>
/// <remarks>
/// Each test writes its own solution to a temp directory, because the tests edit and delete its files.
/// </remarks>
public sealed class SyncDocumentsLinkedCopiesTests : IAsyncLifetime
{
   private const string SharedText = """
      namespace Lib;

      public static class Shared
      {
         public static int Value() => 1;
      }
      """;

   private const string AppOnlyText = """
      namespace App;

      public static class AppOnly
      {
         public static int Value() => 2;
      }
      """;

   private string? _tempDir;
   private WorkspaceManager? _workspaceManager;

   private static CancellationToken CT => TestContext.Current.CancellationToken;

   private string TempDir => _tempDir ?? throw new InvalidOperationException("Test not initialized");

   private WorkspaceManager Workspace => _workspaceManager ?? throw new InvalidOperationException("Test not initialized");

   private string SharedPath => Path.Combine(TempDir, "Lib", "Shared.cs");

   private string AppOnlyPath => Path.Combine(TempDir, "App", "AppOnly.cs");

   public async ValueTask InitializeAsync()
   {
      _tempDir = Path.Combine(Path.GetTempPath(), $"DotLensSyncLinkedTest_{Guid.NewGuid():N}");
      Directory.CreateDirectory(Path.Combine(_tempDir, "Lib"));
      Directory.CreateDirectory(Path.Combine(_tempDir, "App"));

      // Outside the repo, MSBuild would otherwise build the solution with the newest installed SDK.
      File.Copy(Path.Combine(FindRepoRoot(), "global.json"), Path.Combine(_tempDir, "global.json"));

      await File.WriteAllTextAsync(Path.Combine(_tempDir, "Lib", "Lib.csproj"), """
         <Project Sdk="Microsoft.NET.Sdk">
           <PropertyGroup>
             <TargetFrameworks>net10.0;net11.0</TargetFrameworks>
           </PropertyGroup>
         </Project>
         """, CT);
      await File.WriteAllTextAsync(Path.Combine(_tempDir, "App", "App.csproj"), """
         <Project Sdk="Microsoft.NET.Sdk">
           <PropertyGroup>
             <TargetFramework>net10.0</TargetFramework>
           </PropertyGroup>
           <ItemGroup>
             <Compile Include="../Lib/Shared.cs" Link="Shared.cs" />
           </ItemGroup>
         </Project>
         """, CT);
      await File.WriteAllTextAsync(SharedPath, SharedText, CT);
      await File.WriteAllTextAsync(AppOnlyPath, AppOnlyText, CT);

      var solutionPath = Path.Combine(_tempDir, "Linked.slnx");
      await File.WriteAllTextAsync(solutionPath, """
         <Solution>
           <Project Path="Lib/Lib.csproj" />
           <Project Path="App/App.csproj" />
         </Solution>
         """, CT);

      await TestFixtureRestore.EnsureRestoredAsync(solutionPath);
      _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance, Options.Create(new DotLensOptions()));
      await _workspaceManager.LoadSolutionAsync(solutionPath, CT);
   }

   public ValueTask DisposeAsync()
   {
      _workspaceManager?.Dispose();
      if (_tempDir is not null && Directory.Exists(_tempDir))
      {
         try
         {
            Directory.Delete(_tempDir, recursive: true);
         }
         catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
         {
            // A leftover temp directory does not affect other tests.
         }
      }

      return ValueTask.CompletedTask;
   }

   [Fact]
   public async Task SyncDocuments_LinkedCopies_Setup_SharedFileHasOneDocumentPerProject()
   {
      var copies = await GetCopiesAsync(SharedPath);

      Assert.Equivalent(
         new[] { ("Lib(net10.0)", SharedText), ("Lib(net11.0)", SharedText), ("App", SharedText) },
         copies,
         strict: true);
   }

   [Fact]
   public async Task SyncDocuments_LinkedCopies_SpecificFile_UpdatesEveryProjectsCopy()
   {
      Assert.Equal(3, (await GetCopiesAsync(SharedPath)).Count);
      const string newText = "namespace Lib;\n\npublic static class Shared\n{\n   public static int Value() => 3;\n}\n";
      await File.WriteAllTextAsync(SharedPath, newText, CT);

      var result = await Workspace.SyncDocumentsAsync([SharedPath], CT);

      AssertSyncResult(result.Data, updatedFiles: ["Lib/Shared.cs"], removedFiles: []);
      Assert.Equivalent(
         new[] { ("Lib(net10.0)", newText), ("Lib(net11.0)", newText), ("App", newText) },
         await GetCopiesAsync(SharedPath),
         strict: true);
   }

   [Fact]
   public async Task SyncDocuments_LinkedCopies_AllFiles_UpdatesEveryProjectsCopy()
   {
      Assert.Equal(3, (await GetCopiesAsync(SharedPath)).Count);
      const string newText = "namespace Lib;\n\npublic static class Shared\n{\n   public static int Value() => 4;\n}\n";
      await File.WriteAllTextAsync(SharedPath, newText, CT);

      var result = await Workspace.SyncDocumentsAsync(null, CT);

      AssertSyncResult(
         result.Data,
         updatedFiles:
         [
            "Lib/Shared.cs",
            "App/AppOnly.cs",
            "Lib/obj/Debug/net10.0/.NETCoreApp,Version=v10.0.AssemblyAttributes.cs",
            "Lib/obj/Debug/net10.0/Lib.AssemblyInfo.cs",
            "Lib/obj/Debug/net11.0/.NETCoreApp,Version=v11.0.AssemblyAttributes.cs",
            "Lib/obj/Debug/net11.0/Lib.AssemblyInfo.cs",
            "App/obj/Debug/net10.0/.NETCoreApp,Version=v10.0.AssemblyAttributes.cs",
            "App/obj/Debug/net10.0/App.AssemblyInfo.cs",
         ],
         removedFiles: []);
      Assert.Equivalent(
         new[] { ("Lib(net10.0)", newText), ("Lib(net11.0)", newText), ("App", newText) },
         await GetCopiesAsync(SharedPath),
         strict: true);
   }

   [Fact]
   public async Task SyncDocuments_LinkedCopies_DeletedFile_RemovesEveryProjectsCopy()
   {
      Assert.Equal(3, (await GetCopiesAsync(SharedPath)).Count);
      File.Delete(SharedPath);

      var result = await Workspace.SyncDocumentsAsync([SharedPath], CT);

      AssertSyncResult(result.Data, updatedFiles: [], removedFiles: ["Lib/Shared.cs"]);
      Assert.Empty(await GetCopiesAsync(SharedPath));
   }

   [Fact]
   public async Task SyncDocuments_LinkedCopies_UnlinkedFile_UpdatesOnlyItsOwnDocument()
   {
      var before = await GetAllTextsAsync();
      const string newText = "namespace App;\n\npublic static class AppOnly\n{\n   public static int Value() => 5;\n}\n";
      await File.WriteAllTextAsync(AppOnlyPath, newText, CT);

      var result = await Workspace.SyncDocumentsAsync([AppOnlyPath], CT);

      AssertSyncResult(result.Data, updatedFiles: ["App/AppOnly.cs"], removedFiles: []);
      var after = await GetAllTextsAsync();
      Assert.Equivalent(before.Keys, after.Keys, strict: true);
      Assert.Equal(
         [("App", Path.Combine("App", "AppOnly.cs"), newText)],
         after.Where(entry => before[entry.Key] != entry.Value).Select(entry => (entry.Key.Project, entry.Key.RelativePath, entry.Value)));
   }

   private static void AssertSyncResult(
      DotLens.Tools.Worker.SyncDocumentsResult data, string[] updatedFiles, string[] removedFiles)
   {
      Assert.Equivalent(updatedFiles, data.UpdatedFiles, strict: true);
      Assert.Equivalent(removedFiles, data.RemovedFiles, strict: true);
      Assert.Empty(data.AddedFiles);
      Assert.Equal(updatedFiles.Length, data.Updated);
      Assert.Equal(removedFiles.Length, data.Removed);
      Assert.Equal(0, data.Added);
      Assert.Equal(updatedFiles.Length + removedFiles.Length, data.TotalSynced);
      Assert.Null(data.Errors);
   }

   /// <returns>The project name and text of each document whose path is the given path.</returns>
   private async Task<List<(string Project, string Text)>> GetCopiesAsync(string path)
   {
      var copies = new List<(string Project, string Text)>();
      foreach (var id in Workspace.EnsureSolutionLoaded().GetDocumentIdsWithFilePath(path))
      {
         var document = Workspace.EnsureSolutionLoaded().GetDocument(id)
            ?? throw new InvalidOperationException($"No document for id {id}");
         copies.Add((document.Project.Name, (await document.GetTextAsync(CT)).ToString()));
      }

      return copies;
   }

   private async Task<Dictionary<DocumentKey, string>> GetAllTextsAsync()
   {
      var texts = new Dictionary<DocumentKey, string>();
      foreach (var document in Workspace.EnsureSolutionLoaded().Projects.SelectMany(p => p.Documents))
      {
         if (document.FilePath is not { } filePath || !filePath.StartsWith(TempDir, StringComparison.Ordinal))
            continue;
         texts.Add(
            new DocumentKey(document.Project.Name, Path.GetRelativePath(TempDir, filePath)),
            (await document.GetTextAsync(CT)).ToString());
      }

      return texts;
   }

   private static string FindRepoRoot()
   {
      for (var dir = Directory.GetCurrentDirectory(); dir is not null; dir = Directory.GetParent(dir)?.FullName)
      {
         if (File.Exists(Path.Combine(dir, "global.json")) && Directory.Exists(Path.Combine(dir, "tests", "TestFixtures")))
            return dir;
      }

      throw new InvalidOperationException("Could not find the repository root");
   }

   private sealed record DocumentKey(string Project, string RelativePath);
}
