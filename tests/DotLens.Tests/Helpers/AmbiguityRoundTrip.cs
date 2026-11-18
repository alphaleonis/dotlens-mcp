using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DotLens.Tests.Helpers;

/// <summary>
/// Checks the ambiguity error of a name lookup against <see cref="SymbolLookupService.AmbiguityHint"/>.
/// </summary>
public static class AmbiguityRoundTrip
{
   /// <summary>
   /// Asserts that the lookup is ambiguous, that the error lists the expected candidates, and that each listed candidate
   /// finds that candidate alone when its parameter list moves to parameterTypes, its return type to returnType, its
   /// location to position mode, or its project to project.
   /// </summary>
   /// <param name="expected">The listed candidates, without the count of those left out, with each path's separators as '/'.</param>
   public static async Task AssertEachListedCandidateFindsItselfAsync(
      SymbolLookupService service,
      LookupByName lookup,
      string[] expected,
      CancellationToken cancellationToken)
   {
      var matches = await service.FindAsync<ISymbol>(lookup, cancellationToken: cancellationToken);

      var ex = await Assert.ThrowsAsync<ToolException>(() => service.FindSingleAsync<ISymbol>(lookup, cancellationToken: cancellationToken));

      var listed = ex.Message[(ex.Message.IndexOf(": ", StringComparison.Ordinal) + 2)..]
         .Split("; ")
         .Where(candidate => !candidate.StartsWith("and ", StringComparison.Ordinal))
         .ToList();
      Assert.Equal(ErrorCodes.AmbiguousSymbol, ex.Code);
      Assert.Equal(expected.Select(c => c.Replace('/', Path.DirectorySeparatorChar)), listed);
      Assert.Equal(Math.Min(matches.Count, 5), listed.Count);
      for (var i = 0; i < listed.Count; i++)
      {
         var found = await service.FindAsync<ISymbol>(ParseListedCandidate(listed[i]), cancellationToken: cancellationToken);

         Assert.Equal([Identify(matches[i].Symbol)], found.Select(r => Identify(r.Symbol)));
      }

   }

   /// <summary>
   /// Returns what tells symbols apart across the compilations of the solution's projects, which hold different
   /// instances of one symbol.
   /// </summary>
   private static (string Display, string? Assembly, string? Location) Identify(ISymbol symbol) =>
      (symbol.ToDisplayString(), symbol.ContainingAssembly?.Name, symbol.Locations.FirstOrDefault(l => l.IsInSource)?.GetLineSpan().ToString());

   private static SymbolLookup ParseListedCandidate(string candidate)
   {
      string? project = null;
      var inProject = candidate.IndexOf(" in project ", StringComparison.Ordinal);
      if (inProject >= 0)
      {
         project = candidate[(inProject + 12)..];
         candidate = candidate[..inProject];
      }

      var at = candidate.IndexOf(" at ", StringComparison.Ordinal);
      if (at >= 0)
      {
         var position = candidate[(at + 4)..].Split(':');
         return new LookupByPosition { FilePath = position[0], Line = int.Parse(position[1]), Column = int.Parse(position[2]) };
      }

      string? returnType = null;
      var returns = candidate.IndexOf(" returns ", StringComparison.Ordinal);
      if (returns >= 0)
      {
         returnType = candidate[(returns + 9)..];
         candidate = candidate[..returns];
      }

      var (open, close) = candidate.EndsWith(')') ? ('(', ')') : candidate.EndsWith(']') ? ('[', ']') : ('\0', '\0');
      if (open == '\0')
         return new LookupByName { Name = candidate, Project = project };

      var start = FindOpeningBracket(candidate, open, close);
      var name = open == '[' ? candidate[..(start + 1)] + "]" : candidate[..start];
      return new LookupByName { Name = name, ParameterTypes = SplitTopLevel(candidate[(start + 1)..^1]), ReturnType = returnType, Project = project };
   }

   private static int FindOpeningBracket(string text, char open, char close)
   {
      var depth = 0;
      for (var i = text.Length - 1; i >= 0; i--)
      {
         if (text[i] == close)
            depth++;
         else if (text[i] == open && --depth == 0)
            return i;
      }

      throw new InvalidOperationException($"No opening '{open}' in {text}");
   }

   private static string[] SplitTopLevel(string parameters)
   {
      var parts = new List<string>();
      var depth = 0;
      var start = 0;
      for (var i = 0; i < parameters.Length; i++)
      {
         switch (parameters[i])
         {
            case '<' or '(' or '[':
               depth++;
               break;
            case '>' or ')' or ']':
               depth--;
               break;
            case ',' when depth == 0:
               parts.Add(parameters[start..i].Trim());
               start = i + 1;
               break;
         }
      }

      if (parameters.Trim().Length > 0)
         parts.Add(parameters[start..].Trim());
      return parts.ToArray();
   }
}
