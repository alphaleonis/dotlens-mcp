using DotLens.Infrastructure;
using Xunit;

namespace DotLens.Tests.Infrastructure;

public class GlobMatcherTests
{
   [Theory]
   [InlineData("*", "anything")]
   [InlineData("*.cs", "MyClass.cs")]
   [InlineData("*.cs", "MYCLASS.CS")] // Case insensitive
   [InlineData("My*", "MyClass")]
   [InlineData("*Class", "MyClass")]
   [InlineData("*Class*", "MyClassHelper")]
   [InlineData("?yClass", "MyClass")]
   [InlineData("M?Class", "MyClass")]
   [InlineData("MyClass", "MyClass")]
   [InlineData("", "anything")] // Empty matches all
   [InlineData(null, "anything")] // Null matches all
   public void IsMatch_MatchesExpectedPatterns(string? pattern, string input)
   {
      var matcher = new GlobMatcher(pattern);
      Assert.True(matcher.IsMatch(input));
   }

   [Theory]
   [InlineData("*.cs", "MyClass.txt")]
   [InlineData("My*", "YourClass")]
   [InlineData("?yClass", "AnyClass")] // ? matches exactly one char
   [InlineData("MyClass", "MyClass2")]
   [InlineData("MyClass", "my")] // Not a complete match
   public void IsMatch_DoesNotMatchNonMatchingPatterns(string pattern, string input)
   {
      var matcher = new GlobMatcher(pattern);
      Assert.False(matcher.IsMatch(input));
   }

   [Fact]
   public void IsMatch_EscapesRegexSpecialCharacters()
   {
      // These characters have special meaning in regex but should be literal in glob
      var matcher = new GlobMatcher("My[Class].cs");
      Assert.True(matcher.IsMatch("My[Class].cs"));
      Assert.False(matcher.IsMatch("MyClass.cs")); // Not a regex character class
   }

   [Fact]
   public void IsMatch_HandlesDotInPattern()
   {
      // Dot is special in regex (matches any char) but should be literal in glob
      var matcher = new GlobMatcher("file.txt");
      Assert.True(matcher.IsMatch("file.txt"));
      Assert.False(matcher.IsMatch("fileXtxt")); // . should not match any char
   }

   [Fact]
   public void IsMatch_HandlesMultipleWildcards()
   {
      var matcher = new GlobMatcher("*My*Class*");
      Assert.True(matcher.IsMatch("AnyMyHelperClassHere"));
      Assert.True(matcher.IsMatch("MyClass"));
      Assert.False(matcher.IsMatch("AnyOtherThing"));
   }

   [Fact]
   public void IsMatch_IsReusable()
   {
      var matcher = new GlobMatcher("*.cs");

      // Same matcher can be used multiple times
      Assert.True(matcher.IsMatch("File1.cs"));
      Assert.True(matcher.IsMatch("File2.cs"));
      Assert.False(matcher.IsMatch("File.txt"));
   }
}
