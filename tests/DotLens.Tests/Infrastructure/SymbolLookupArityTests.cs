using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tests.Helpers;
using DotLens.Tools.Editing;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DotLens.Tests.Infrastructure;

/// <summary>
/// Covers name lookups among candidates that differ in their type-parameter counts, in their parameters, in their
/// assembly, or in more than one of these, declared in a file written to a copy of CoreTestProject.
/// </summary>
public class SymbolLookupArityTests : IsolatedProjectTestBase
{
   private const string Source = """
      namespace ArityLookup
      {
         public static class Forge
         {
            public static int Cast(int size) => size;

            public static T? Cast<T>(int size) => default;

            public static string Cast(string name) => name;
         }

         public class Shell
         {
            public class Core { }
         }

         public class Shell<T>
         {
            public class Core { }
         }

         public class Pallet<T>
         {
            public void Stow(T item) { }
         }

         public class Pallet
         {
            public void Stow<T>(T item) { }
         }
      }

      namespace InheritanceTestProject
      {
         public interface IRepository<T> { }
      }

      """;

   [Fact]
   public async Task LookupByName_TypeParameterCountZero_PrefersOnlyTheVariantOfTheSameCandidate()
   {
      var service = await WriteSourceAsync();

      Assert.Equal(["ArityLookup.Forge.Cast(int)"], await FindAsync(service, "Forge.Cast", ["int"]));
      Assert.Equal(["ArityLookup.Forge.Cast<T>(int)"], await FindAsync(service, "Forge.Cast<T>", null));
      Assert.Equal(["ArityLookup.Shell.Core"], await FindAsync(service, "Shell.Core", null));
      Assert.Equal(["ArityLookup.Shell.Core"], await FindAsync(service, "ArityLookup.Shell.Core", null));
      Assert.Equal(["ArityLookup.Shell<T>.Core"], await FindAsync(service, "Shell<T>.Core", null));
      Assert.Equal(["ArityLookup.Pallet.Stow<T>(T)"], await FindAsync(service, "Pallet.Stow<T>", null));
      Assert.Equal(["InheritanceTestProject.IRepository<T>"], await FindAsync(service, "InheritanceTestProject.IRepository<T>", null));
   }

   /// <summary>
   /// Cast(string) has no generic variant, and a bare Core lists no container. Pallet&lt;T&gt;.Stow and Pallet.Stow&lt;T&gt;
   /// have their type parameters on different levels. CoreTestProject declares InheritanceTestProject.IRepository&lt;T&gt;
   /// beside InheritanceTestProject's own IRepository.
   /// </summary>
   [Theory]
   [InlineData("Forge.Cast", new[] { "ArityLookup.Forge.Cast(int)", "ArityLookup.Forge.Cast(string)" })]
   [InlineData("Core", new[] { "ArityLookup.Shell.Core", "ArityLookup.Shell<T>.Core" })]
   [InlineData("Stow", new[] { "ArityLookup.Pallet<T>.Stow(T)", "ArityLookup.Pallet.Stow<T>(T)" })]
   [InlineData("Pallet.Stow", new[] { "ArityLookup.Pallet<T>.Stow(T)", "ArityLookup.Pallet.Stow<T>(T)" })]
   [InlineData("IRepository", new[] { "CoreTestProject.Types.IRepository<T>", "InheritanceTestProject.IRepository<T>", "InheritanceTestProject.IRepository at InheritanceTestProject/InheritanceFixtures.cs:81:18" })]
   [InlineData("InheritanceTestProject.IRepository", new[] { "InheritanceTestProject.IRepository<T>", "InheritanceTestProject.IRepository at InheritanceTestProject/InheritanceFixtures.cs:81:18" })]
   public async Task LookupByName_AmbiguousCandidates_EachListedCandidateFindsItself(string name, string[] expected)
   {
      var service = await WriteSourceAsync();

      await AmbiguityRoundTrip.AssertEachListedCandidateFindsItselfAsync(service, new LookupByName { Name = name }, expected, CT);
   }

   private async Task<SymbolLookupService> WriteSourceAsync()
   {
      var written = await new WriteFileTool(WorkspaceManager).WriteFileAsync(GetCoreTestProjectPath("ArityLookup/Forge.cs"), Source, CT);
      Assert.True(written.Data.IsNewFile);
      return new SymbolLookupService(WorkspaceManager);
   }

   private async Task<List<string>> FindAsync(SymbolLookupService service, string name, string[]? parameterTypes)
   {
      var results = await service.FindAsync<ISymbol>(new LookupByName { Name = name, ParameterTypes = parameterTypes }, cancellationToken: CT);
      return results.Select(r => r.Symbol.ToDisplayString()).ToList();
   }
}
