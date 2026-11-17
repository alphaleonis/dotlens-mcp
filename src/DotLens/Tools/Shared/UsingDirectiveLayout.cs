using DotLens.Infrastructure;
using Microsoft.CodeAnalysis.Formatting;

namespace DotLens.Tools.Shared;

/// <summary>
/// Rewrites the top-level using directives of a C# file. Keeps its file header first, keeps every preprocessor
/// directive among the usings, and never moves a using across a directive.
/// </summary>
/// <remarks>
/// <para>
/// The file header holds a license banner, or a directive such as #nullable or #define that belongs above the code. It
/// is the leading trivia of the first using up to its last directive or blank line, or up to its last line break when it
/// has neither.
/// In a file without usings, it is the leading trivia of the code up to the last blank line or directive outside an #if
/// block. When no such split comes before the first #if block, it ends at the line that opens that block.
/// </para>
/// <para>
/// A run is a sequence of usings with no directive between them. Each run starts at a using whose leading trivia holds
/// a directive, and that trivia up to its last directive or blank line stays at the start of the run. The methods sort
/// usings only within a run, so no using enters or leaves an #if or #region block.
/// </para>
/// </remarks>
internal static class UsingDirectiveLayout
{
   /// <summary>
   /// Returns the line break that the document's text uses.
   /// </summary>
   /// <returns>The line break that ends the first line, or the document's formatting option when the text has one line.</returns>
   public static async Task<string> GetNewLineAsync(Document document, CancellationToken cancellationToken)
   {
      var text = await document.GetTextAsync(cancellationToken);
      if (text.Lines.Count > 1)
      {
         var firstLine = text.Lines[0];
         return text.ToString(TextSpan.FromBounds(firstLine.End, firstLine.EndIncludingLineBreak));
      }

      var options = await document.GetOptionsAsync(cancellationToken);
      return options.GetOption(FormattingOptions.NewLine);
   }

   /// <summary>
   /// Removes the top-level usings of <paramref name="unit"/> that <paramref name="kept"/> lacks and sorts the rest
   /// within each run.
   /// </summary>
   /// <param name="kept">The usings to keep, as the instances from <see cref="CompilationUnitSyntax.Usings"/>.</param>
   public static CompilationUnitSyntax Organize(CompilationUnitSyntax unit, IEnumerable<UsingDirectiveSyntax> kept)
   {
      if (unit.Usings.Count == 0)
         return unit;

      var keep = kept.ToHashSet(ReferenceEqualityComparer.Instance);
      return Place(unit, SplitRuns(unit.Usings, keep.Contains));
   }

   /// <summary>
   /// Adds <paramref name="newUsing"/> to the first run outside an #if block and sorts the usings within each run.
   /// </summary>
   /// <remarks>
   /// When every run is inside an #if block, the new using goes in a run of its own below the file header.
   /// </remarks>
   /// <param name="newLine">The line break of the blank line that follows a using added to a file without usings.</param>
   /// <exception cref="ToolException">A #define or #undef inside an #if block follows the place for the new using.</exception>
   public static CompilationUnitSyntax Add(CompilationUnitSyntax unit, UsingDirectiveSyntax newUsing, string newLine)
   {
      if (unit.Usings.Count == 0)
         return AddToFileWithoutUsings(unit, newUsing, newLine);

      var runs = SplitRuns(unit.Usings, _ => true);
      if (runs.Find(r => !r.InsideIf) is { } target)
      {
         target.Usings.Add(newUsing);
      }
      else
      {
         var (header, lead) = SplitTrivia(runs[0].Lead, outsideIfOnly: true);
         EnsureNoSymbolDirective(lead);
         runs[0] = runs[0] with { Lead = lead };
         runs.Insert(0, new Run(header, [newUsing], InsideIf: false));
      }

      return Place(unit, runs);
   }

   /// <param name="Lead">The trivia that stays at the start of the run, whether or not any of its usings remain.</param>
   /// <param name="Usings">The usings that remain, the first of them without the lead in its leading trivia.</param>
   /// <param name="InsideIf">Whether the run is inside an #if, #elif or #else branch.</param>
   private sealed record Run(SyntaxTriviaList Lead, List<UsingDirectiveSyntax> Usings, bool InsideIf);

