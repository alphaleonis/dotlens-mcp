using System.Diagnostics;
using System.Text;
using DotLens.Infrastructure;
using DotLens.Tools.Editing;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DotLens.Tests.Infrastructure;

/// <summary>
/// Covers how <see cref="WorkspaceManager.ApplyChanges"/> writes added and removed documents to the project files and the disk.
/// </summary>
/// <remarks>
/// Each test writes its own solution to a temp directory, because the tests edit and delete its files.
/// </remarks>
public sealed class ApplyChangesDocumentFilesTests : IAsyncLifetime
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

   private const string AppProjectTextWithoutLink = """
      <Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup>
          <TargetFramework>net10.0</TargetFramework>
        </PropertyGroup>
      </Project>
      """;

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

   private const string LibProjectTextWithListed = """
      <Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup>
          <TargetFramework>net10.0</TargetFramework>
        </PropertyGroup>
        <ItemGroup>
          <Compile Remove="Explicit/**" />
          <Compile Include="Explicit/Extra.cs" />
          <Compile Include="Explicit/Listed.cs" />
        </ItemGroup>
      </Project>
      """;

   private const string ImpProjectText = """
      <Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup>
          <TargetFramework>net10.0</TargetFramework>
        </PropertyGroup>
        <ItemGroup>
          <Compile Include="../Shared/ListedA.cs;../Shared/ListedB.cs" />
        </ItemGroup>
      </Project>
      """;

   private const string ImpBuildPropsText = """
      <Project>
        <PropertyGroup>
          <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
        </PropertyGroup>
        <ItemGroup>
          <Compile Include="../Shared/Imported.cs" />
          <Compile Include="Inner.cs" />
        </ItemGroup>
      </Project>
      """;

   private const string MtProjectText = """
      <Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup>
          <TargetFrameworks>net10.0;net11.0</TargetFrameworks>
        </PropertyGroup>
        <ItemGroup Condition="'$(TargetFramework)' != 'net10.0'">
          <Compile Remove="Only10*.cs" />
        </ItemGroup>
        <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
          <Compile Include="../Shared/Polyfill.cs" />
        </ItemGroup>
        <ItemGroup>
          <Compile Include="../Shared/Linked.cs" />
        </ItemGroup>
      </Project>
      """;

   private const string MtProjectTextWithReference = """
      <Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup>
          <TargetFrameworks>net10.0;net11.0</TargetFrameworks>
        </PropertyGroup>
        <ItemGroup Condition="'$(TargetFramework)' != 'net10.0'">
          <Compile Remove="Only10*.cs" />
        </ItemGroup>
        <ItemGroup>
          <Compile Include="../Shared/Linked.cs" />
        </ItemGroup>
        <ItemGroup>
          <ProjectReference Include="../One/One.csproj">
            <Name>One</Name>
          </ProjectReference>
        </ItemGroup>
      </Project>
      """;

   private const string AdditionHint = "Write the files and call reload_solution, or add them to the project file first.";

   private const string OneProjectText = """
      <Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup>
          <TargetFrameworks>net10.0</TargetFrameworks>
        </PropertyGroup>
      </Project>
      """;

   private const string PropProjectText = """
      <Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup>
          <TargetFramework>net10.0</TargetFramework>
        </PropertyGroup>
        <ItemGroup>
          <Compile Include="$(MSBuildThisFileDirectory)../Shared/FromProperty.cs" />
        </ItemGroup>
      </Project>
      """;

   private const string AppOnlyText = "namespace App;\n\npublic static class AppOnly\n{\n    public static int Value() => Math.Abs(2);\n}\n";

   private const string RefusalHint =
      "Remove the document from every project that compiles it, or edit the project file and call reload_solution.";

   private string? _tempDir;
   private WorkspaceManager? _workspaceManager;

   public enum RefusedRemoval
   {
      GlobCopyStillLinked,
      Imported,
      ImportedFromProjectDirectory,
      ListedWithOtherFiles,
      ListedThroughProperty,
   }

   private static CancellationToken CT => TestContext.Current.CancellationToken;

   private string TempDir => _tempDir ?? throw new InvalidOperationException("Test not initialized");

   private WorkspaceManager Workspace => _workspaceManager ?? throw new InvalidOperationException("Test not initialized");

   private string AppProjectPath => Path.Combine(TempDir, "App", "App.csproj");

   private string LibProjectPath => Path.Combine(TempDir, "Lib", "Lib.csproj");

   private string ImpProjectPath => Path.Combine(TempDir, "Imp", "Imp.csproj");

   private string PropProjectPath => Path.Combine(TempDir, "Prop", "Prop.csproj");

   private string SharedPath => Path.Combine(TempDir, "Lib", "Shared.cs");

   private string GlobPath => Path.Combine(TempDir, "Lib", "Glob.cs");

   private string AppOnlyPath => Path.Combine(TempDir, "App", "AppOnly.cs");

   private string InnerPath => Path.Combine(TempDir, "Imp", "Inner.cs");

   private string MtProjectPath => Path.Combine(TempDir, "Mt", "Mt.csproj");

   private string OneProjectPath => Path.Combine(TempDir, "One", "One.csproj");

   private string[] ProjectPaths => [AppProjectPath, LibProjectPath, ImpProjectPath, PropProjectPath, MtProjectPath, OneProjectPath];

   private string SolutionPath => Path.Combine(TempDir, "RemoveDocument.slnx");

   public async ValueTask InitializeAsync()
   {
      _tempDir = Path.Combine(Path.GetTempPath(), $"DotLensRemoveDocumentTest_{Guid.NewGuid():N}");
      foreach (var dir in new[] { "App", "Lib/Explicit", "Imp", "Prop", "Shared", "Mt", "One" })
         Directory.CreateDirectory(Path.Combine(_tempDir, dir));

      // Outside the repo, MSBuild would otherwise build the solution with the newest installed SDK.
      File.Copy(Path.Combine(FindRepoRoot(), "global.json"), Path.Combine(_tempDir, "global.json"));

      await File.WriteAllTextAsync(AppProjectPath, AppProjectText, CT);
      await File.WriteAllTextAsync(LibProjectPath, LibProjectText, CT);
      await File.WriteAllTextAsync(ImpProjectPath, ImpProjectText, CT);
      await File.WriteAllTextAsync(Path.Combine(_tempDir, "Imp", "Directory.Build.props"), ImpBuildPropsText, CT);
      await File.WriteAllTextAsync(PropProjectPath, PropProjectText, CT);
      await File.WriteAllTextAsync(AppOnlyPath, AppOnlyText, CT);
      await File.WriteAllTextAsync(SharedPath, ClassText("Lib", "Shared"), CT);
      await File.WriteAllTextAsync(GlobPath, ClassText("Lib", "Glob"), CT);
      await File.WriteAllTextAsync(Path.Combine(_tempDir, "Lib", "Explicit", "Extra.cs"), ClassText("Lib", "Extra"), CT);
      await File.WriteAllTextAsync(InnerPath, ClassText("Imp", "Inner"), CT);
      await File.WriteAllTextAsync(MtProjectPath, MtProjectText, CT);
      await File.WriteAllTextAsync(OneProjectPath, OneProjectText, CT);
      await File.WriteAllTextAsync(Path.Combine(_tempDir, "Mt", "Both.cs"), ClassText("Mt", "Both"), CT);
      await File.WriteAllTextAsync(Path.Combine(_tempDir, "Mt", "Only10.cs"), ClassText("Mt", "Only10"), CT);
      await File.WriteAllTextAsync(Path.Combine(_tempDir, "One", "OneGlob.cs"), ClassText("One", "OneGlob"), CT);
      foreach (var name in new[] { "Imported", "ListedA", "ListedB", "FromProperty", "Polyfill", "Linked" })
         await File.WriteAllTextAsync(SharedFilePath(name), ClassText("Shared", name), CT);

      await File.WriteAllTextAsync(SolutionPath, """
         <Solution>
           <Project Path="Lib/Lib.csproj" />
           <Project Path="App/App.csproj" />
           <Project Path="Imp/Imp.csproj" />
           <Project Path="Prop/Prop.csproj" />
           <Project Path="Mt/Mt.csproj" />
           <Project Path="One/One.csproj" />
         </Solution>
         """, CT);

      await TestFixtureRestore.EnsureRestoredAsync(SolutionPath);
      _workspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance, Options.Create(new DotLensOptions()));
      await _workspaceManager.LoadSolutionAsync(SolutionPath, CT);
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
   public async Task ApplyChanges_RemoveDocument_LinkedCopy_RemovesItemAndKeepsSharedFile()
   {
      var sharedBytes = await File.ReadAllBytesAsync(SharedPath, CT);
      var libProjectBytes = await File.ReadAllBytesAsync(LibProjectPath, CT);

      Assert.True(await ApplyRemovalAsync(SharedPath, "App"));

      Assert.Equal(AppProjectTextWithoutLink, await File.ReadAllTextAsync(AppProjectPath, CT));
      Assert.Equal(libProjectBytes, await File.ReadAllBytesAsync(LibProjectPath, CT));
      Assert.Equal(sharedBytes, await File.ReadAllBytesAsync(SharedPath, CT));
      Assert.Equal(["Lib"], ProjectNamesCompiling(SharedPath));

      await AssertLaterWriteChangesOnlyItsFileAsync();
      Assert.Equal(sharedBytes, await File.ReadAllBytesAsync(SharedPath, CT));
      Assert.Equal(["Lib"], ProjectNamesCompiling(SharedPath));
   }

   [Fact]
   public async Task ApplyChanges_RemoveDocument_GlobIncluded_DeletesFileWithoutEditingProject()
   {
      var projectBytes = await ReadProjectBytesAsync();

      Assert.True(await ApplyRemovalAsync(GlobPath, "Lib"));

      Assert.False(File.Exists(GlobPath));
      Assert.Empty(ProjectNamesCompiling(GlobPath));
      Assert.Equal(projectBytes, await ReadProjectBytesAsync());

      await AssertLaterWriteChangesOnlyItsFileAsync();
      Assert.Equal(projectBytes, await ReadProjectBytesAsync());
      Assert.False(File.Exists(GlobPath));
   }

   [Fact]
   public async Task ApplyChanges_RemoveDocument_EveryCopy_RemovesItemAndDeletesFile()
   {
      var libProjectBytes = await File.ReadAllBytesAsync(LibProjectPath, CT);

      Assert.True(await ApplyRemovalAsync(SharedPath, "App", "Lib"));

      Assert.False(File.Exists(SharedPath));
      Assert.Empty(ProjectNamesCompiling(SharedPath));
      Assert.Equal(AppProjectTextWithoutLink, await File.ReadAllTextAsync(AppProjectPath, CT));
      Assert.Equal(libProjectBytes, await File.ReadAllBytesAsync(LibProjectPath, CT));

      await AssertLaterWriteChangesOnlyItsFileAsync();
      Assert.Equal(AppProjectTextWithoutLink, await File.ReadAllTextAsync(AppProjectPath, CT));
      Assert.Equal(libProjectBytes, await File.ReadAllBytesAsync(LibProjectPath, CT));
   }

   [Theory]
   [InlineData(RefusedRemoval.GlobCopyStillLinked)]
   [InlineData(RefusedRemoval.Imported)]
   [InlineData(RefusedRemoval.ImportedFromProjectDirectory)]
   [InlineData(RefusedRemoval.ListedWithOtherFiles)]
   [InlineData(RefusedRemoval.ListedThroughProperty)]
   public async Task ApplyChanges_RemoveDocument_Refused_LeavesDiskAndSolutionUnchanged(RefusedRemoval refused)
   {
      var (path, project, expectedMessage) = refused switch
      {
         RefusedRemoval.GlobCopyStillLinked => (SharedPath, "Lib",
            $"Cannot remove {Workspace.FormatPath(SharedPath)} from project Lib: the project includes it by a wildcard, and project App still compiles it."),
         RefusedRemoval.Imported => (SharedFilePath("Imported"), "Imp",
            $"Cannot remove {Workspace.FormatPath(SharedFilePath("Imported"))} from project Imp: a file that the project imports includes it."),
         RefusedRemoval.ImportedFromProjectDirectory => (InnerPath, "Imp",
            $"Cannot remove {Workspace.FormatPath(InnerPath)} from project Imp: a file that the project imports includes it."),
         RefusedRemoval.ListedWithOtherFiles => (SharedFilePath("ListedA"), "Imp",
            $"Cannot remove {Workspace.FormatPath(SharedFilePath("ListedA"))} from project Imp: the project file lists it in one Compile item with other files."),
         RefusedRemoval.ListedThroughProperty => (SharedFilePath("FromProperty"), "Prop",
            $"Cannot remove {Workspace.FormatPath(SharedFilePath("FromProperty"))} from project Prop: the project file has a Compile item that DotLens cannot evaluate."),
         _ => throw new InvalidOperationException($"Unexpected value: {refused}"),
      };
      var projectBytes = await ReadProjectBytesAsync();
      var fileBytes = await File.ReadAllBytesAsync(path, CT);
      var solution = Workspace.EnsureSolutionLoaded();
      var compilingBefore = ProjectNamesCompiling(path);

      var ex = await Assert.ThrowsAsync<ToolException>(() => ApplyRemovalAsync(path, project));

      Assert.Equal(ErrorCodes.ApplyChangesFailed, ex.Code);
      Assert.Equal(expectedMessage, ex.Message);
      Assert.Equal(RefusalHint, ex.Hint);
      Assert.Same(solution, Workspace.EnsureSolutionLoaded());
      Assert.Equal(compilingBefore, ProjectNamesCompiling(path));
      Assert.Equal(fileBytes, await File.ReadAllBytesAsync(path, CT));
      Assert.Equal(projectBytes, await ReadProjectBytesAsync());

      await AssertLaterWriteChangesOnlyItsFileAsync();
      Assert.Equal(fileBytes, await File.ReadAllBytesAsync(path, CT));
      Assert.Equal(projectBytes, await ReadProjectBytesAsync());
   }

   [Fact]
   public async Task ApplyChanges_RemoveDocument_ProjectFileWithDeclaredEncoding_KeepsItsBytes()
   {
      var windows1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)
         ?? throw new InvalidOperationException("windows-1252 is not available");
      const string declaration = "<?xml version=\"1.0\" encoding=\"windows-1252\"?>\n<!-- café -->\n";
      await File.WriteAllBytesAsync(AppProjectPath, windows1252.GetBytes(declaration + AppProjectText), CT);

      Assert.True(await ApplyRemovalAsync(SharedPath, "App"));

      Assert.Equal(windows1252.GetBytes(declaration + AppProjectTextWithoutLink), await File.ReadAllBytesAsync(AppProjectPath, CT));
      Assert.True(File.Exists(SharedPath));
   }

   [Fact]
   public async Task ApplyChanges_AddDocument_GlobIncluded_LeavesProjectFileUnchanged()
   {
      var projectBytes = await ReadProjectBytesAsync();
      var addedPath = Path.Combine(TempDir, "Lib", "Added.cs");
      var addedText = ClassText("Lib", "Added");

      Assert.True(await ApplyAdditionAsync("Lib", "Added.cs", [], addedPath, addedText));

      Assert.Equal(addedText, await File.ReadAllTextAsync(addedPath, CT));
      Assert.Equal(["Lib"], ProjectNamesCompiling(addedPath));
      Assert.Equal(projectBytes, await ReadProjectBytesAsync());
      Assert.Equal((0, ""), await RunCheckForDuplicateItemsAsync(LibProjectPath));

      await AssertLaterWriteChangesOnlyItsFileAsync();
      Assert.Equal(projectBytes, await ReadProjectBytesAsync());
      Assert.Equal(["Lib"], ProjectNamesCompiling(addedPath));
   }

   // The workspace saves the project file it edits with a UTF-8 byte order mark.
   private static byte[] LibProjectBytesWithListed => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(LibProjectTextWithListed)];

   [Fact]
   public async Task ApplyChanges_AddDocument_OutsideGlob_ListsItInProjectFile()
   {
      var addedPath = Path.Combine(TempDir, "Lib", "Explicit", "Listed.cs");
      var addedText = ClassText("Lib", "Listed");

      Assert.True(await ApplyAdditionAsync("Lib", "Listed.cs", ["Explicit"], addedPath, addedText));

      Assert.Equal(addedText, await File.ReadAllTextAsync(addedPath, CT));
      Assert.Equal(["Lib"], ProjectNamesCompiling(addedPath));
      Assert.Equal(LibProjectBytesWithListed, await File.ReadAllBytesAsync(LibProjectPath, CT));
      Assert.Equal((0, ""), await RunCheckForDuplicateItemsAsync(LibProjectPath));

      await AssertLaterWriteChangesOnlyItsFileAsync();
      Assert.Equal(LibProjectBytesWithListed, await File.ReadAllBytesAsync(LibProjectPath, CT));
   }

   [Fact]
   public async Task ApplyChanges_AddDocument_GlobIncludedAndListed_RemovesOnlyTheDuplicateItem()
   {
      var globPath = Path.Combine(TempDir, "Lib", "Added.cs");
      var listedPath = Path.Combine(TempDir, "Lib", "Explicit", "Listed.cs");
      var solution = Workspace.EnsureSolutionLoaded();
      var lib = Assert.Single(solution.Projects, p => p.Name == "Lib");
      var added = solution
         .AddDocument(DocumentId.CreateNewId(lib.Id), "Added.cs", SourceText.From(ClassText("Lib", "Added"), Encoding.UTF8), [], globPath)
         .AddDocument(DocumentId.CreateNewId(lib.Id), "Listed.cs", SourceText.From(ClassText("Lib", "Listed"), Encoding.UTF8), ["Explicit"], listedPath);

      bool applied;
      using (await Workspace.WriteLock.AcquireAsync(CT))
         applied = Workspace.ApplyChanges(added, CT);

      Assert.True(applied);
      Assert.Equal(ClassText("Lib", "Added"), await File.ReadAllTextAsync(globPath, CT));
      Assert.Equal(ClassText("Lib", "Listed"), await File.ReadAllTextAsync(listedPath, CT));
      Assert.Equal(LibProjectBytesWithListed, await File.ReadAllBytesAsync(LibProjectPath, CT));
      Assert.Equal((0, ""), await RunCheckForDuplicateItemsAsync(LibProjectPath));
   }

   [Fact]
   public async Task ApplyChanges_AddDocument_ProjectNotEvaluable_Refuses()
   {
      const string brokenProjectText = """<Project Sdk="DotLens.Missing.Sdk" />""";
      await File.WriteAllTextAsync(LibProjectPath, brokenProjectText, CT);
      var solution = Workspace.EnsureSolutionLoaded();
      var addedPath = Path.Combine(TempDir, "Lib", "Added.cs");

      var ex = await Assert.ThrowsAsync<ToolException>(
         () => ApplyAdditionAsync("Lib", "Added.cs", [], addedPath, ClassText("Lib", "Added")));

      Assert.Equal(ErrorCodes.ApplyChangesFailed, ex.Code);
      Assert.Equal("Cannot add documents to project Lib: MSBuild could not evaluate the project.", ex.Message);
      Assert.Equal(AdditionHint, ex.Hint);
      Assert.Same(solution, Workspace.EnsureSolutionLoaded());
      Assert.False(File.Exists(addedPath));
      Assert.Equal(brokenProjectText, await File.ReadAllTextAsync(LibProjectPath, CT));
   }

   private async Task<bool> ApplyAdditionAsync(string projectName, string name, string[] folders, string path, string text)
   {
      var solution = Workspace.EnsureSolutionLoaded();
      var project = Assert.Single(solution.Projects, p => p.Name == projectName);
      var added = solution.AddDocument(DocumentId.CreateNewId(project.Id), name, SourceText.From(text, Encoding.UTF8), folders, path);

      using (await Workspace.WriteLock.AcquireAsync(CT))
         return Workspace.ApplyChanges(added, CT);
   }

   /// <returns>The exit code, and the NETSDK1022 errors that the SDK's duplicate check reports.</returns>
   private static async Task<(int ExitCode, string Errors)> RunCheckForDuplicateItemsAsync(string projectPath, string? targetFramework = null)
   {
      var startInfo = new ProcessStartInfo("dotnet")
      {
         WorkingDirectory = Path.GetDirectoryName(projectPath) ?? "",
         RedirectStandardOutput = true,
         RedirectStandardError = true,
         UseShellExecute = false,
      };
      foreach (var arg in new[] { "msbuild", projectPath, "-t:CheckForDuplicateItems", "-nologo" })
         startInfo.ArgumentList.Add(arg);
      if (targetFramework is not null)
         startInfo.ArgumentList.Add("-p:TargetFramework=" + targetFramework);

      using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start 'dotnet msbuild'");
      var outputTask = process.StandardOutput.ReadToEndAsync(CT);
      var errorTask = process.StandardError.ReadToEndAsync(CT);
      await process.WaitForExitAsync(CT);
      var output = await outputTask + await errorTask;
      var errors = string.Join("\n", output.Split('\n').Where(line => line.Contains("NETSDK1022", StringComparison.Ordinal)));
      return (process.ExitCode, errors);
   }

   [Fact]
   public async Task ApplyChanges_RemoveDocument_MultiTarget_ListedForOneFramework_RemovesItsItemGroup()
   {
      var polyfillPath = SharedFilePath("Polyfill");

      Assert.True(await ApplyRemovalAsync(polyfillPath, "Mt(net10.0)"));

      Assert.False(File.Exists(polyfillPath));
      Assert.Empty(ProjectNamesCompiling(polyfillPath));
      Assert.Equal(MtProjectText.Replace("""
           <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
             <Compile Include="../Shared/Polyfill.cs" />
           </ItemGroup>

         """, "", StringComparison.Ordinal), await File.ReadAllTextAsync(MtProjectPath, CT));
   }

   [Theory]
   [InlineData("Both.cs", new[] { "Mt(net10.0)", "Mt(net11.0)" })]
   [InlineData("Only10.cs", new[] { "Mt(net10.0)" })]
   public async Task ApplyChanges_RemoveDocument_MultiTarget_GlobIncluded_DeletesFileWithoutEditingProject(string fileName, string[] projectNames)
   {
      var path = Path.Combine(TempDir, "Mt", fileName);
      Assert.Equal(projectNames, ProjectNamesCompiling(path));
      var projectBytes = await ReadProjectBytesAsync();

      Assert.True(await ApplyRemovalAsync(path, projectNames));

      Assert.False(File.Exists(path));
      Assert.Empty(ProjectNamesCompiling(path));
      Assert.Equal(projectBytes, await ReadProjectBytesAsync());
   }

   [Fact]
   public async Task ApplyChanges_RemoveDocument_MultiTarget_ListedForEveryFramework_RemovesItemAndDeletesFile()
   {
      var linkedPath = SharedFilePath("Linked");
      Assert.Equal(["Mt(net10.0)", "Mt(net11.0)"], ProjectNamesCompiling(linkedPath));

      Assert.True(await ApplyRemovalAsync(linkedPath, "Mt(net10.0)", "Mt(net11.0)"));

      Assert.False(File.Exists(linkedPath));
      Assert.Equal(MtProjectText.Replace("""
           <ItemGroup>
             <Compile Include="../Shared/Linked.cs" />
           </ItemGroup>

         """, "", StringComparison.Ordinal), await File.ReadAllTextAsync(MtProjectPath, CT));
   }

   [Fact]
   public async Task ApplyChanges_RemoveDocument_MultiTarget_OneOfTwoFrameworks_Refuses()
   {
      var bothPath = Path.Combine(TempDir, "Mt", "Both.cs");
      var projectBytes = await ReadProjectBytesAsync();
      var solution = Workspace.EnsureSolutionLoaded();

      var ex = await Assert.ThrowsAsync<ToolException>(() => ApplyRemovalAsync(bothPath, "Mt(net10.0)"));

      Assert.Equal(
         $"Cannot remove {Workspace.FormatPath(bothPath)} from project Mt(net10.0): project Mt(net11.0) has the same project file and still compiles it.",
         ex.Message);
      Assert.Same(solution, Workspace.EnsureSolutionLoaded());
      Assert.True(File.Exists(bothPath));
      Assert.Equal(projectBytes, await ReadProjectBytesAsync());
   }

   [Fact]
   public async Task ApplyChanges_RemoveDocument_SingleEntryTargetFrameworks_DeletesGlobFile()
   {
      var path = Path.Combine(TempDir, "One", "OneGlob.cs");
      Assert.Equal(["One"], ProjectNamesCompiling(path));
      var projectBytes = await ReadProjectBytesAsync();

      Assert.True(await ApplyRemovalAsync(path, "One"));

      Assert.False(File.Exists(path));
      Assert.Equal(projectBytes, await ReadProjectBytesAsync());
   }

   [Fact]
   public async Task ApplyChanges_RemoveDocument_ResponseFile_IsNotRead()
   {
      // Read by the command line, -p:EnableDefaultCompileItems=false would leave the glob file out of the evaluation.
      await File.WriteAllTextAsync(Path.Combine(TempDir, "Lib", "Directory.Build.rsp"), "-bl\n-warnaserror\n-p:EnableDefaultCompileItems=false\n", CT);
      var projectBytes = await ReadProjectBytesAsync();

      Assert.True(await ApplyRemovalAsync(GlobPath, "Lib"));

      Assert.False(File.Exists(GlobPath));
      Assert.Equal(projectBytes, await ReadProjectBytesAsync());
      Assert.Equal(["Directory.Build.rsp", "Lib.csproj", "Shared.cs"], Directory.GetFiles(Path.Combine(TempDir, "Lib")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
   }

   [Fact]
   public void ApplyChanges_RemoveDocument_EvaluationBudgetSpent_Refuses()
   {
      var solution = Workspace.EnsureSolutionLoaded();
      var globId = Assert.Single(solution.GetDocumentIdsWithFilePath(GlobPath));

      var ex = Assert.Throws<ToolException>(
         () => DocumentChangePlan.Create(solution, solution.RemoveDocument(globId), Workspace.FormatPath, TimeSpan.Zero, CT));

      Assert.Equal(
         $"Cannot remove {Workspace.FormatPath(GlobPath)} from project Lib: MSBuild could not evaluate the project.", ex.Message);
      Assert.True(File.Exists(GlobPath));
   }

   [Fact]
   public void ApplyChanges_RemoveDocument_EvaluationHost_IsTheWorkspaceSdk()
   {
      var lib = Assert.Single(Workspace.EnsureSolutionLoaded().Projects, p => p.Name == "Lib");

      var host = DotnetHost.Find(lib);

      var sdkDirectory = Assert.IsType<string>(host.SdkDirectory);
      Assert.Equal(Path.GetFullPath(Path.Combine(sdkDirectory, "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet")), host.Path);
      Assert.True(File.Exists(Path.Combine(sdkDirectory, "Sdks", "Microsoft.NET.Sdk", "targets", "Microsoft.NET.Sdk.DefaultItems.props")));
      var items = new EvaluatedCompileItems(TimeSpan.FromMinutes(1), CT).Get(lib, reuse: false);
      Assert.Equal(
         [Path.Combine(TempDir, "Lib", "Explicit", "Extra.cs"), GlobPath, SharedPath],
         items.Select(i => i.FullPath).Order(StringComparer.Ordinal));
   }

   [Theory]
   [InlineData("Mt(net10.0);Mt(net11.0)")]
   [InlineData("Mt(net10.0)")]
   public async Task ApplyChanges_AddDocument_MultiTarget_GlobIncluded_LeavesProjectFileUnchanged(string addingProjects)
   {
      var projectNames = addingProjects.Split(';');
      var projectBytes = await ReadProjectBytesAsync();
      var addedPath = Path.Combine(TempDir, "Mt", "Added.cs");

      Assert.True(await ApplyAdditionToProjectsAsync(projectNames, "Added.cs", addedPath, ClassText("Mt", "Added")));

      Assert.Equal(ClassText("Mt", "Added"), await File.ReadAllTextAsync(addedPath, CT));
      Assert.Equal(projectNames, ProjectNamesCompiling(addedPath));
      Assert.Equal(projectBytes, await ReadProjectBytesAsync());
      Assert.Equal((0, ""), await RunCheckForDuplicateItemsAsync(MtProjectPath, "net10.0"));
      Assert.Equal((0, ""), await RunCheckForDuplicateItemsAsync(MtProjectPath, "net11.0"));
   }

   [Fact]
   public async Task ApplyChanges_AddDocument_MultiTarget_NeededInOneFrameworkDuplicateInAnother_Refuses()
   {
      var addedPath = Path.Combine(TempDir, "Mt", "Only10Added.cs");
      var projectBytes = await ReadProjectBytesAsync();
      var solution = Workspace.EnsureSolutionLoaded();

      var ex = await Assert.ThrowsAsync<ToolException>(
         () => ApplyAdditionToProjectsAsync(["Mt(net11.0)"], "Only10Added.cs", addedPath, ClassText("Mt", "Only10Added")));

      Assert.Equal(ConflictMessage(addedPath), ex.Message);
      Assert.Equal(AdditionHint, ex.Hint);
      Assert.Same(solution, Workspace.EnsureSolutionLoaded());
      Assert.False(File.Exists(addedPath));
      Assert.Equal(projectBytes, await ReadProjectBytesAsync());
   }

   /// <summary>
   /// The removal alone and the addition alone each succeed, so the refused addition must also hold back the removal.
   /// </summary>
   [Fact]
   public async Task ApplyChanges_AddDocument_RefusedWithARemoval_WritesNothing()
   {
      var addedPath = Path.Combine(TempDir, "Mt", "Only10Added.cs");
      var polyfillPath = SharedFilePath("Polyfill");
      var projectBytes = await ReadProjectBytesAsync();
      var polyfillBytes = await File.ReadAllBytesAsync(polyfillPath, CT);
      var solution = Workspace.EnsureSolutionLoaded();
      var changed = solution.RemoveDocuments([.. solution.GetDocumentIdsWithFilePath(polyfillPath)]);
      var mt11 = Assert.Single(changed.Projects, p => p.Name == "Mt(net11.0)");
      changed = changed.AddDocument(
         DocumentId.CreateNewId(mt11.Id), "Only10Added.cs", SourceText.From(ClassText("Mt", "Only10Added"), Encoding.UTF8), [], addedPath);

      ToolException ex;
      using (await Workspace.WriteLock.AcquireAsync(CT))
         ex = Assert.Throws<ToolException>(() => Workspace.ApplyChanges(changed, CT));

      Assert.Equal(ConflictMessage(addedPath), ex.Message);
      Assert.Same(solution, Workspace.EnsureSolutionLoaded());
      Assert.Equal(polyfillBytes, await File.ReadAllBytesAsync(polyfillPath, CT));
      Assert.False(File.Exists(addedPath));
      Assert.Equal(projectBytes, await ReadProjectBytesAsync());

      await AssertLaterWriteChangesOnlyItsFileAsync();
      Assert.Equal(projectBytes, await ReadProjectBytesAsync());
   }

   /// <summary>
   /// One change removes a file listed for net10.0, adds a glob file to net10.0, and adds a project reference to
   /// net11.0 only, which the project file must keep.
   /// </summary>
   [Fact]
   public async Task ApplyChanges_AddDocument_WithRemovalAndReferenceAcrossFrameworks_WritesEachChange()
   {
      var addedPath = Path.Combine(TempDir, "Mt", "Added.cs");
      var polyfillPath = SharedFilePath("Polyfill");
      var solution = Workspace.EnsureSolutionLoaded();
      var changed = solution.RemoveDocuments([.. solution.GetDocumentIdsWithFilePath(polyfillPath)]);
      var mt10 = Assert.Single(changed.Projects, p => p.Name == "Mt(net10.0)");
      var mt11 = Assert.Single(changed.Projects, p => p.Name == "Mt(net11.0)");
      var one = Assert.Single(changed.Projects, p => p.Name == "One");
      changed = changed
         .AddDocument(DocumentId.CreateNewId(mt10.Id), "Added.cs", SourceText.From(ClassText("Mt", "Added"), Encoding.UTF8), [], addedPath)
         .AddProjectReference(mt11.Id, new ProjectReference(one.Id));

      bool applied;
      using (await Workspace.WriteLock.AcquireAsync(CT))
         applied = Workspace.ApplyChanges(changed, CT);

      Assert.True(applied);
      // The workspace saves the project file with a byte order mark when it adds the reference.
      byte[] expectedBytes = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(MtProjectTextWithReference)];
      Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(MtProjectPath, CT));
      Assert.False(File.Exists(polyfillPath));
      Assert.Empty(ProjectNamesCompiling(polyfillPath));
      Assert.Equal(ClassText("Mt", "Added"), await File.ReadAllTextAsync(addedPath, CT));
      Assert.Equal(["Mt(net10.0)"], ProjectNamesCompiling(addedPath));
      Assert.Equal([one.Id], Workspace.EnsureSolutionLoaded().GetProject(mt11.Id)?.ProjectReferences.Select(r => r.ProjectId));
      Assert.Equal((0, ""), await RunCheckForDuplicateItemsAsync(MtProjectPath, "net10.0"));
      Assert.Equal((0, ""), await RunCheckForDuplicateItemsAsync(MtProjectPath, "net11.0"));
   }

   /// <summary>
   /// App.csproj is read-only, so its edit fails, while the deletion of Lib/Glob.cs in the same change still runs.
   /// </summary>
   [Fact]
   public async Task ApplyChanges_RemoveDocument_WriteFails_RunsTheOtherWritesAndReportsIt()
   {
      var solution = Workspace.EnsureSolutionLoaded();
      var appSharedId = Assert.Single(solution.GetDocumentIdsWithFilePath(SharedPath), id => solution.GetProject(id.ProjectId)?.Name == "App");
      var globId = Assert.Single(solution.GetDocumentIdsWithFilePath(GlobPath));
      File.SetAttributes(AppProjectPath, FileAttributes.ReadOnly);

      ToolException ex;
      using (await Workspace.WriteLock.AcquireAsync(CT))
         ex = Assert.Throws<ToolException>(() => Workspace.ApplyChanges(solution.RemoveDocuments([appSharedId, globId]), CT));

      File.SetAttributes(AppProjectPath, FileAttributes.Normal);
      Assert.Equal(
         $"The changes were applied to the solution, but these files could not be updated: {Workspace.FormatPath(AppProjectPath)} (Access to the path '{AppProjectPath}' is denied.)",
         ex.Message);
      Assert.False(File.Exists(GlobPath));
      Assert.Equal(AppProjectText, await File.ReadAllTextAsync(AppProjectPath, CT));
      Assert.Equal(["Lib"], ProjectNamesCompiling(SharedPath));
   }

   [Fact]
   public void ApplyChanges_RemoveDocument_EvaluationHost_IgnoresSdkPathOutsideKnownInstallations()
   {
      var plantedSdk = Path.Combine(TempDir, "Lib", "evil", "sdk", "1");
      Directory.CreateDirectory(Path.Combine(plantedSdk, "Sdks", "X"));
      File.WriteAllText(Path.Combine(TempDir, "Lib", "evil", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"), "");
      var lib = Assert.Single(Workspace.EnsureSolutionLoaded().Projects, p => p.Name == "Lib");
      var planted = lib.AnalyzerConfigDocuments.Aggregate(lib.WithAnalyzerReferences([]), (p, d) => p.RemoveAnalyzerConfigDocument(d.Id))
         .AddAnalyzerConfigDocument("a.globalconfig", SourceText.From(""), filePath: Path.Combine(plantedSdk, "Sdks", "X", "a.globalconfig"))
         .Project;

      var host = DotnetHost.Find(planted);

      Assert.Null(host.SdkDirectory);
      Assert.Equal(DotnetHost.Find(planted, []).Path, host.Path);
      Assert.DoesNotContain(TempDir, host.Path, StringComparison.Ordinal);
   }

   [Fact]
   public async Task ApplyChanges_AddDocument_MultiTarget_OutsideGlobToEveryFramework_ListsItOnce()
   {
      var addedPath = SharedFilePath("Twice");
      var solution = Workspace.EnsureSolutionLoaded();
      foreach (var name in new[] { "Mt(net10.0)", "Mt(net11.0)" })
      {
         var project = Assert.Single(solution.Projects, p => p.Name == name);
         solution = solution.AddDocument(
            DocumentId.CreateNewId(project.Id), "Twice.cs", SourceText.From(ClassText("Shared", "Twice"), Encoding.UTF8), ["..", "Shared"], addedPath);
      }

      bool applied;
      using (await Workspace.WriteLock.AcquireAsync(CT))
         applied = Workspace.ApplyChanges(solution, CT);

      Assert.True(applied);
      Assert.Equal(ClassText("Shared", "Twice"), await File.ReadAllTextAsync(addedPath, CT));
      Assert.Equal(["Mt(net10.0)", "Mt(net11.0)"], ProjectNamesCompiling(addedPath));
      byte[] expectedBytes = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(MtProjectText.Replace(
         "    <Compile Include=\"../Shared/Linked.cs\" />\n",
         "    <Compile Include=\"../Shared/Linked.cs\" />\n    <Compile Include=\"../Shared/Twice.cs\" />\n",
         StringComparison.Ordinal))];
      Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(MtProjectPath, CT));
      Assert.Equal((0, ""), await RunCheckForDuplicateItemsAsync(MtProjectPath, "net10.0"));
      Assert.Equal((0, ""), await RunCheckForDuplicateItemsAsync(MtProjectPath, "net11.0"));
   }

   /// <summary>
   /// Glob.cs comes before Explicit/Extra.cs in Lib, so the workspace writes Glob.cs and then fails on the read-only
   /// Extra.cs.
   /// </summary>
   [Fact]
   public async Task ApplyChanges_Recovery_TryApplyChangesThrowsPartway_RestoresDiskAndBothSolutions()
   {
      var extraPath = Path.Combine(TempDir, "Lib", "Explicit", "Extra.cs");
      var solution = Workspace.EnsureSolutionLoaded();
      var lib = Assert.Single(solution.Projects, p => p.Name == "Lib");
      var globId = Assert.Single(solution.GetDocumentIdsWithFilePath(GlobPath));
      var extraId = Assert.Single(solution.GetDocumentIdsWithFilePath(extraPath));
      Assert.True(lib.DocumentIds.ToList().IndexOf(globId) < lib.DocumentIds.ToList().IndexOf(extraId));
      var globBytes = await File.ReadAllBytesAsync(GlobPath, CT);
      var extraBytes = await File.ReadAllBytesAsync(extraPath, CT);
      var globWrittenAt = File.GetLastWriteTimeUtc(GlobPath);
      var projectBytes = await ReadProjectBytesAsync();
      var changed = solution
         .WithDocumentText(globId, SourceText.From(ClassText("Lib", "GlobChanged"), Encoding.UTF8))
         .WithDocumentText(extraId, SourceText.From(ClassText("Lib", "ExtraChanged"), Encoding.UTF8));
      File.SetAttributes(extraPath, FileAttributes.ReadOnly);

      ToolException ex;
      try
      {
         using (await Workspace.WriteLock.AcquireAsync(CT))
            ex = Assert.Throws<ToolException>(() => Workspace.ApplyChanges(changed, CT));
      }
      finally
      {
         File.SetAttributes(extraPath, FileAttributes.Normal);
      }

      Assert.Equal(ErrorCodes.ApplyChangesFailed, ex.Code);
      Assert.Equal(
         $"The workspace could not apply the changes, so DotLens restored the files it had written: Access to the path '{extraPath}' is denied.",
         ex.Message);
      Assert.Equal("Fix the cause, then try again.", ex.Hint);
      // The restore wrote Glob.cs again, which shows that the workspace had written it before it failed.
      Assert.NotEqual(globWrittenAt, File.GetLastWriteTimeUtc(GlobPath));
      Assert.Equal(globBytes, await File.ReadAllBytesAsync(GlobPath, CT));
      Assert.Equal(extraBytes, await File.ReadAllBytesAsync(extraPath, CT));
      Assert.Equal(projectBytes, await ReadProjectBytesAsync());
      Assert.Same(solution, Workspace.EnsureSolutionLoaded());
      Assert.Same(solution, Workspace.WorkspaceSolution);

      await AssertLaterWriteChangesOnlyItsFileAsync();
      Assert.Equal(globBytes, await File.ReadAllBytesAsync(GlobPath, CT));
      Assert.Equal(extraBytes, await File.ReadAllBytesAsync(extraPath, CT));
   }

   /// <summary>
   /// The added file is outside Lib's glob and lists it in Lib.csproj, so the write would change the project file too.
   /// </summary>
   [Theory]
   [InlineData(false)]
   [InlineData(true)]
   public async Task ApplyChanges_Recovery_Canceled_WritesNothing(bool duringEvaluation)
   {
      var addedPath = Path.Combine(TempDir, "Lib", "Explicit", "Listed.cs");
      var projectBytes = await ReadProjectBytesAsync();
      var solution = Workspace.EnsureSolutionLoaded();
      var lib = Assert.Single(solution.Projects, p => p.Name == "Lib");
      var added = solution.AddDocument(
         DocumentId.CreateNewId(lib.Id), "Listed.cs", SourceText.From(ClassText("Lib", "Listed"), Encoding.UTF8), ["Explicit"], addedPath);
      using var cts = CancellationTokenSource.CreateLinkedTokenSource(CT);
      var sawPlaceholder = false;
      Task cancelTask;
      if (duringEvaluation)
      {
         // The placeholder exists only while MSBuild evaluates the project.
         cancelTask = Task.Run(async () =>
         {
            while (!File.Exists(addedPath))
               await Task.Delay(1, CT);
            sawPlaceholder = true;
            await cts.CancelAsync();
         }, CT);
      }
      else
      {
         await cts.CancelAsync();
         cancelTask = Task.CompletedTask;
      }

      using (await Workspace.WriteLock.AcquireAsync(CT))
         Assert.Throws<OperationCanceledException>(() => Workspace.ApplyChanges(added, cts.Token));
      await cancelTask;

      Assert.Equal(duringEvaluation, sawPlaceholder);
      Assert.False(File.Exists(addedPath));
      Assert.Equal(projectBytes, await ReadProjectBytesAsync());
      Assert.Empty(Directory.GetFiles(Path.Combine(TempDir, "Lib", "obj"), "DotLens.placeholders.*"));
      Assert.Same(solution, Workspace.EnsureSolutionLoaded());
      Assert.Same(solution, Workspace.WorkspaceSolution);

      await AssertLaterWriteChangesOnlyItsFileAsync();
      Assert.False(File.Exists(addedPath));
   }

   /// <summary>
   /// The journal and the placeholder are what an evaluation leaves when its process ends before it deletes them.
   /// Kept.cs is listed too, but holds a user's text.
   /// </summary>
   [Fact]
   public async Task ApplyChanges_Recovery_LeftoverPlaceholder_IsDeletedOnLoad()
   {
      var leftoverDirectory = Path.Combine(TempDir, "Lib", "New");
      var leftoverPath = Path.Combine(leftoverDirectory, "Left.cs");
      var keptPath = Path.Combine(TempDir, "Lib", "Kept.cs");
      var journalPath = Path.Combine(TempDir, "Lib", "obj", $"DotLens.placeholders.{Guid.NewGuid():N}.json");
      Directory.CreateDirectory(leftoverDirectory);
      await File.WriteAllTextAsync(leftoverPath, EvaluationPlaceholders.Content, CT);
      await File.WriteAllTextAsync(keptPath, ClassText("Lib", "Kept"), CT);
      var journal = new System.Text.Json.Nodes.JsonObject
      {
         ["files"] = new System.Text.Json.Nodes.JsonArray(leftoverPath, keptPath),
         ["directories"] = new System.Text.Json.Nodes.JsonArray(leftoverDirectory),
      };
      await File.WriteAllTextAsync(journalPath, journal.ToJsonString(), CT);
      var projectBytes = await ReadProjectBytesAsync();

      await Workspace.LoadSolutionAsync(SolutionPath, CT);

      Assert.False(File.Exists(leftoverPath));
      Assert.False(Directory.Exists(leftoverDirectory));
      Assert.False(File.Exists(journalPath));
      Assert.Equal(ClassText("Lib", "Kept"), await File.ReadAllTextAsync(keptPath, CT));
      Assert.Empty(ProjectNamesCompiling(leftoverPath));
      Assert.Equal(["Lib"], ProjectNamesCompiling(keptPath));
      Assert.Equal(projectBytes, await ReadProjectBytesAsync());
      Assert.Same(Workspace.EnsureSolutionLoaded(), Workspace.WorkspaceSolution);

      await AssertLaterWriteChangesOnlyItsFileAsync();
      Assert.False(File.Exists(leftoverPath));
   }

   /// <summary>
   /// The placeholders belong to an evaluation that is still in progress, as in another DotLens process.
   /// </summary>
   [Fact]
   public async Task ApplyChanges_Recovery_LivePlaceholder_IsKeptOnLoad()
   {
      var livePath = Path.Combine(TempDir, "Lib", "Live", "Live.cs");

      using (EvaluationPlaceholders.Create(Path.Combine(TempDir, "Lib"), [livePath],
         reason => new ToolException(ErrorCodes.ApplyChangesFailed, reason), Workspace.FormatPath))
      {
         var journals = Directory.GetFiles(Path.Combine(TempDir, "Lib", "obj"), "DotLens.placeholders.*");

         await Workspace.LoadSolutionAsync(SolutionPath, CT);

         Assert.Equal(EvaluationPlaceholders.Content, await File.ReadAllTextAsync(livePath, CT));
         Assert.Equal(journals, Directory.GetFiles(Path.Combine(TempDir, "Lib", "obj"), "DotLens.placeholders.*"));
         Assert.Single(journals);
      }

      Assert.False(File.Exists(livePath));
      Assert.False(Directory.Exists(Path.Combine(TempDir, "Lib", "Live")));
      Assert.Empty(Directory.GetFiles(Path.Combine(TempDir, "Lib", "obj"), "DotLens.placeholders.*"));
   }

   [Fact]
   public async Task ApplyChanges_Recovery_ChangeNotSupported_ReportsFilesUnchanged()
   {
      var solution = Workspace.EnsureSolutionLoaded();
      var lib = Assert.Single(solution.Projects, p => p.Name == "Lib");
      var globId = Assert.Single(solution.GetDocumentIdsWithFilePath(GlobPath));
      var parseOptions = Assert.IsType<CSharpParseOptions>(lib.ParseOptions);
      var globBytes = await File.ReadAllBytesAsync(GlobPath, CT);
      var globWrittenAt = File.GetLastWriteTimeUtc(GlobPath);
      var changed = solution
         .WithDocumentText(globId, SourceText.From(ClassText("Lib", "GlobChanged"), Encoding.UTF8))
         .WithProjectParseOptions(lib.Id, parseOptions.WithLanguageVersion(LanguageVersion.CSharp7));

      ToolException ex;
      using (await Workspace.WriteLock.AcquireAsync(CT))
         ex = Assert.Throws<ToolException>(() => Workspace.ApplyChanges(changed, CT));

      Assert.Equal(ErrorCodes.ApplyChangesFailed, ex.Code);
      Assert.Equal("The workspace could not apply the changes and left every file unchanged: Changing parse options is not supported.", ex.Message);
      Assert.Equal("Fix the cause, then try again.", ex.Hint);
      Assert.Equal(globWrittenAt, File.GetLastWriteTimeUtc(GlobPath));
      Assert.Equal(globBytes, await File.ReadAllBytesAsync(GlobPath, CT));
      Assert.Same(solution, Workspace.EnsureSolutionLoaded());
      Assert.Same(solution, Workspace.WorkspaceSolution);
   }

   /// <summary>
   /// A file named obj stands where the journal's directory belongs, so the journal cannot be written.
   /// </summary>
   [Fact]
   public async Task ApplyChanges_Recovery_JournalNotWritable_RefusesNamingTheDirectory()
   {
      var objPath = Path.Combine(TempDir, "Lib", "obj");
      Directory.Move(objPath, objPath + "-moved");
      await File.WriteAllTextAsync(objPath, "", CT);
      var addedPath = Path.Combine(TempDir, "Lib", "Explicit", "Listed.cs");
      var projectBytes = await ReadProjectBytesAsync();
      var solution = Workspace.EnsureSolutionLoaded();

      var ex = await Assert.ThrowsAsync<ToolException>(
         () => ApplyAdditionAsync("Lib", "Listed.cs", ["Explicit"], addedPath, ClassText("Lib", "Listed")));

      Assert.Equal(ErrorCodes.ApplyChangesFailed, ex.Code);
      Assert.Equal(
         $"Cannot add documents to project Lib: the journal of its placeholder files could not be written to {Workspace.FormatPath(objPath)} "
            + $"(The file '{objPath}' already exists.).",
         ex.Message);
      Assert.Equal(AdditionHint, ex.Hint);
      Assert.False(File.Exists(addedPath));
      Assert.Equal(projectBytes, await ReadProjectBytesAsync());
      Assert.Same(solution, Workspace.EnsureSolutionLoaded());
   }

   /// <summary>
   /// The journal lists a placeholder and an empty directory outside the solution, as a journal that a repository ships
   /// can.
   /// </summary>
   [Fact]
   public async Task ApplyChanges_Recovery_LeftoverJournal_IgnoresPathsOutsideTheSolution()
   {
      var outside = Path.Combine(Path.GetTempPath(), $"DotLensOutside_{Guid.NewGuid():N}");
      var outsideDirectory = Path.Combine(outside, "Empty");
      var outsidePath = Path.Combine(outside, "Left.cs");
      var insidePath = Path.Combine(TempDir, "Shared", "Left.cs");
      Directory.CreateDirectory(outsideDirectory);
      try
      {
         await File.WriteAllTextAsync(outsidePath, EvaluationPlaceholders.Content, CT);
         await File.WriteAllTextAsync(insidePath, EvaluationPlaceholders.Content, CT);
         var journalPath = Path.Combine(TempDir, "Lib", "obj", $"DotLens.placeholders.{Guid.NewGuid():N}.json");
         var journal = new System.Text.Json.Nodes.JsonObject
         {
            ["files"] = new System.Text.Json.Nodes.JsonArray(outsidePath, insidePath),
            ["directories"] = new System.Text.Json.Nodes.JsonArray(outsideDirectory),
         };
         await File.WriteAllTextAsync(journalPath, journal.ToJsonString(), CT);

         await Workspace.LoadSolutionAsync(SolutionPath, CT);

         Assert.Equal(EvaluationPlaceholders.Content, await File.ReadAllTextAsync(outsidePath, CT));
         Assert.True(Directory.Exists(outsideDirectory));
         Assert.False(File.Exists(insidePath));
         Assert.False(File.Exists(journalPath));
      }
      finally
      {
         Directory.Delete(outside, recursive: true);
      }
   }

   /// <summary>
   /// MSBuildLocator, which picks the SDK that loads a project, also finds dotnet through DOTNET_HOST_PATH and
   /// DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR.
   /// </summary>
   [Fact]
   public void ApplyChanges_Recovery_EvaluationHost_KnowsMSBuildLocatorInstallations()
   {
      var executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
      var hostRoot = Path.Combine(TempDir, "host-root");
      var cliRoot = Path.Combine(TempDir, "cli-root");
      var linkedRoot = Path.Combine(TempDir, "linked-root");
      foreach (var root in new[] { hostRoot, cliRoot, linkedRoot })
         Directory.CreateDirectory(Path.Combine(root, "sdk", "1", "Sdks", "X"));
      File.WriteAllText(Path.Combine(hostRoot, executable), "");
      File.WriteAllText(Path.Combine(linkedRoot, executable), "");
      // MSBuildLocator resolves the link of the dotnet in DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR except on Windows.
      if (OperatingSystem.IsWindows())
         File.WriteAllText(Path.Combine(cliRoot, executable), "");
      else
         File.CreateSymbolicLink(Path.Combine(cliRoot, executable), Path.Combine(linkedRoot, executable));

      var environment = new Dictionary<string, string>
      {
         ["DOTNET_HOST_PATH"] = Path.Combine(hostRoot, executable),
         ["DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR"] = cliRoot,
      };
      var installations = DotnetHost.KnownInstallations(name => environment.GetValueOrDefault(name));

      string[] expected = OperatingSystem.IsWindows()
         ? [.. DotnetHost.KnownInstallations(_ => null), hostRoot, cliRoot]
         : [.. DotnetHost.KnownInstallations(_ => null), hostRoot, cliRoot, linkedRoot];
      Assert.Equal(expected, installations);
      var lib = Assert.Single(Workspace.EnsureSolutionLoaded().Projects, p => p.Name == "Lib");
      var planted = lib.AnalyzerConfigDocuments.Aggregate(lib.WithAnalyzerReferences([]), (p, d) => p.RemoveAnalyzerConfigDocument(d.Id))
         .AddAnalyzerConfigDocument("a.globalconfig", SourceText.From(""), filePath: Path.Combine(hostRoot, "sdk", "1", "Sdks", "X", "a.globalconfig"))
         .Project;
      Assert.Equal(new DotnetHost(Path.Combine(hostRoot, executable), Path.Combine(hostRoot, "sdk", "1")), DotnetHost.Find(planted, installations));
   }

   private string ConflictMessage(string addedPath) =>
      $"Cannot add documents to project Mt(net11.0): {Workspace.FormatPath(addedPath)} needs a Compile item, "
      + "but project Mt(net10.0) already includes it, so its build would report NETSDK1022.";

   private async Task<bool> ApplyAdditionToProjectsAsync(string[] projectNames, string name, string path, string text)
   {
      var solution = Workspace.EnsureSolutionLoaded();
      foreach (var projectName in projectNames)
      {
         var project = Assert.Single(solution.Projects, p => p.Name == projectName);
         solution = solution.AddDocument(DocumentId.CreateNewId(project.Id), name, SourceText.From(text, Encoding.UTF8), [], path);
      }

      using (await Workspace.WriteLock.AcquireAsync(CT))
         return Workspace.ApplyChanges(solution, CT);
   }

   private async Task<bool> ApplyRemovalAsync(string path, params string[] projectNames)
   {
      var solution = Workspace.EnsureSolutionLoaded();
      var ids = solution.GetDocumentIdsWithFilePath(path)
         .Where(id => projectNames.Contains(solution.GetProject(id.ProjectId)?.Name))
         .ToList();
      Assert.Equal(projectNames.Length, ids.Count);

      using (await Workspace.WriteLock.AcquireAsync(CT))
         return Workspace.ApplyChanges(solution.RemoveDocuments([.. ids]), CT);
   }

   /// <summary>
   /// An edit to an unrelated file writes only that file when DotLens's solution and the MSBuildWorkspace's agree.
   /// </summary>
   private async Task AssertLaterWriteChangesOnlyItsFileAsync()
   {
      var edited = await new EditFileTool(Workspace).EditFileAsync(
         AppOnlyPath, [new EditOperation { Find = "Abs(2)", Replace = "Abs(6)" }], CT);

      Assert.True(edited.Data.Applied);
      Assert.Equal(AppOnlyText.Replace("Abs(2)", "Abs(6)", StringComparison.Ordinal), await File.ReadAllTextAsync(AppOnlyPath, CT));
   }

   private List<string> ProjectNamesCompiling(string path)
   {
      var solution = Workspace.EnsureSolutionLoaded();
      return [.. solution.GetDocumentIdsWithFilePath(path)
         .Select(id => solution.GetProject(id.ProjectId)?.Name ?? "")
         .Order(StringComparer.Ordinal)];
   }

   private async Task<List<byte[]>> ReadProjectBytesAsync()
   {
      var bytes = new List<byte[]>();
      foreach (var path in ProjectPaths)
         bytes.Add(await File.ReadAllBytesAsync(path, CT));
      return bytes;
   }

   private string SharedFilePath(string name) => Path.Combine(TempDir, "Shared", name + ".cs");

   private static string ClassText(string ns, string name) => $"namespace {ns};\n\npublic static class {name}\n{{\n}}\n";

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
