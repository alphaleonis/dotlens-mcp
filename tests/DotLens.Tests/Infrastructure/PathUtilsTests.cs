using System.Security;
using DotLens.Infrastructure;
using Xunit;

namespace DotLens.Tests.Infrastructure;

public class PathUtilsTests
{
   #region NormalizePath Tests

   [Theory]
   [InlineData(@"C:\Users\test\file.cs", "C:/Users/test/file.cs")]
   [InlineData("src/file.cs", "src/file.cs")]
   [InlineData(@"src\folder\file.cs", "src/folder/file.cs")]
   [InlineData(@"\\server\share\file.cs", "//server/share/file.cs")]
   [InlineData("", "")]
   public void NormalizePath_ReplacesBackslashesWithForwardSlashes(string input, string expected)
   {
      var result = PathUtils.NormalizePath(input);
      Assert.Equal(expected, result);
   }

   #endregion

   #region FormatPath Tests

   [Fact]
   public void FormatPath_WithNullPath_ReturnsEmptyString()
   {
      var result = PathUtils.FormatPath(null, "/some/solution.sln", useAbsolutePaths: false);
      Assert.Equal("", result);
   }

   [Fact]
   public void FormatPath_WithEmptyPath_ReturnsEmptyString()
   {
      var result = PathUtils.FormatPath("", "/some/solution.sln", useAbsolutePaths: false);
      Assert.Equal("", result);
   }

   [Fact]
   public void FormatPath_WithUseAbsolutePaths_ReturnsNormalizedAbsolutePath()
   {
      var absolutePath = Path.Combine(Path.GetTempPath(), "src", "file.cs");
      var result = PathUtils.FormatPath(absolutePath, "/some/solution.sln", useAbsolutePaths: true);

      // Should return the normalized absolute path (forward slashes)
      Assert.DoesNotContain("\\", result);
      Assert.Contains("file.cs", result);
   }

   [Fact]
   public void FormatPath_WithNullSolutionPath_ReturnsNormalizedAbsolutePath()
   {
      var absolutePath = Path.Combine(Path.GetTempPath(), "src", "file.cs");
      var result = PathUtils.FormatPath(absolutePath, null, useAbsolutePaths: false);

      // Without solution path, must return absolute path
      Assert.Contains("file.cs", result);
   }

   #endregion

   #region ValidatePathWithinDirectory Tests

   [Fact]
   public void ValidatePathWithinDirectory_ThrowsOnNullPath()
   {
      Assert.Throws<ArgumentNullException>(() =>
          PathUtils.ValidatePathWithinDirectory(null!, Path.GetTempPath()));
   }

   [Fact]
   public void ValidatePathWithinDirectory_ThrowsOnNullBaseDirectory()
   {
      Assert.Throws<ArgumentNullException>(() =>
          PathUtils.ValidatePathWithinDirectory("some/path.cs", null!));
   }

   [Fact]
   public void ValidatePathWithinDirectory_ThrowsOnEmptyPath()
   {
      // ArgumentException (not ArgumentNullException) is thrown for empty strings
      Assert.Throws<ArgumentException>(() =>
          PathUtils.ValidatePathWithinDirectory("", Path.GetTempPath()));
   }

   [Fact]
   public void ValidatePathWithinDirectory_RejectsUncPaths()
   {
      Assert.Throws<SecurityException>(() =>
          PathUtils.ValidatePathWithinDirectory(@"\\server\share\file.cs", Path.GetTempPath()));
   }

   [Fact]
   public void ValidatePathWithinDirectory_RejectsUncPathsWithForwardSlashes()
   {
      Assert.Throws<SecurityException>(() =>
          PathUtils.ValidatePathWithinDirectory("//server/share/file.cs", Path.GetTempPath()));
   }

   #endregion

   #region WriteFileSafelyAsync Tests