   private static List<Run> SplitRuns(SyntaxList<UsingDirectiveSyntax> usings, Func<UsingDirectiveSyntax, bool> keep)
   {
      var runs = new List<Run>();
      var openIfs = 0;
      for (var i = 0; i < usings.Count; i++)
      {
         var trivia = usings[i].GetLeadingTrivia();
         var hasDirective = trivia.Any(t => t.IsDirective);
         var current = usings[i];
         if (i == 0 || hasDirective)
         {
            var (lead, rest) = SplitTrivia(trivia, outsideIfOnly: false);
            if (i == 0 && lead.Count == 0)
               (lead, rest) = SplitAfterLastLineBreak(trivia);

            foreach (var directive in lead.Where(t => t.IsDirective))
            {
               // The count stays at zero on an #endif whose #if is not among the usings.
               openIfs = Math.Max(0, openIfs + directive.Kind() switch
               {
                  SyntaxKind.IfDirectiveTrivia => 1,
                  SyntaxKind.EndIfDirectiveTrivia => -1,
                  _ => 0,
               });
            }

            runs.Add(new Run(lead, [], InsideIf: openIfs > 0));
            current = current.WithLeadingTrivia(rest);
         }

         if (keep(usings[i]))
            runs[^1].Usings.Add(current);
      }

      return runs;
   }

   /// <summary>
   /// Replaces the usings of <paramref name="unit"/> with the runs, each sorted, and keeps the lead of a run whose
   /// usings were all removed.
   /// </summary>
   private static CompilationUnitSyntax Place(CompilationUnitSyntax unit, List<Run> runs)
   {
      var placed = new List<UsingDirectiveSyntax>();
      var pending = SyntaxTriviaList.Empty;
      for (var i = 0; i < runs.Count; i++)
      {
         var run = runs[i];
         var atFileStart = i > 0 && placed.Count == 0 && pending.Count == 0 && unit.Externs.Count == 0;
         pending = pending.AddRange(atFileStart || EndsWithBlankLine(pending) ? TrimLeadingBlankLines(run.Lead) : run.Lead);
         var sorted = run.Usings
            .OrderBy(u => u.Name?.ToString().StartsWith("System", StringComparison.Ordinal) == true ? 0 : 1)
            .ThenBy(u => u.Name?.ToString())
            .ToList();
         if (sorted.Count == 0)
            continue;

         sorted[0] = sorted[0].WithLeadingTrivia(pending.AddRange(TrimLeadingBlankLines(sorted[0].GetLeadingTrivia())));
         pending = SyntaxTriviaList.Empty;
         placed.AddRange(sorted);
      }

      if (pending.Count > 0 || placed.Count == 0)
      {
         var next = unit.Usings[^1].GetLastToken().GetNextToken(includeZeroWidth: true);
         var nextTrivia = placed.Count == 0 || EndsWithBlankLine(pending)
            ? TrimLeadingBlankLines(next.LeadingTrivia)
            : next.LeadingTrivia;
         unit = unit.ReplaceToken(next, next.WithLeadingTrivia(pending.AddRange(nextTrivia)));
      }

      return unit.WithUsings(SyntaxFactory.List(placed));
   }

   private static CompilationUnitSyntax AddToFileWithoutUsings(
      CompilationUnitSyntax unit,
      UsingDirectiveSyntax newUsing,
      string newLine)
   {
      var next = unit.Externs.Count > 0
         ? unit.Externs[^1].GetLastToken().GetNextToken(includeZeroWidth: true)
         : unit.GetFirstToken(includeZeroWidth: true);
      var (header, nextTrivia) = SplitTrivia(next.LeadingTrivia, outsideIfOnly: true);
      EnsureNoSymbolDirective(nextTrivia);

      var placed = newUsing.WithLeadingTrivia(header.AddRange(newUsing.GetLeadingTrivia()));
      if (!next.IsKind(SyntaxKind.EndOfFileToken) || nextTrivia.Any(t => !IsBlank(t)))
         placed = placed.WithTrailingTrivia(placed.GetTrailingTrivia().Add(SyntaxFactory.EndOfLine(newLine)));

      return unit.ReplaceToken(next, next.WithLeadingTrivia(nextTrivia)).WithUsings([placed]);
   }

   private static void EnsureNoSymbolDirective(SyntaxTriviaList trivia)
   {
      // The compiler rejects a #define or #undef after the first token of the file.
      if (trivia.Any(t => t.IsKind(SyntaxKind.DefineDirectiveTrivia) || t.IsKind(SyntaxKind.UndefDirectiveTrivia)))
      {
         throw new ToolException(
            ErrorCodes.NotSupported,
            "Cannot add the using: a #define or #undef inside an #if block must stay above it",
            "Add the using with edit_file at the place it belongs");
      }
   }

