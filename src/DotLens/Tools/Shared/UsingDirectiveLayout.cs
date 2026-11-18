using DotLens.Infrastructure;
using Microsoft.CodeAnalysis.Formatting;

namespace DotLens.Tools.Shared;

/// <summary>
/// Rewrites the using lists of a C# file: the one of the compilation unit and the one of each namespace declaration.
/// Keeps the file header first, keeps every preprocessor directive among the usings, and never moves a using across a
/// directive.
/// </summary>
/// <remarks>
/// <para>
/// The file header holds a license banner, or a directive such as #nullable or #define that belongs above the code. It
/// is the leading trivia of the first using up to its last directive or blank line, or up to its last line break when it
/// has neither.
/// In a file without usings, it is the leading trivia of the code up to the last blank line or directive outside an #if
/// block. When no such split comes before the first #if block, it ends at the line that opens that block. When that
/// header would leave a #define or #undef below it, it may end inside the #if blocks that enclose all the code.
/// </para>
/// <para>
/// A run is a sequence of usings with no directive between them. Each run starts at a using whose leading trivia holds
/// a directive, and that trivia up to its last directive or blank line stays at the start of the run. The methods sort
/// usings only within a run, so no using enters or leaves an #if or #region block.
/// </para>
/// <para>
/// The scope chain is the compilation unit, then each namespace declaration that is the only member of the one before.
/// The usings of every list in the chain reach all the code of the file.
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
   /// Returns the usings of every list in the scope chain of <paramref name="unit"/>.
   /// </summary>
   public static IEnumerable<UsingDirectiveSyntax> GetUsingsInScopeChain(CompilationUnitSyntax unit) =>
      GetScopeChain(unit).SelectMany(c => GetUsings(c));

   /// <summary>
   /// Returns the usings of the compilation unit and of every namespace declaration in it.
   /// </summary>
   public static IEnumerable<UsingDirectiveSyntax> GetAllUsings(CompilationUnitSyntax unit) =>
      GetContainers(unit).SelectMany(c => GetUsings(c));

   /// <summary>
   /// Removes the usings for which <paramref name="keep"/> returns false and sorts the rest within each run, in every
   /// using list of <paramref name="unit"/>.
   /// </summary>
   /// <param name="keep">Called with the usings of <paramref name="unit"/> itself.</param>
   /// <param name="newLine">The line break that ends a using whose line it shared with what no longer follows it.</param>
   public static CompilationUnitSyntax Organize(
      CompilationUnitSyntax unit,
      Func<UsingDirectiveSyntax, bool> keep,
      string newLine)
   {
      var containers = GetContainers(unit).Where(c => GetUsings(c).Count > 0).ToList();
      var markers = containers.ToDictionary(c => c, _ => new SyntaxAnnotation());
      var removedMarker = new SyntaxAnnotation();
      var removed = containers.SelectMany(c => GetUsings(c)).Where(u => !keep(u));

      // The annotations find each list and each removed using again in the tree that every Place call rewrites.
      var marked = unit.ReplaceNodes(
         containers.Where(c => c != unit).Concat<SyntaxNode>(removed),
         (original, rewritten) => rewritten.WithAdditionalAnnotations(
            original is UsingDirectiveSyntax ? removedMarker : markers[original]));
      if (markers.TryGetValue(unit, out var unitMarker))
         marked = marked.WithAdditionalAnnotations(unitMarker);

      foreach (var marker in markers.Values)
      {
         var container = marked.GetAnnotatedNodes(marker).Single();
         var runs = SplitRuns(GetUsings(container), u => !u.HasAnnotation(removedMarker));
         marked = Place(marked, container, runs, newLine);
      }

      return marked;
   }

   /// <summary>
   /// Adds <paramref name="newUsing"/> to the first list in the scope chain that has usings, and sorts the usings of
   /// that list within each run.
   /// </summary>
   /// <remarks>
   /// The new using goes in the first run whose #if blocks all enclose all the code that the list reaches. When no run
   /// qualifies, the new using goes in a run of its own below the file header. A file without usings gets the new using
   /// at file level, below the file header.
   /// </remarks>
   /// <param name="newUsing">The using to add, without leading trivia. It takes the indentation of the first using.</param>
   /// <param name="newLine">
   /// The line break of the blank line that follows a using added to a file without usings, and of a using whose line it
   /// shared with what no longer follows it.
   /// </param>
   /// <exception cref="ToolException">A #define or #undef inside an #if block follows the place for the new using.</exception>
   public static CompilationUnitSyntax Add(CompilationUnitSyntax unit, UsingDirectiveSyntax newUsing, string newLine)
   {
      var regions = new ConditionalRegions(unit);
      if (GetTargetList(unit) is not { } container)
         return AddToFileWithoutUsings(unit, newUsing, newLine, regions);

      var usings = GetUsings(container);
      var codeRegions = regions.EnclosingCode(container, usings[^1].GetLastToken().GetNextToken(includeZeroWidth: true));
      var indented = newUsing.WithLeadingTrivia(GetIndentation(usings[0].GetLeadingTrivia()));
      var runs = SplitRuns(usings, _ => true);
      if (runs.Find(r => regions.At(r.Start).IsSubsetOf(codeRegions)) is { } target)
      {
         target.Usings.Add((indented, -1));
      }
      else
      {
         var headerLength = GetHeaderLength(usings[0].GetLeadingTrivia(), regions, codeRegions);
         var header = SyntaxFactory.TriviaList(runs[0].Lead.Take(headerLength));
         var lead = SyntaxFactory.TriviaList(runs[0].Lead.Skip(headerLength));
         EnsureNoSymbolDirective(lead);
         runs[0] = runs[0] with { Lead = lead };
         runs.Insert(0, new Run(header, [(indented, -1)], runs[0].Start));
      }

      return Place(unit, container, runs, newLine);
   }

   /// <summary>
   /// Returns the position of the first using in the list that <see cref="Add"/> adds to, or null when that list is the
   /// compilation unit's.
   /// </summary>
   public static int? GetNamespaceListPosition(CompilationUnitSyntax unit) =>
      GetTargetList(unit) is BaseNamespaceDeclarationSyntax declaration ? declaration.Usings[0].SpanStart : null;

   private static SyntaxNode? GetTargetList(CompilationUnitSyntax unit) =>
      GetScopeChain(unit).Find(c => GetUsings(c).Count > 0);

   /// <param name="Lead">The trivia that stays at the start of the run, whether or not any of its usings remain.</param>
   /// <param name="Usings">
   /// The usings that remain, the first of them without the lead in its leading trivia, each with its index in the list
   /// the run comes from, or -1 for a new using.
   /// </param>
   /// <param name="Start">The position of the first using of the run in the tree that the run comes from.</param>
   private sealed record Run(SyntaxTriviaList Lead, List<(UsingDirectiveSyntax Using, int Index)> Usings, int Start);

   private static List<Run> SplitRuns(SyntaxList<UsingDirectiveSyntax> usings, Func<UsingDirectiveSyntax, bool> keep)
   {
      var runs = new List<Run>();
      for (var i = 0; i < usings.Count; i++)
      {
         var trivia = usings[i].GetLeadingTrivia();
         var current = usings[i];
         if (i == 0 || trivia.Any(t => t.IsDirective))
         {
            var leadLength = SplitIndex(trivia, canSplitAt: null);
            if (i == 0 && leadLength == 0)
               leadLength = SplitAfterLastLineBreak(trivia);

            runs.Add(new Run(SyntaxFactory.TriviaList(trivia.Take(leadLength)), [], usings[i].SpanStart));
            current = current.WithLeadingTrivia(trivia.Skip(leadLength));
         }

         if (keep(usings[i]))
            runs[^1].Usings.Add((current, i));
      }

      return runs;
   }

   /// <summary>
   /// Replaces the usings of <paramref name="container"/> with the runs, each sorted, and keeps the lead of a run whose
   /// usings were all removed.
   /// </summary>
   private static CompilationUnitSyntax Place(
      CompilationUnitSyntax root,
      SyntaxNode container,
      List<Run> runs,
      string newLine)
   {
      var usings = GetUsings(container);
      var ordered = new List<(UsingDirectiveSyntax Using, int Index)>();
      var pending = SyntaxTriviaList.Empty;
      for (var i = 0; i < runs.Count; i++)
      {
         var run = runs[i];
         var atListStart = i > 0 && ordered.Count == 0 && pending.Count == 0 && GetExterns(container).Count == 0;
         pending = pending.AddRange(atListStart || EndsWithBlankLine(pending) ? TrimLeadingBlankLines(run.Lead) : run.Lead);

         // The compiler requires the global usings of a file to precede its other usings.
         var sorted = run.Usings
            .OrderBy(u => u.Using.GlobalKeyword.IsKind(SyntaxKind.None) ? 1 : 0)
            .ThenBy(u => u.Using.Name?.ToString().StartsWith("System", StringComparison.Ordinal) == true ? 0 : 1)
            .ThenBy(u => u.Using.Name?.ToString())
            .ToList();
         if (sorted.Count == 0)
            continue;

         var first = sorted[0].Using;
         sorted[0] = sorted[0] with
         {
            Using = first.WithLeadingTrivia(pending.AddRange(TrimLeadingBlankLines(first.GetLeadingTrivia()))),
         };
         pending = SyntaxTriviaList.Empty;
         ordered.AddRange(sorted);
      }

      var indentation = GetIndentation(usings[0].GetLeadingTrivia());
      var placed = new List<UsingDirectiveSyntax>(ordered.Count);
      for (var i = 0; i < ordered.Count; i++)
      {
         var (current, index) = ordered[i];

         // A using from the middle of a shared line has no indentation of its own, since the space before it trails
         // the using before it.
         var startsLine = i == 0 || EndsWithLineBreak(placed[i - 1].GetTrailingTrivia());
         if (startsLine && index > 0 && !EndsWithLineBreak(usings[index - 1].GetTrailingTrivia()))
            current = current.WithLeadingTrivia(current.GetLeadingTrivia().AddRange(indentation));

         var trailing = current.GetTrailingTrivia();
         var follower = i + 1 < ordered.Count ? ordered[i + 1].Index : usings.Count;

         // A using that shares its line with what follows keeps sharing it only with the same follower.
         if (index >= 0 && follower != index + 1 && !EndsWithLineBreak(trailing))
            current = current.WithTrailingTrivia(TrimTrailingWhitespace(trailing).Add(SyntaxFactory.EndOfLine(newLine)));

         placed.Add(current);
      }

      if (pending.Count == 0 && placed.Count > 0)
         return root.ReplaceNode(container, WithUsings(container, placed));

      var next = usings[^1].GetLastToken().GetNextToken(includeZeroWidth: true);
      var nextTrivia = placed.Count == 0 || EndsWithBlankLine(pending)
         ? TrimLeadingBlankLines(next.LeadingTrivia)
         : next.LeadingTrivia;
      return root.ReplaceSyntax(
         [container],
         (_, rewritten) => WithUsings(rewritten, placed),
         [next],
         (_, rewritten) => rewritten.WithLeadingTrivia(pending.AddRange(nextTrivia)),
         [],
         (_, rewritten) => rewritten);
   }

   private static CompilationUnitSyntax AddToFileWithoutUsings(
      CompilationUnitSyntax unit,
      UsingDirectiveSyntax newUsing,
      string newLine,
      ConditionalRegions regions)
   {
      var next = unit.Externs.Count > 0
         ? unit.Externs[^1].GetLastToken().GetNextToken(includeZeroWidth: true)
         : unit.GetFirstToken(includeZeroWidth: true);
      var codeRegions = regions.EnclosingCode(unit, next);
      var headerLength = GetHeaderLength(next.LeadingTrivia, regions, codeRegions);
      var header = SyntaxFactory.TriviaList(next.LeadingTrivia.Take(headerLength));
      var nextTrivia = SyntaxFactory.TriviaList(next.LeadingTrivia.Skip(headerLength));
      EnsureNoSymbolDirective(nextTrivia);

      var placed = newUsing.WithLeadingTrivia(header.AddRange(newUsing.GetLeadingTrivia()));
      if (!next.IsKind(SyntaxKind.EndOfFileToken) || nextTrivia.Any(t => !IsBlank(t)))
         placed = placed.WithTrailingTrivia(placed.GetTrailingTrivia().Add(SyntaxFactory.EndOfLine(newLine)));

      return unit.ReplaceToken(next, next.WithLeadingTrivia(nextTrivia)).WithUsings([placed]);
   }

   /// <summary>
   /// Returns the number of trivia in the file header, which ends outside every #if block when no #define or #undef
   /// follows it there.
   /// </summary>
   /// <remarks>
   /// When a #define or #undef would follow, the header may end inside the #if blocks that enclose all the code, at
   /// the branches <paramref name="codeRegions"/> holds.
   /// </remarks>
   private static int GetHeaderLength(SyntaxTriviaList trivia, ConditionalRegions regions, HashSet<int> codeRegions)
   {
      var outside = SplitIndex(trivia, p => regions.At(p).Count == 0);
      return HasSymbolDirective(trivia.Skip(outside))
         ? SplitIndex(trivia, p => regions.At(p).IsSubsetOf(codeRegions))
         : outside;
   }

   private static void EnsureNoSymbolDirective(SyntaxTriviaList trivia)
   {
      // The compiler rejects a #define or #undef after the first token of the file.
      if (HasSymbolDirective(trivia))
      {
         throw new ToolException(
            ErrorCodes.NotSupported,
            "Cannot add the using: a #define or #undef inside an #if block must stay above it",
            "Add the using with edit_file at the place it belongs");
      }
   }

   private static bool HasSymbolDirective(IEnumerable<SyntaxTrivia> trivia) =>
      trivia.Any(t => t.IsKind(SyntaxKind.DefineDirectiveTrivia) || t.IsKind(SyntaxKind.UndefDirectiveTrivia));

   /// <summary>
   /// Returns the number of trivia up to the last directive or blank line.
   /// </summary>
   /// <param name="canSplitAt">
   /// Whether a split may fall at a position of the tree, or null when it may fall anywhere. When the trivia before the
   /// first #if block that it allows has no other split, the split falls at the start of the line that opens that block.
   /// It needs trivia from a tree, since it reads the position of each trivia.
   /// </param>
   private static int SplitIndex(SyntaxTriviaList trivia, Func<int, bool>? canSplitAt)
   {
      var leadLength = 0;
      var lineIsBlank = true;
      var lineStart = 0;
      for (var i = 0; i < trivia.Count; i++)
      {
         var item = trivia[i];
         var canSplitAfter = canSplitAt?.Invoke(item.FullSpan.End) ?? true;
         if (item.IsKind(SyntaxKind.EndOfLineTrivia))
         {
            if (lineIsBlank && canSplitAfter)
               leadLength = i + 1;
            lineIsBlank = true;
            lineStart = i + 1;
            continue;
         }

         if (item.IsDirective)
         {
            if (canSplitAt is not null && item.IsKind(SyntaxKind.IfDirectiveTrivia) && leadLength == 0
                && canSplitAt(trivia[lineStart].FullSpan.Start))
            {
               leadLength = lineStart;
            }

            if (canSplitAfter)
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

      return leadLength;
   }

   /// <summary>
   /// Returns the number of trivia up to the last line break, so trivia on the line of the token stays with the token.
   /// </summary>
   private static int SplitAfterLastLineBreak(SyntaxTriviaList trivia)
   {
      var leadLength = 0;
      for (var i = 0; i < trivia.Count; i++)
      {
         if (trivia[i].IsKind(SyntaxKind.EndOfLineTrivia) || EndsWithLineBreak(trivia[i]))
            leadLength = i + 1;
      }

      return leadLength;
   }

   /// <summary>
   /// Returns the whitespace that starts the last line of the trivia.
   /// </summary>
   private static SyntaxTriviaList GetIndentation(SyntaxTriviaList trivia) =>
      SyntaxFactory.TriviaList(trivia
         .Skip(SplitAfterLastLineBreak(trivia))
         .TakeWhile(t => t.IsKind(SyntaxKind.WhitespaceTrivia)));

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

   private static bool EndsWithLineBreak(SyntaxTriviaList trivia) => trivia.Count > 0 && EndsWithLineBreak(trivia[^1]);

   private static SyntaxTriviaList TrimTrailingWhitespace(SyntaxTriviaList trivia)
   {
      var length = trivia.Count;
      while (length > 0 && trivia[length - 1].IsKind(SyntaxKind.WhitespaceTrivia))
         length--;

      return SyntaxFactory.TriviaList(trivia.Take(length));
   }

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

   private static List<SyntaxNode> GetScopeChain(CompilationUnitSyntax unit)
   {
      var chain = new List<SyntaxNode> { unit };
      if (unit.AttributeLists.Count > 0)
         return chain;

      var members = unit.Members;
      while (members is [BaseNamespaceDeclarationSyntax only])
      {
         chain.Add(only);
         members = only.Members;
      }

      return chain;
   }

   private static IEnumerable<SyntaxNode> GetContainers(CompilationUnitSyntax unit) =>
      unit.DescendantNodesAndSelf(IsContainer).Where(IsContainer);

   private static bool IsContainer(SyntaxNode node) => node is CompilationUnitSyntax or BaseNamespaceDeclarationSyntax;

   private static SyntaxList<UsingDirectiveSyntax> GetUsings(SyntaxNode container) => container switch
   {
      CompilationUnitSyntax unit => unit.Usings,
      BaseNamespaceDeclarationSyntax declaration => declaration.Usings,
      _ => throw new InvalidOperationException($"Unexpected using container: {container.Kind()}"),
   };

   private static SyntaxList<ExternAliasDirectiveSyntax> GetExterns(SyntaxNode container) => container switch
   {
      CompilationUnitSyntax unit => unit.Externs,
      BaseNamespaceDeclarationSyntax declaration => declaration.Externs,
      _ => throw new InvalidOperationException($"Unexpected using container: {container.Kind()}"),
   };

   private static SyntaxNode WithUsings(SyntaxNode container, IEnumerable<UsingDirectiveSyntax> usings) => container switch
   {
      CompilationUnitSyntax unit => unit.WithUsings(SyntaxFactory.List(usings)),
      BaseNamespaceDeclarationSyntax declaration => declaration.WithUsings(SyntaxFactory.List(usings)),
      _ => throw new InvalidOperationException($"Unexpected using container: {container.Kind()}"),
   };

   /// <summary>
   /// The branches of the #if blocks of a file. A branch runs from the end of the #if, #elif or #else line that opens it
   /// to the start of the directive that ends it.
   /// </summary>
   private sealed class ConditionalRegions
   {
      /// <summary>
      /// The branches, each with whether it is the only branch of its block, an #if without #elif or #else.
      /// </summary>
      private readonly List<(int Start, int End, bool Alone)> _branches = [];

      public ConditionalRegions(CompilationUnitSyntax unit)
      {
         for (var directive = unit.GetFirstDirective(); directive is not null; directive = directive.GetNextDirective())
         {
            if (!directive.IsKind(SyntaxKind.IfDirectiveTrivia))
               continue;

            var related = directive.GetRelatedDirectives().OrderBy(d => d.SpanStart).ToList();
            var alone = related.Count(d => !d.IsKind(SyntaxKind.EndIfDirectiveTrivia)) == 1;
            for (var i = 0; i < related.Count && !related[i].IsKind(SyntaxKind.EndIfDirectiveTrivia); i++)
            {
               // A branch without an #endif runs to the end of the file.
               var end = i + 1 < related.Count ? related[i + 1].FullSpan.Start : int.MaxValue;
               _branches.Add((related[i].FullSpan.End, end, alone));
            }
         }
      }

      /// <summary>
      /// Returns the indexes of the branches that hold the position.
      /// </summary>
      public HashSet<int> At(int position) =>
         [.. Enumerable.Range(0, _branches.Count).Where(i => _branches[i].Start <= position && position < _branches[i].End)];

      /// <summary>
      /// Returns the indexes of the branches that hold all the code of <paramref name="container"/> from
      /// <paramref name="first"/> on.
      /// </summary>
      /// <remarks>
      /// A branch of a block with #elif or #else never counts, because the other branches can hold code that the parser
      /// keeps as disabled text.
      /// </remarks>
      public HashSet<int> EnclosingCode(SyntaxNode container, SyntaxToken first)
      {
         var last = container switch
         {
            CompilationUnitSyntax unit => unit.EndOfFileToken.GetPreviousToken(),
            NamespaceDeclarationSyntax declaration => declaration.CloseBraceToken.GetPreviousToken(),
            FileScopedNamespaceDeclarationSyntax declaration => declaration.GetLastToken(),
            _ => throw new InvalidOperationException($"Unexpected using container: {container.Kind()}"),
         };

         var regions = At(first.SpanStart);
         if (last.SpanStart > first.SpanStart)
            regions.IntersectWith(At(last.SpanStart));
         regions.RemoveWhere(i => !_branches[i].Alone);
         return regions;
      }
   }
}
