using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DotLens.Tests.Infrastructure;

/// <summary>
/// Covers the C# language version that the referenced Roslyn packages parse as the latest.
/// </summary>
public class CSharp15LanguageVersionTests
{
   [Fact]
   public void LatestLanguageVersion_IsCSharp15()
   {
      Assert.Equal(LanguageVersion.CSharp15, LanguageVersion.Latest.MapSpecifiedToEffectiveVersion());
   }
}