   /// <summary>
   /// Splits the trivia after its last directive or blank line.
   /// </summary>
   /// <param name="outsideIfOnly">
   /// Whether to split only where no #if block is open. When the trivia before the first #if block has no other split, the
   /// split falls at the start of the line that opens that block.
   /// </param>
   private static (SyntaxTriviaList Lead, SyntaxTriviaList Remainder) SplitTrivia(
      SyntaxTriviaList trivia,
      bool outsideIfOnly)
   {
      var leadLength = 0;
      var lineIsBlank = true;
      var lineStart = 0;
      var openIfs = 0;
      for (var i = 0; i < trivia.Count; i++)
      {
         var item = trivia[i];
         if (item.IsKind(SyntaxKind.EndOfLineTrivia))
         {
            if (lineIsBlank && (openIfs == 0 || !outsideIfOnly))
               leadLength = i + 1;
            lineIsBlank = true;
            lineStart = i + 1;
            continue;
         }

         if (item.IsDirective)
         {
            if (item.IsKind(SyntaxKind.IfDirectiveTrivia) && openIfs == 0 && outsideIfOnly && leadLength == 0)
               leadLength = lineStart;

            // The count stays at zero on an #endif whose #if opened before this trivia.
            openIfs = Math.Max(0, openIfs + item.Kind() switch
            {
               SyntaxKind.IfDirectiveTrivia => 1,
               SyntaxKind.EndIfDirectiveTrivia => -1,
               _ => 0,
            });
            if (openIfs == 0 || !outsideIfOnly)
               leadLength = i + 1;
         }

         // Trivia that ends with a line break, such as a directive, ends its line.
         if (!item.IsKind(SyntaxKind.WhitespaceTrivia))
         {
            lineIsBlank = EndsWithLineBreak(item);
            if (lineIsBlank)
               lineStart = i + 1;
         }
      }

      return (
         SyntaxFactory.TriviaList(trivia.Take(leadLength)),
         SyntaxFactory.TriviaList(trivia.Skip(leadLength)));
   }

   /// <summary>
   /// Splits the trivia after its last line break, so trivia on the line of the token stays with the token.
   /// </summary>
   private static (SyntaxTriviaList Lead, SyntaxTriviaList Remainder) SplitAfterLastLineBreak(SyntaxTriviaList trivia)
   {
      var leadLength = 0;
      for (var i = 0; i < trivia.Count; i++)
      {
         if (trivia[i].IsKind(SyntaxKind.EndOfLineTrivia) || EndsWithLineBreak(trivia[i]))
            leadLength = i + 1;
      }

      return (
         SyntaxFactory.TriviaList(trivia.Take(leadLength)),
         SyntaxFactory.TriviaList(trivia.Skip(leadLength)));
   }

   /// <summary>
   /// Returns whether the trivia, which starts at the start of a line, ends with a blank line.
   /// </summary>
   private static bool EndsWithBlankLine(SyntaxTriviaList trivia)
   {
      var lineIsBlank = true;
      var endsWithBlankLine = false;
      foreach (var item in trivia)
      {
         if (item.IsKind(SyntaxKind.EndOfLineTrivia))
         {
            endsWithBlankLine = lineIsBlank;
            lineIsBlank = true;
            continue;
         }

         endsWithBlankLine = false;
         if (!item.IsKind(SyntaxKind.WhitespaceTrivia))
            lineIsBlank = EndsWithLineBreak(item);
      }

      return endsWithBlankLine;
   }

   private static bool EndsWithLineBreak(SyntaxTrivia trivia) => trivia.ToFullString() is [.., '\n' or '\r'];

   private static SyntaxTriviaList TrimLeadingBlankLines(SyntaxTriviaList trivia)
   {
      var start = 0;
      for (var i = 0; i < trivia.Count && IsBlank(trivia[i]); i++)
      {
         if (trivia[i].IsKind(SyntaxKind.EndOfLineTrivia))
            start = i + 1;
      }

      return SyntaxFactory.TriviaList(trivia.Skip(start));
   }

   private static bool IsBlank(SyntaxTrivia trivia) =>
      trivia.IsKind(SyntaxKind.WhitespaceTrivia) || trivia.IsKind(SyntaxKind.EndOfLineTrivia);
}
