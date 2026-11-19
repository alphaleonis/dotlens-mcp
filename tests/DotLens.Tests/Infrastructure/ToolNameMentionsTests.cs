using System.Reflection;
using System.Text.RegularExpressions;
using DotLens.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ModelContextProtocol.Server;
using Xunit;

namespace DotLens.Tests.Infrastructure;

/// <summary>
/// Checks that each snake_case token in a src/DotLens string that starts like a registered tool name (get_, find_, ...) is a registered tool name.
/// </summary>
public partial class ToolNameMentionsTests
{
   private static readonly SyntaxKind[] StringTextKinds =
   [
      SyntaxKind.StringLiteralToken,
      SyntaxKind.Utf8StringLiteralToken,
      SyntaxKind.SingleLineRawStringLiteralToken,
      SyntaxKind.MultiLineRawStringLiteralToken,
      SyntaxKind.Utf8SingleLineRawStringLiteralToken,
      SyntaxKind.Utf8MultiLineRawStringLiteralToken,
      SyntaxKind.InterpolatedStringTextToken,
   ];

   [Fact]
   public void ToolNameMentions_NameRegisteredTools()
   {
      var toolNames = typeof(DotLensWorkerToolAttribute).Assembly.GetTypes()
         .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
         .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
         .OfType<string>()
         .ToHashSet(StringComparer.Ordinal);
      var prefixes = toolNames.Select(FirstSegment).ToHashSet(StringComparer.Ordinal);
      Assert.NotEmpty(toolNames);

      var mentions = SourceFiles()
         .SelectMany(path => StringTokens(path).SelectMany(token =>
            SnakeCaseToken().Matches(token.ValueText)
               .Select(match => match.Value)
               .Where(name => prefixes.Contains(FirstSegment(name)))
               .Select(name => (File: Path.GetFileName(path), Line: token.GetLocation().GetLineSpan().StartLinePosition.Line + 1, Name: name))))
         .ToList();

      Assert.NotEmpty(mentions);
      Assert.Equal([], mentions.Where(m => !toolNames.Contains(m.Name)).Select(m => $"{m.File}:{m.Line}: {m.Name}"));
   }

   private static string FirstSegment(string name) => name.Split('_')[0];

   private static IEnumerable<SyntaxToken> StringTokens(string path)
   {
      var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path), new CSharpParseOptions(LanguageVersion.Preview), path);
      return tree.GetRoot().DescendantTokens().Where(token => StringTextKinds.Contains(token.Kind()));
   }

   private static IEnumerable<string> SourceFiles()
   {
      var sourceDir = Path.Combine(FindRepoRoot(), "src", "DotLens");
      var buildOutputDirs = new[] { Path.Combine(sourceDir, "bin"), Path.Combine(sourceDir, "obj") };
      return Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories)
         .Where(path => !buildOutputDirs.Any(dir => path.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.Ordinal)));
   }

   private static string FindRepoRoot()
   {
      for (var dir = Directory.GetCurrentDirectory(); dir is not null; dir = Directory.GetParent(dir)?.FullName)
      {
         if (File.Exists(Path.Combine(dir, "DotLens.slnx")))
            return dir;
      }

      throw new InvalidOperationException("Could not find the repository root containing DotLens.slnx");
   }

   [GeneratedRegex("(?<![A-Za-z0-9_])[a-z][a-z0-9]*(?:_[a-z0-9]+)+(?![A-Za-z0-9_])")]
   private static partial Regex SnakeCaseToken();
}
