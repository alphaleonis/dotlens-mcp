using DotLens.Tools.Shared;
using FakeItEasy;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DotLens.Tests.Shared;

public class SymbolFilterHelperTests
{
   [Fact]
   public void MatchesModifiers_InvalidModifierFilter_ThrowsArgumentOutOfRange()
   {
      var fakeSymbol = A.Fake<IMethodSymbol>();

      var invalidFilter = (ModifierFilter)999;

      Assert.Throws<ArgumentOutOfRangeException>(() =>
         SymbolFilterHelper.MatchesModifiers(fakeSymbol, [invalidFilter]));
   }
}
