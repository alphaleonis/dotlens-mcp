using System.Reflection;
using DotLens.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Text;

namespace DotLens.Tools.Refactoring;

/// <summary>
/// Shared helper methods for refactoring tools.
/// </summary>
internal static class RefactoringHelpers
{
   public static string ToCamelCase(string name)
   {
      if (string.IsNullOrEmpty(name))
         return name;
      return char.ToLowerInvariant(name[0]) + name[1..];
   }

   private static List<CodeRefactoringProvider>? _builtInProviders;

   public static List<CodeRefactoringProvider> GetBuiltInRefactoringProviders()
   {
      if (_builtInProviders != null)
         return _builtInProviders;

      var providers = new List<CodeRefactoringProvider>();
      var refactoringProviderType = typeof(CodeRefactoringProvider);

      // Load from Features assemblies (where refactoring providers are defined)
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
            var assemblyProviders = LoadRefactoringProvidersFromAssembly(assembly, refactoringProviderType);
            providers.AddRange(assemblyProviders);
         }
         catch
         {
            // Assembly not available
         }
      }

      // Also check already loaded assemblies containing CodeRefactoringProvider types
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

            var assemblyProviders = LoadRefactoringProvidersFromAssembly(assembly, refactoringProviderType);
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

   private static IEnumerable<CodeRefactoringProvider> LoadRefactoringProvidersFromAssembly(
      Assembly assembly, Type refactoringProviderType)
   {
      return assembly.GetTypes()
         .Where(t => !t.IsAbstract && refactoringProviderType.IsAssignableFrom(t))
         .Where(t => t.GetConstructors().Any(c => c.GetParameters().Length == 0))
         .Select(t =>
         {
            try
            { return Activator.CreateInstance(t) as CodeRefactoringProvider; }
            catch { return null; }
         })
         .Where(p => p != null)
         .Cast<CodeRefactoringProvider>();
   }

   public static async Task<List<(CodeAction action, string kind)>> GetAllCodeActionsAtPositionAsync(
      Document document,
      int startPosition,
      int? endPosition,
      bool includeCodeFixes,
      bool includeRefactorings,
      CancellationToken cancellationToken)
   {
      var allActions = new List<(CodeAction action, string kind)>();
      var span = endPosition.HasValue
         ? TextSpan.FromBounds(startPosition, endPosition.Value)
         : new TextSpan(startPosition, 0);

      if (includeCodeFixes)
      {
         var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
         var syntaxTree = await document.GetSyntaxTreeAsync(cancellationToken);

         if (semanticModel != null && syntaxTree != null)
         {
            var diagnostics = semanticModel.GetDiagnostics(cancellationToken: cancellationToken)
               .Concat(syntaxTree.GetDiagnostics(cancellationToken))
               .Where(d => d.Location.SourceSpan.IntersectsWith(span))
               .ToList();

            var codeFixProviders = CodeFixHelpers.GetBuiltInCodeFixProviders();
            foreach (var diagnostic in diagnostics)
            {
               cancellationToken.ThrowIfCancellationRequested();
               foreach (var provider in codeFixProviders)
               {
                  if (!provider.FixableDiagnosticIds.Contains(diagnostic.Id))
                     continue;

                  try
                  {
                     var context = new CodeFixContext(
                        document,
                        diagnostic,
                        (action, _) => allActions.Add((action, "fix")),
                        cancellationToken);
                     await provider.RegisterCodeFixesAsync(context);
                  }
                  catch { }
               }
            }
         }
      }

      if (includeRefactorings)
      {
         var refactoringProviders = GetBuiltInRefactoringProviders();
         foreach (var provider in refactoringProviders)
         {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
               var context = new CodeRefactoringContext(
                  document,
                  span,
                  action => allActions.Add((action, "refactoring")),
                  cancellationToken);
               await provider.ComputeRefactoringsAsync(context);
            }
            catch { }
         }
      }

      return allActions;
   }

   public static async Task<List<ChangedFile>> GetChangedFilesAsync(
      Solution oldSolution,
      Solution newSolution,
      bool includeText,
      CancellationToken cancellationToken)
   {
      var changedFiles = new List<ChangedFile>();
      var solutionChanges = newSolution.GetChanges(oldSolution);

      foreach (var projectChanges in solutionChanges.GetProjectChanges())
      {
         cancellationToken.ThrowIfCancellationRequested();
         foreach (var changedDocId in projectChanges.GetChangedDocuments())
         {
            cancellationToken.ThrowIfCancellationRequested();
            var oldDoc = oldSolution.GetDocument(changedDocId);
            var newDoc = newSolution.GetDocument(changedDocId);
            if (oldDoc == null || newDoc == null)
               continue;

            var oldText = await oldDoc.GetTextAsync(cancellationToken);
            var newText = await newDoc.GetTextAsync(cancellationToken);
            var changes = newText.GetTextChanges(oldText).ToList();

            changedFiles.Add(new ChangedFile
            {
               FilePath = newDoc.FilePath,
               ChangeCount = changes.Count,
               NewText = includeText ? newText.ToString() : null
            });
         }
      }

      return changedFiles;
   }
}

/// <summary>
/// Represents a changed file during refactoring.
/// </summary>
internal class ChangedFile
{
   public string? FilePath { get; set; }
   public int ChangeCount { get; set; }
   public string? NewText { get; set; }
}

/// <summary>
/// Represents a signature change request.
/// </summary>
public class SignatureChange
{
   public string Action { get; set; } = ""; // add, remove, rename, reorder
   public string? Name { get; set; }
   public string? Type { get; set; }
   public string? NewName { get; set; }
   public string? DefaultValue { get; set; }
   public int? Position { get; set; }
   public List<string>? Order { get; set; }
}
