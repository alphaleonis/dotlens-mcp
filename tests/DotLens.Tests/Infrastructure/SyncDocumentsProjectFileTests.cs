using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Editing;
using DotLens.Tools.Formatting;
using DotLens.Tools.Refactoring;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DotLens.Tests.Infrastructure;

/// <summary>
/// Covers that sync_documents changes only the in-memory solution, so a later write to the workspace does not apply
/// the synced changes to the project files or to disk.
/// </summary>
/// <remarks>
/// Each test writes its own solution to a temp directory, because the tests edit and delete its files.
/// </remarks>
public sealed class SyncDocumentsProjectFileTests : IAsyncLifetime
{
   private const string AppProjectText = """
      <Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup>
          <TargetFramework>net10.0</TargetFramework>
        </PropertyGroup>
        <ItemGroup>
          <Compile Include="../Lib/Shared.cs" Link="Shared.cs" />
        </ItemGroup>
      </Project>
      """;

   // Explicit/Extra.cs has its own item, because MSBuild cannot remove an item that the SDK's default glob adds.
   private const string LibProjectText = """
      <Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup>
          <TargetFramework>net10.0</TargetFramework>
        </PropertyGroup>
        <ItemGroup>
          <Compile Remove="Explicit/**" />
          <Compile Include="Explicit/Extra.cs" />
        </ItemGroup>
      </Project>
      """;

   private const string SharedText = "namespace Lib;\n\npublic static class Shared\n{\n    public static int Value() => 1;\n}\n";

   private const string ExtraText = "namespace Lib;\n\npublic static class Extra\n{\n}\n";

   private const string PlainText = "namespace App;\n\npublic static class Plain\n{\n    public static int Value() => 3;\n}\n";

   private const string AppOnlyText = "using System;\nusing System.Text;\n\nnamespace App;\n\npublic static class AppOnly\n{\n    public static int Value()  =>  Math.Abs(2);\n}\n";

   private string? _tempDir;
   private WorkspaceManager? _workspaceManager;

   public enum DeletedFile
   {
      Linked,
      Plain,
   }

   public enum WriteTool
   {
      EditFile,
      WriteFile,
      RenameSymbol,
      FormatDocumentBatch,
      OrganizeUsings,
   }

   public static TheoryData<DeletedFile, WriteTool> DeletedFileAndWriteTool
   {
      get
      {
         var data = new TheoryData<DeletedFile, WriteTool>();
         foreach (var deleted in Enum.GetValues<DeletedFile>())
         {
            foreach (var tool in Enum.GetValues<WriteTool>())
               data.Add(deleted, tool);
         }

         return data;
      }
   }

   private static CancellationToken CT => TestContext.Current.CancellationToken;

   private string TempDir => _tempDir ?? throw new InvalidOperationException("Test not initialized");

   private WorkspaceManager Workspace => _workspaceManager ?? throw new InvalidOperationException("Test not initialized");

   private string AppProjectPath => Path.Combine(TempDir, "App", "App.csproj");

   private string LibProjectPath => Path.Combine(TempDir, "Lib", "Lib.csproj");

   private string SharedPath => Path.Combine(TempDir, "Lib", "Shared.cs");

   private string ExtraPath => Path.Combine(TempDir, "Lib", "Explicit", "Extra.cs");

   private string PlainPath => Path.Combine(TempDir, "App", "Plain.cs");

   private string AppOnlyPath => Path.Combine(TempDir, "App", "AppOnly.cs");

