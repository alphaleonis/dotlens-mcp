using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DotLens.Infrastructure;

/// <summary>
/// Shared helper methods for working with Roslyn CodeFixProviders.
/// </summary>
internal static class CodeFixHelpers
{
   private static readonly ConcurrentDictionary<string, List<CodeFixProvider>> _analyzerCache = new();
   private static List<CodeFixProvider>? _builtInProviders;

   /// <summary>
   /// Gets all built-in CodeFixProviders from the Roslyn assemblies.
   /// </summary>
   public static List<CodeFixProvider> GetBuiltInCodeFixProviders()
   {
      if (_builtInProviders != null)
         return _builtInProviders;

      var providers = new List<CodeFixProvider>();
      var codeFixProviderType = typeof(CodeFixProvider);

      // Try to load from Features assemblies (where code fixes are defined)
      var assemblyNames = new[]
      {
         "Microsoft.CodeAnalysis.CSharp.Features",
         "Microsoft.CodeAnalysis.Features"
      };

      foreach (var assemblyName in assemblyNames)
      {
         try
         {
            var assembly = Assembly.Load(assemblyName);
            var assemblyProviders = LoadProvidersFromAssembly(assembly, codeFixProviderType);
            providers.AddRange(assemblyProviders);
         }
         catch
         {
            // Assembly not available
         }
      }

      // Also check already loaded assemblies containing CodeFixProvider types
      foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
      {
         try
         {
            // Skip assemblies we've already processed
            if (assemblyNames.Contains(assembly.GetName().Name))
               continue;

            // Only check Microsoft.CodeAnalysis assemblies
            var name = assembly.GetName().Name;
            if (name == null || !name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))
               continue;

            var assemblyProviders = LoadProvidersFromAssembly(assembly, codeFixProviderType);
            providers.AddRange(assemblyProviders);
         }
         catch
         {
            // Skip assemblies that can't be inspected
         }
      }

      _builtInProviders = providers.DistinctBy(p => p.GetType().FullName).ToList();
      return _builtInProviders;
   }

   private static IEnumerable<CodeFixProvider> LoadProvidersFromAssembly(Assembly assembly, Type codeFixProviderType)
   {
      return assembly.GetTypes()
         .Where(t => !t.IsAbstract && codeFixProviderType.IsAssignableFrom(t))
         .Where(t => t.GetConstructors().Any(c => c.GetParameters().Length == 0))
         .Select(t =>
         {
            try
            { return Activator.CreateInstance(t) as CodeFixProvider; }
            catch { return null; }
         })
         .Where(p => p != null)
         .Cast<CodeFixProvider>();
   }

   /// <summary>
   /// Gets all CodeFixProviders for a project, including built-in and third-party analyzers.
   /// </summary>
   public static List<CodeFixProvider> GetCodeFixProvidersForProject(Project project)
   {
      var providers = new List<CodeFixProvider>(GetBuiltInCodeFixProviders());

      foreach (var analyzerRef in project.AnalyzerReferences)
      {
         if (analyzerRef is AnalyzerFileReference fileRef)
         {
            var cacheKey = fileRef.FullPath;
            if (_analyzerCache.TryGetValue(cacheKey, out var cachedProviders))
            {
               providers.AddRange(cachedProviders);
               continue;
            }

            var analyzerProviders = LoadCodeFixProvidersFromAssembly(fileRef.FullPath);
            _analyzerCache[cacheKey] = analyzerProviders;
            providers.AddRange(analyzerProviders);
         }
      }

      return providers;
   }

   /// <summary>
   /// Loads CodeFixProviders from an analyzer assembly.
   /// </summary>
   private static List<CodeFixProvider> LoadCodeFixProvidersFromAssembly(string assemblyPath)
   {
      var providers = new List<CodeFixProvider>();

      try
      {
         if (!File.Exists(assemblyPath))
            return providers;

         var assembly = Assembly.LoadFrom(assemblyPath);
         var codeFixProviderType = typeof(CodeFixProvider);

         foreach (var type in assembly.GetTypes())
         {
            if (type.IsAbstract || !codeFixProviderType.IsAssignableFrom(type))
               continue;

            // Check for parameterless constructor
            if (!type.GetConstructors().Any(c => c.GetParameters().Length == 0))
               continue;

            try
            {
               if (Activator.CreateInstance(type) is CodeFixProvider provider)
               {
                  providers.Add(provider);
               }
            }
            catch
            {
               // Skip providers that fail to instantiate
            }
         }
      }
      catch
      {
         // Assembly load failure - return empty list
      }

      return providers;
   }

   /// <summary>
   /// Clears the analyzer cache. Call when solution is unloaded.
   /// </summary>
   public static void ClearCache()
   {
      _analyzerCache.Clear();
   }
}
