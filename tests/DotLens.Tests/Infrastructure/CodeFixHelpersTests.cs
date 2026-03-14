using DotLens.Infrastructure;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DotLens.Tests.Infrastructure;

public class CodeFixHelpersTests : IsolatedProjectTestBase
{

   [Fact]
   public void GetBuiltInCodeFixProviders_ReturnsListWithoutThrowing()
   {
      // The method should return a list without throwing
      // The list may be empty in test environments where Features assembly isn't loaded
      var providers = CodeFixHelpers.GetBuiltInCodeFixProviders();

      Assert.NotNull(providers);

      // If providers are found, verify they have valid fixable diagnostics
      foreach (var provider in providers)
      {
         Assert.False(provider.FixableDiagnosticIds.IsDefault);
      }
   }

   [Fact]
   public void GetBuiltInCodeFixProviders_IsCached()
   {
      var providers1 = CodeFixHelpers.GetBuiltInCodeFixProviders();
      var providers2 = CodeFixHelpers.GetBuiltInCodeFixProviders();

      // Should return the same list instance (cached)
      Assert.Same(providers1, providers2);
   }

   [Fact]
   public async Task GetCodeFixProvidersForProject_ReturnsListWithoutThrowing()
   {
      var project = WorkspaceManager.Solution!.Projects.First();
      var providers = CodeFixHelpers.GetCodeFixProvidersForProject(project);

      Assert.NotNull(providers);

      // Should include at least as many providers as built-in
      var builtInProviders = CodeFixHelpers.GetBuiltInCodeFixProviders();
      Assert.True(providers.Count >= builtInProviders.Count);
   }

   [Fact]
   public async Task GetCodeFixProvidersForProject_ValidatesProviderProperties()
   {
      var project = WorkspaceManager.Solution!.Projects.First();
      var providers = CodeFixHelpers.GetCodeFixProvidersForProject(project);

      // All providers should have valid fixable diagnostics
      foreach (var provider in providers)
      {
         Assert.False(provider.FixableDiagnosticIds.IsDefault);
      }
   }

   [Fact]
   public void ClearCache_DoesNotThrow()
   {
      // Should not throw when cache is empty
      CodeFixHelpers.ClearCache();

      // Should not throw after providers are loaded
      _ = CodeFixHelpers.GetBuiltInCodeFixProviders();
      CodeFixHelpers.ClearCache();
   }
}