   [Fact]
   public async Task WriteFileSafelyAsync_WritesToNewFile()
   {
      var tempDir = Path.Combine(Path.GetTempPath(), $"PathUtilsTest_{Guid.NewGuid():N}");
      Directory.CreateDirectory(tempDir);
      try
      {
         var filePath = Path.Combine(tempDir, "test.txt");
         var content = "Hello, World!";

         await PathUtils.WriteFileSafelyAsync(filePath, content, tempDir);

         Assert.True(File.Exists(filePath));
         Assert.Equal(content, await File.ReadAllTextAsync(filePath));
      }
      finally
      {
         Directory.Delete(tempDir, recursive: true);
      }
   }

   [Fact]
   public async Task WriteFileSafelyAsync_OverwritesExistingFile()
   {
      var tempDir = Path.Combine(Path.GetTempPath(), $"PathUtilsTest_{Guid.NewGuid():N}");
      Directory.CreateDirectory(tempDir);
      try
      {
         var filePath = Path.Combine(tempDir, "existing.txt");
         await File.WriteAllTextAsync(filePath, "Original content");

         var newContent = "Updated content";
         await PathUtils.WriteFileSafelyAsync(filePath, newContent, tempDir);

         Assert.Equal(newContent, await File.ReadAllTextAsync(filePath));
      }
      finally
      {
         Directory.Delete(tempDir, recursive: true);
      }
   }

   [Fact]
   public async Task WriteFileSafelyAsync_RejectsPathTraversal()
   {
      var tempDir = Path.Combine(Path.GetTempPath(), $"PathUtilsTest_{Guid.NewGuid():N}");
      Directory.CreateDirectory(tempDir);
      try
      {
         var maliciousPath = Path.Combine(tempDir, "..", "outside.txt");

         await Assert.ThrowsAsync<SecurityException>(() =>
            PathUtils.WriteFileSafelyAsync(maliciousPath, "malicious", tempDir));
      }
      finally
      {
         Directory.Delete(tempDir, recursive: true);
      }
   }

   [Fact]
   public async Task WriteFileSafelyAsync_RejectsAbsolutePathOutsideDirectory()
   {
      var tempDir = Path.Combine(Path.GetTempPath(), $"PathUtilsTest_{Guid.NewGuid():N}");
      var otherDir = Path.Combine(Path.GetTempPath(), $"OtherDir_{Guid.NewGuid():N}");
      Directory.CreateDirectory(tempDir);
      Directory.CreateDirectory(otherDir);
      try
      {
         var outsidePath = Path.Combine(otherDir, "outside.txt");

         await Assert.ThrowsAsync<SecurityException>(() =>
            PathUtils.WriteFileSafelyAsync(outsidePath, "content", tempDir));
      }
      finally
      {
         Directory.Delete(tempDir, recursive: true);
         Directory.Delete(otherDir, recursive: true);
      }
   }

   [Fact]
   public async Task WriteFileSafelyAsync_CreatesSubdirectories()
   {
      var tempDir = Path.Combine(Path.GetTempPath(), $"PathUtilsTest_{Guid.NewGuid():N}");
      Directory.CreateDirectory(tempDir);
      try
      {
         var subDir = Path.Combine(tempDir, "sub", "folder");
         Directory.CreateDirectory(subDir);
         var filePath = Path.Combine(subDir, "nested.txt");
         var content = "Nested content";

         await PathUtils.WriteFileSafelyAsync(filePath, content, tempDir);

         Assert.True(File.Exists(filePath));
         Assert.Equal(content, await File.ReadAllTextAsync(filePath));
      }
      finally
      {
         Directory.Delete(tempDir, recursive: true);
      }
   }

   [Fact]
   public async Task WriteFileSafelyAsync_SupportsCancellation()
   {
      var tempDir = Path.Combine(Path.GetTempPath(), $"PathUtilsTest_{Guid.NewGuid():N}");
      Directory.CreateDirectory(tempDir);
      try
      {
         var filePath = Path.Combine(tempDir, "cancelled.txt");
         using var cts = new CancellationTokenSource();
         cts.Cancel();

         // TaskCanceledException is a subclass of OperationCanceledException
         await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PathUtils.WriteFileSafelyAsync(filePath, "content", tempDir, cts.Token));
      }
      finally
      {
         Directory.Delete(tempDir, recursive: true);
      }
   }

   #endregion
}
