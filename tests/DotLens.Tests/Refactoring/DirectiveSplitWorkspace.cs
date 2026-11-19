using DotLens.Infrastructure;
using DotLens.Tools.Editing;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace DotLens.Tests.Refactoring;

/// <summary>
/// A temp copy of the test fixtures, loaded once for the tests of a class, with the namespaces that the using shapes of
/// <see cref="UsingToolsDirectiveSplitTests"/> import and one folder for each combination of the two .editorconfig
/// using options.
/// </summary>
/// <remarks>
/// A test writes its files into the folders and removes them with <see cref="RemoveSourceAsync"/>.
/// <see cref="AssertUnchangedAsync"/> checks that no file and no change to an .editorconfig is left from a test before.
/// </remarks>
public sealed class DirectiveSplitWorkspace : IsolatedProjectTestBase
{
   /// <summary>
   /// The folders under DirectiveSplit, each with the options that its .editorconfig sets.
   /// </summary>
   public static readonly (string Folder, bool SortSystemFirst, bool SeparateGroups)[] Combinations =
   [
      ("SystemFirst", true, false),
      ("SystemFirstSeparated", true, true),
      ("Alphabetical", false, false),
      ("AlphabeticalSeparated", false, true),
   ];

   private string? _baseline;

   public WorkspaceManager Workspace => WorkspaceManager;

   /// <summary>
   /// Returns the absolute path of a file in CoreTestProject.
   /// </summary>
   /// <param name="relativePath">The path relative to the project directory, with '/' separators.</param>
   public string GetPath(string relativePath) => GetCoreTestProjectPath(relativePath);

   public override async ValueTask InitializeAsync()
   {
      await base.InitializeAsync();
      await WriteSourceAsync("DirectiveSplit/DirectiveSplitNamespaces.cs", """
         namespace Alpha
         {
            public class AlphaType { }
            public static class AlphaStatics { public static int AlphaValue => 1; }
         }

         namespace Beta { public class BetaType { } }

         namespace Gamma { public class GammaType { } }

         namespace Juliet { public class JulietType { } }

         namespace Kilo { public class KiloType { } }

         namespace Mike { public class MikeType { } }

         namespace Spare { public class SpareType { } }

         namespace Extra { public class ExtraType { } }

         namespace Zeta
         {
            public class ZetaType { }
            public static class ZetaStatics { public static int ZetaValue => 2; }
         }

         """);

      foreach (var (folder, sortSystemFirst, separateGroups) in Combinations)
         await AddEditorConfigAsync(folder, sortSystemFirst, separateGroups);

      _baseline = await SnapshotAsync();
   }

   /// <summary>
   /// Writes the source and checks that it parses without diagnostics, so a diagnostic in the result comes from the tool.
   /// </summary>
   /// <returns>The absolute path of the file.</returns>
   public async Task<string> WriteSourceAsync(string relativePath, string text)
   {
      Assert.Empty(CSharpSyntaxTree.ParseText(text, cancellationToken: CT).GetDiagnostics());
      var path = GetCoreTestProjectPath(relativePath);
      var written = await new WriteFileTool(WorkspaceManager).WriteFileAsync(path, text, CT);
      Assert.True(written.Data.IsNewFile);
      return path;
   }

   /// <summary>
   /// Removes the file from the workspace and the disk, so its global usings and types do not reach the next file. A
   /// file that the workspace or the disk does not hold is skipped there.
   /// </summary>
   public async Task RemoveSourceAsync(string path)
   {
      if (await WorkspaceManager.TryGetDocumentAsync(path, CT) is { } document)
      {
         using var writeLock = await WorkspaceManager.WriteLock.AcquireAsync(CT);
         WorkspaceManager.UpdateSolution(WorkspaceManager.EnsureSolutionLoaded().RemoveDocument(document.Id));
      }

      File.Delete(path);
   }

   /// <summary>
   /// Checks that CoreTestProject holds the documents and .editorconfig texts it held after initialization, and that the
   /// DirectiveSplit folder on disk holds the same files.
   /// </summary>
   public async Task AssertUnchangedAsync() => Assert.Equal(_baseline, await SnapshotAsync());

   private async Task<string> SnapshotAsync()
   {
      var project = Assert.Single(WorkspaceManager.EnsureSolutionLoaded().Projects, p => p.Name == "CoreTestProject");
      var lines = project.Documents.Select(d => $"document {d.FilePath}").ToList();
      foreach (var config in project.AnalyzerConfigDocuments)
         lines.Add($"editorconfig {config.FilePath}: {await config.GetTextAsync(CT)}");
      lines.AddRange(Directory.GetFiles(GetCoreTestProjectPath("DirectiveSplit"), "*", SearchOption.AllDirectories)
         .Select(f => $"disk {f}"));
      lines.Sort(StringComparer.Ordinal);
      return string.Join("\n", lines);
   }

   /// <summary>
   /// Writes the .editorconfig of the folder and adds it to CoreTestProject, as a load of the solution would.
   /// </summary>
   private async Task AddEditorConfigAsync(string folder, bool sortSystemFirst, bool separateGroups)
   {
      var path = GetCoreTestProjectPath($"DirectiveSplit/{folder}/.editorconfig");
      var text = $"""
         root = true

         [*.cs]
         dotnet_sort_system_directives_first = {(sortSystemFirst ? "true" : "false")}
         dotnet_separate_import_directive_groups = {(separateGroups ? "true" : "false")}

         """;
      Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException(path));
      await File.WriteAllTextAsync(path, text, CT);
      using var writeLock = await WorkspaceManager.WriteLock.AcquireAsync(CT);
      var project = Assert.Single(WorkspaceManager.EnsureSolutionLoaded().Projects, p => p.Name == "CoreTestProject");
      WorkspaceManager.UpdateSolution(
         project.AddAnalyzerConfigDocument(".editorconfig", SourceText.From(text), filePath: path).Project.Solution);
   }
}