   public async ValueTask InitializeAsync()
   {
      _tempDir = Path.Combine(Path.GetTempPath(), $"DotLensSyncProjectFileTest_{Guid.NewGuid():N}");
      Directory.CreateDirectory(Path.Combine(_tempDir, "Lib", "Explicit"));
      Directory.CreateDirectory(Path.Combine(_tempDir, "App"));

      // Outside the repo, MSBuild would otherwise build the solution with the newest installed SDK.
      File.Copy(Path.Combine(FindRepoRoot(), "global.json"), Path.Combine(_tempDir, "global.json"));

      await File.WriteAllTextAsync(LibProjectPath, LibProjectText, CT);
      await File.WriteAllTextAsync(AppProjectPath, AppProjectText, CT);
      await File.WriteAllTextAsync(SharedPath, SharedText, CT);
      await File.WriteAllTextAsync(ExtraPath, ExtraText, CT);
      await File.WriteAllTextAsync(PlainPath, PlainText, CT);
      await File.WriteAllTextAsync(AppOnlyPath, AppOnlyText, CT);

      var solutionPath = Path.Combine(_tempDir, "ProjectFile.slnx");
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

   [Theory]
   [MemberData(nameof(DeletedFileAndWriteTool))]
   public async Task SyncDocuments_DeletedFile_DoesNotRewriteProject(DeletedFile deleted, WriteTool tool)
   {
      var deletedPath = deleted switch
      {
         DeletedFile.Linked => SharedPath,
         DeletedFile.Plain => PlainPath,
         _ => throw new InvalidOperationException($"Unexpected value: {deleted}"),
      };
      var appProjectBytes = await File.ReadAllBytesAsync(AppProjectPath, CT);
      var libProjectBytes = await File.ReadAllBytesAsync(LibProjectPath, CT);
      File.Delete(deletedPath);
      var syncResult = await Workspace.SyncDocumentsAsync([deletedPath], CT);
      Assert.Equal([Workspace.FormatPath(deletedPath)], syncResult.Data.RemovedFiles);

      var expectedAppOnlyText = await RunWriteToolAsync(tool);

      Assert.Equal(appProjectBytes, await File.ReadAllBytesAsync(AppProjectPath, CT));
      Assert.Equal(libProjectBytes, await File.ReadAllBytesAsync(LibProjectPath, CT));
      Assert.Equal(expectedAppOnlyText, await File.ReadAllTextAsync(AppOnlyPath, CT));
      Assert.False(File.Exists(deletedPath));
      Assert.Empty(Workspace.EnsureSolutionLoaded().GetDocumentIdsWithFilePath(deletedPath));
   }

   [Fact]
   public async Task SyncDocuments_UpdatedFile_LaterWriteDoesNotRewriteIt()
   {
      await File.WriteAllTextAsync(SharedPath, SharedText.Replace("=> 1", "=> 4", StringComparison.Ordinal), CT);
      var syncResult = await Workspace.SyncDocumentsAsync([SharedPath], CT);
      Assert.Equal([Workspace.FormatPath(SharedPath)], syncResult.Data.UpdatedFiles);
      var unsyncedText = SharedText.Replace("=> 1", "=> 5", StringComparison.Ordinal);
      await File.WriteAllTextAsync(SharedPath, unsyncedText, CT);

      var expectedAppOnlyText = await RunWriteToolAsync(WriteTool.EditFile);

      Assert.Equal(unsyncedText, await File.ReadAllTextAsync(SharedPath, CT));
      Assert.Equal(expectedAppOnlyText, await File.ReadAllTextAsync(AppOnlyPath, CT));
   }

   [Fact]
   public async Task WriteFile_NewFile_LaterWriteDoesNotRewriteProject()
   {
      var appProjectBytes = await File.ReadAllBytesAsync(AppProjectPath, CT);
      var newPath = Path.Combine(TempDir, "App", "Added.cs");
      const string newText = "namespace App;\n\npublic static class Added\n{\n}\n";
      var writeResult = await new WriteFileTool(Workspace).WriteFileAsync(newPath, newText, CT);
      Assert.True(writeResult.Data.IsNewFile);

      var expectedAppOnlyText = await RunWriteToolAsync(WriteTool.EditFile);

      Assert.Equal(appProjectBytes, await File.ReadAllBytesAsync(AppProjectPath, CT));
      Assert.Equal(expectedAppOnlyText, await File.ReadAllTextAsync(AppOnlyPath, CT));
      Assert.Equal(newText, await File.ReadAllTextAsync(newPath, CT));
      Assert.Single(Workspace.EnsureSolutionLoaded().GetDocumentIdsWithFilePath(newPath));
   }

   /// <summary>
   /// A removal that a write itself makes still reaches its project file and the disk after a sync removed the linked
   /// file, whose item stays in App.csproj.
   /// </summary>
   [Fact]
   public async Task SyncDocuments_DeletedFile_LaterWriteStillAppliesItsOwnRemoval()
   {
      File.Delete(SharedPath);
      var syncResult = await Workspace.SyncDocumentsAsync([SharedPath], CT);
      Assert.Equal([Workspace.FormatPath(SharedPath)], syncResult.Data.RemovedFiles);
      var solution = Workspace.EnsureSolutionLoaded();
      var extraId = Assert.Single(solution.GetDocumentIdsWithFilePath(ExtraPath));

      bool applied;
      using (await Workspace.WriteLock.AcquireAsync(CT))
         applied = Workspace.ApplyChanges(solution.RemoveDocument(extraId));

      Assert.True(applied);
      Assert.False(File.Exists(ExtraPath));
      Assert.Empty(Workspace.EnsureSolutionLoaded().GetDocumentIdsWithFilePath(ExtraPath));
      Assert.Equal(LibProjectText.Replace("""
             <Compile Include="Explicit/Extra.cs" />

         """, "", StringComparison.Ordinal), await File.ReadAllTextAsync(LibProjectPath, CT));
      Assert.Equal(AppProjectText, await File.ReadAllTextAsync(AppProjectPath, CT));
      Assert.False(File.Exists(SharedPath));
      Assert.Empty(Workspace.EnsureSolutionLoaded().GetDocumentIdsWithFilePath(SharedPath));
   }

   /// <returns>The text the tool leaves in App/AppOnly.cs.</returns>
   private async Task<string> RunWriteToolAsync(WriteTool tool)
   {
      switch (tool)
      {
         case WriteTool.EditFile:
            var edited = await new EditFileTool(Workspace).EditFileAsync(
               AppOnlyPath, [new EditOperation { Find = "Abs(2)", Replace = "Abs(6)" }], CT);
            Assert.True(edited.Data.Applied);
            return AppOnlyText.Replace("Abs(2)", "Abs(6)", StringComparison.Ordinal);
         case WriteTool.WriteFile:
            const string written = "namespace App;\n\npublic static class AppOnly\n{\n    public static int Value() => 7;\n}\n";
            var writeResult = await new WriteFileTool(Workspace).WriteFileAsync(AppOnlyPath, written, CT);
            Assert.True(writeResult.Data.Applied);
            Assert.False(writeResult.Data.IsNewFile);
            return written;
         case WriteTool.RenameSymbol:
            var renamed = await new RenameSymbolTool(Workspace, new SymbolLookupService(Workspace)).RenameSymbolAsync(
               new LookupByName { Name = "AppOnly.Value" }, "Number", preview: false, cancellationToken: CT);
            Assert.True(renamed.Data.Applied);
            Assert.Equal(1, renamed.Data.TotalFiles);
            return AppOnlyText.Replace("Value()", "Number()", StringComparison.Ordinal);
         case WriteTool.FormatDocumentBatch:
            var formatted = await new FormatDocumentBatchTool(Workspace).FormatDocumentBatchAsync(
               new FileScope { Path = AppOnlyPath }, preview: false, cancellationToken: CT);
            Assert.True(formatted.Data.Applied);
            Assert.Equal(1, formatted.Data.FilesFormatted);
            return AppOnlyText.Replace("()  =>  Math", "() => Math", StringComparison.Ordinal);
         case WriteTool.OrganizeUsings:
            var organized = await new OrganizeUsingsTool(Workspace).OrganizeUsingsAsync(
               new FileScope { Path = AppOnlyPath }, preview: false, cancellationToken: CT);
            Assert.True(organized.Data.Applied);
            Assert.Equal(1, organized.Data.FilesChanged);
            return AppOnlyText.Replace("using System.Text;\n", "", StringComparison.Ordinal);
         default:
            throw new InvalidOperationException($"Unexpected value: {tool}");
      }
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
}
