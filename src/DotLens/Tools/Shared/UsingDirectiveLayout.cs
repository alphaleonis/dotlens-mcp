using System.Globalization;
using DotLens.Infrastructure;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Formatting;

namespace DotLens.Tools.Shared;

/// <summary>
/// Rewrites the using lists of a C# file: the one of the compilation unit and the one of each namespace declaration.
/// Keeps the file header first, keeps every preprocessor directive among the usings, and never moves a using across a
/// directive. Sorts the extern alias lists of the file the same way.
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
/// A run is a sequence of entries of one list, usings or extern aliases, with no directive between them. Each run starts
/// at an entry whose leading trivia holds a directive, and that trivia up to its last directive or blank line stays at
/// the start of the run. The methods sort entries only within a run, so no entry enters or leaves an #if or #region
/// block.
/// </para>
/// <para>
/// Roslyn's Organize Imports leaves the extern aliases and usings of a container in their order when a directive splits
/// them, as IsSplit defines it, so these methods keep that order too.
/// </para>
/// <para>
/// The scope chain is the compilation unit, then each namespace declaration that is the only member of the one before.
/// The usings of every list in the chain reach all the code of the file.
/// </para>
/// <para>
/// When the entries of a compilation unit or namespace declaration are out of order and no directive splits them into
/// runs, each entry below the file header loses the line breaks that start its leading trivia, as Roslyn's Organize
/// Imports does when it sorts. A line that holds only whitespace stays. When the options separate groups, a blank line
/// goes above each using that starts a group and has no line break in its leading trivia, as Roslyn puts it there.
/// </para>
/// <para>
/// Each line break that the methods add is the one that GetListNewLine returns for the list. Every other line break
/// stays with the entry whose trailing trivia holds it, so a sorted entry takes its line break along, as Roslyn's does.
/// </para>
/// </remarks>
internal static class UsingDirectiveLayout
{
   private const string SortSystemFirstKey = "dotnet_sort_system_directives_first";
   private const string SeparateGroupsKey = "dotnet_separate_import_directive_groups";

   /// <summary>
   /// Returns the layout options of the document: its line break, and the two using options of its .editorconfig.
   /// </summary>
   public static async Task<UsingLayoutOptions> GetOptionsAsync(Document document, CancellationToken cancellationToken)
   {
      var tree = await document.GetSyntaxTreeAsync(cancellationToken)
         ?? throw new InvalidOperationException($"The document has no syntax tree: {document.FilePath}");
      var config = document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(tree);
      return new UsingLayoutOptions(
         await GetNewLineAsync(document, cancellationToken),
         ReadBoolean(config, SortSystemFirstKey, defaultValue: true),
         ReadBoolean(config, SeparateGroupsKey, defaultValue: false));
   }

   // Roslyn takes a value that bool.TryParse rejects, such as one with a severity, as the default, as
   // UsingToolsEditorConfigTests pins.
   private static bool ReadBoolean(AnalyzerConfigOptions config, string key, bool defaultValue) =>
      config.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : defaultValue;

   /// <summary>
   /// Returns the line break that the document's text uses.
   /// </summary>
   /// <returns>The line break that ends the first line, or the document's formatting option when the text has one line.</returns>
   private static async Task<string> GetNewLineAsync(Document document, CancellationToken cancellationToken)
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
   /// Returns whether the compilation unit or a namespace declaration in it has an extern alias.
   /// </summary>
   public static bool HasExternAliases(CompilationUnitSyntax unit) =>
      GetContainers(unit).Any(c => GetExterns(c).Count > 0);

   /// <summary>
   /// Removes the usings for which <paramref name="keep"/> returns false and sorts the rest, in every using list of
   /// <paramref name="unit"/>. Sorts every extern alias list too.
   /// </summary>
   /// <remarks>
   /// The extern aliases and usings of a container that a directive splits keep their order.
   /// </remarks>
   /// <param name="keep">Called with the usings of <paramref name="unit"/> itself.</param>
   public static CompilationUnitSyntax Organize(
      CompilationUnitSyntax unit,
      Func<UsingDirectiveSyntax, bool> keep,
      UsingLayoutOptions options)
   {
      var usingList = UsingList(options);
      var containers = GetContainers(unit).Where(c => GetUsings(c).Count > 0 || GetExterns(c).Count > 0).ToList();
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
         bool IsKept(UsingDirectiveSyntax u) => !u.HasAnnotation(removedMarker);
         var externRuns = SplitRuns(GetExterns(container), _ => true);
         var usingRuns = SplitRuns(GetUsings(container), IsKept);
         var split = IsSplit(externRuns, usingRuns);
         var keepBlankLines = KeepsBlankLines(split, externRuns, usingRuns, usingList.Order);
         var newLine = GetListNewLine(
            GetExterns(container).Concat<SyntaxNode>(GetUsings(container).Where(IsKept)), options.NewLine);
         if (GetExterns(container).Count > 0)
         {
            marked = Place(marked, container, ExternList, externRuns, keepBlankLines, sort: !split, newLine);
            container = marked.GetAnnotatedNodes(marker).Single();
         }

         if (GetUsings(container).Count > 0)
         {
            var runs = SplitRuns(GetUsings(container), IsKept);
            marked = Place(marked, container, usingList, runs, keepBlankLines, sort: !split, newLine);
         }
      }

      return marked;
   }

   /// <summary>
   /// Adds <paramref name="newUsing"/> to the first list in the scope chain that has usings, and sorts the usings of
   /// that list within each run when no directive splits it.
   /// </summary>
   /// <remarks>
   /// <para>
   /// The new using goes in the first run whose #if blocks all enclose all the code that the list reaches. When no run
   /// qualifies, the new using goes in a run of its own below the file header. A file without usings gets the new using
   /// at file level, below the file header.
   /// </para>
   /// <para>
   /// A list that a directive splits keeps the order of its usings. The new using then goes to its sorted place in a
   /// run that is in order, and to the end of any other run, as Roslyn adds a using to a list it does not sort.
   /// </para>
   /// </remarks>
   /// <param name="newUsing">
   /// The using to add, without trivia. It takes the indentation of the first using and the line break that
   /// <see cref="GetListNewLine"/> returns for the list.
   /// </param>
   /// <exception cref="ToolException">A #define or #undef inside an #if block follows the place for the new using.</exception>
   public static CompilationUnitSyntax Add(
      CompilationUnitSyntax unit,
      UsingDirectiveSyntax newUsing,
      UsingLayoutOptions options)
   {
      var regions = new ConditionalRegions(unit);
      if (GetTargetList(unit) is not { } container)
         return AddToFileWithoutUsings(unit, newUsing, options, regions);

      var usingList = UsingList(options);
      var usings = GetUsings(container);
      var codeRegions = regions.EnclosingCode(container, usings[^1].GetLastToken().GetNextToken(includeZeroWidth: true));
      var newLine = GetListNewLine(GetExterns(container).Concat<SyntaxNode>(usings), options.NewLine);
      var indented = newUsing
         .WithLeadingTrivia(GetIndentation(usings[0].GetLeadingTrivia()))
         .WithTrailingTrivia(SyntaxFactory.EndOfLine(newLine));
      var runs = SplitRuns(usings, _ => true);
      var externRuns = SplitRuns(GetExterns(container), _ => true);
      var split = IsSplit(externRuns, runs);
      var keepBlankLines = KeepsBlankLines(split, externRuns, runs, usingList.Order);
      if (runs.Find(r => regions.At(r.Start).IsSubsetOf(codeRegions)) is { } target)
      {
         var index = split && IsInOrder([target], usingList.Order)
            ? target.Entries.FindIndex(e => usingList.Order.Compare(indented, e.Entry) < 0)
            : -1;
         target.Entries.Insert(index < 0 ? target.Entries.Count : index, (indented, -1));
      }
      else
      {
         var headerLength = GetHeaderLength(usings[0].GetLeadingTrivia(), regions, codeRegions);
         var header = SyntaxFactory.TriviaList(runs[0].Lead.Take(headerLength));
         var lead = SyntaxFactory.TriviaList(runs[0].Lead.Skip(headerLength));
         EnsureNoSymbolDirective(lead);
         runs[0] = runs[0] with { Lead = lead };
         runs.Insert(0, new Run<UsingDirectiveSyntax>(header, [(indented, -1)], runs[0].Start));
      }

      return Place(unit, container, usingList, runs, keepBlankLines, sort: !split, newLine);
   }

   /// <summary>
   /// Returns the position of the first using in the list that <see cref="Add"/> adds to, or null when that list is the
   /// compilation unit's.
   /// </summary>
   public static int? GetNamespaceListPosition(CompilationUnitSyntax unit) =>
      GetTargetList(unit) is BaseNamespaceDeclarationSyntax declaration ? declaration.Usings[0].SpanStart : null;

   private static SyntaxNode? GetTargetList(CompilationUnitSyntax unit) =>
      GetScopeChain(unit).Find(c => GetUsings(c).Count > 0);

   /// <summary>
   /// One kind of list in a compilation unit or namespace declaration, and the order its entries sort in.
   /// </summary>
   /// <param name="IsFirst">Whether the list of a container comes first in it, with no extern alias above it.</param>
   /// <param name="StartsGroup">
   /// Whether the second of two adjacent entries starts a group, or null when no blank line separates groups. The first
   /// entry of a list below extern aliases starts a group too.
   /// </param>
   private sealed record EntryList<T>(
      Func<SyntaxNode, SyntaxList<T>> Get,
      Func<SyntaxNode, IEnumerable<T>, SyntaxNode> With,
      IComparer<T> Order,
      Func<SyntaxNode, bool> IsFirst,
      Func<T, T, bool>? StartsGroup)
      where T : SyntaxNode;

   private static EntryList<UsingDirectiveSyntax> UsingList(UsingLayoutOptions options) => new(
      GetUsings,
      WithUsings,
      new UsingComparer(options.SystemFirst),
      c => GetExterns(c).Count == 0,
      options.SeparateGroups ? UsingComparer.StartsGroup : null);

   // Roslyn's Organize Imports sorts extern aliases by name, with System not first.
   private static readonly EntryList<ExternAliasDirectiveSyntax> ExternList = new(
      GetExterns,
      WithExterns,
      Comparer<ExternAliasDirectiveSyntax>.Create(
         (x, y) => NameOrder.CompareText(x.Identifier.ValueText, y.Identifier.ValueText, systemFirst: false)),
      _ => true,
      StartsGroup: null);

   /// <param name="Lead">The trivia that stays at the start of the run, whether or not any of its entries remain.</param>
   /// <param name="Entries">
   /// The entries that remain, the first of them without the lead in its leading trivia, each with its index in the list
   /// the run comes from, or -1 for a new using.
   /// </param>
   /// <param name="Start">The position of the first entry of the run in the tree that the run comes from.</param>
   private sealed record Run<T>(SyntaxTriviaList Lead, List<(T Entry, int Index)> Entries, int Start)
      where T : SyntaxNode;

   private static List<Run<T>> SplitRuns<T>(SyntaxList<T> entries, Func<T, bool> keep)
      where T : SyntaxNode
   {
      var runs = new List<Run<T>>();
      for (var i = 0; i < entries.Count; i++)
      {
         var trivia = entries[i].GetLeadingTrivia();
         var current = entries[i];
         if (i == 0 || trivia.Any(t => t.IsDirective))
         {
            var leadLength = SplitIndex(trivia, canSplitAt: null);
            if (i == 0 && leadLength == 0)
               leadLength = SplitAfterLastLineBreak(trivia);

            runs.Add(new Run<T>(SyntaxFactory.TriviaList(trivia.Take(leadLength)), [], entries[i].SpanStart));
            current = current.WithLeadingTrivia(trivia.Skip(leadLength));
         }

         if (keep(entries[i]))
            runs[^1].Entries.Add((current, i));
      }

      return runs;
   }

   /// <summary>
   /// Replaces the entries of <paramref name="list"/> in <paramref name="container"/> with the runs, and keeps the lead
   /// of a run whose entries were all removed.
   /// </summary>
   /// <param name="keepBlankLines">Whether the entries keep the blank lines above them.</param>
   /// <param name="sort">Whether to sort the entries of each run, or to keep them in the order the run holds them.</param>
   /// <param name="newLine">The line break of each line that the method adds, from <see cref="GetListNewLine"/>.</param>
   private static CompilationUnitSyntax Place<T>(
      CompilationUnitSyntax root,
      SyntaxNode container,
      EntryList<T> list,
      List<Run<T>> runs,
      bool keepBlankLines,
      bool sort,
      string newLine)
      where T : SyntaxNode
   {
      var entries = list.Get(container);
      var ordered = new List<(T Entry, int Index)>();
      var pending = SyntaxTriviaList.Empty;
      for (var i = 0; i < runs.Count; i++)
      {
         var run = runs[i];
         var atListStart = i > 0 && ordered.Count == 0 && pending.Count == 0 && list.IsFirst(container);

         var lead = run.Lead;
         var runEntries = run.Entries;

         // Below extern aliases no file header exists, so a sort moves the lead with the entry it stood above.
         if (i == 0 && !keepBlankLines && !list.IsFirst(container))
         {
            if (runEntries is [(var head, 0), ..])
            {
               var moved = head.WithLeadingTrivia(lead.AddRange(head.GetLeadingTrivia()));
               runEntries = [(moved, 0), .. runEntries.Skip(1)];
               lead = SyntaxTriviaList.Empty;
            }
            else
            {
               lead = TrimLeadingLineBreaks(lead);
            }
         }

         pending = pending.AddRange(atListStart || EndsWithBlankLine(pending) ? TrimLeadingBlankLines(lead) : lead);

         var sorted = (sort ? runEntries.OrderBy(e => e.Entry, list.Order) : runEntries.AsEnumerable())
            .Select(e => keepBlankLines ? e : e with { Entry = WithoutLeadingLineBreaks(e.Entry) })
            .ToList();
         if (sorted.Count == 0)
            continue;

         var first = sorted[0].Entry;
         sorted[0] = sorted[0] with
         {
            Entry = first.WithLeadingTrivia(pending.AddRange(
               keepBlankLines ? TrimLeadingBlankLines(first.GetLeadingTrivia()) : first.GetLeadingTrivia())),
         };
         pending = SyntaxTriviaList.Empty;
         ordered.AddRange(sorted);
      }

      var indentation = GetIndentation(entries[0].GetLeadingTrivia());
      var placed = new List<T>(ordered.Count);
      for (var i = 0; i < ordered.Count; i++)
      {
         var (current, index) = ordered[i];

         // An entry from the middle of a shared line has no indentation of its own, since the space before it trails
         // the entry before it.
         var startsLine = i == 0 || EndsWithLineBreak(placed[i - 1].GetTrailingTrivia());
         if (startsLine && index > 0 && !EndsWithLineBreak(entries[index - 1].GetTrailingTrivia()))
            current = current.WithLeadingTrivia(current.GetLeadingTrivia().AddRange(indentation));

         var trailing = current.GetTrailingTrivia();
         var follower = i + 1 < ordered.Count ? ordered[i + 1].Index : entries.Count;

         // An entry that shares its line with what follows keeps sharing it only with the same follower.
         if (index >= 0 && follower != index + 1 && !EndsWithLineBreak(trailing))
            current = current.WithTrailingTrivia(TrimTrailingWhitespace(trailing).Add(SyntaxFactory.EndOfLine(newLine)));

         if (list.StartsGroup is { } startsGroup
             && (i == 0 ? !list.IsFirst(container) : startsGroup(placed[i - 1], current)))
         {
            current = current.WithLeadingTrivia(SeparateGroup(current.GetLeadingTrivia(), newLine));
         }

         placed.Add(current);
      }

      if (pending.Count == 0 && placed.Count > 0)
         return root.ReplaceNode(container, list.With(container, placed));

      var next = entries[^1].GetLastToken().GetNextToken(includeZeroWidth: true);
      var nextTrivia = placed.Count == 0 || EndsWithBlankLine(pending)
         ? TrimLeadingBlankLines(next.LeadingTrivia)
         : next.LeadingTrivia;
      return root.ReplaceSyntax(
         [container],
         (_, rewritten) => list.With(rewritten, placed),
         [next],
         (_, rewritten) => rewritten.WithLeadingTrivia(pending.AddRange(nextTrivia)),
         [],
         (_, rewritten) => rewritten);
   }

   private static CompilationUnitSyntax AddToFileWithoutUsings(
      CompilationUnitSyntax unit,
      UsingDirectiveSyntax newUsing,
      UsingLayoutOptions options,
      ConditionalRegions regions)
   {
      var newLine = GetListNewLine(unit.Externs, options.NewLine);
      var next = unit.Externs.Count > 0
         ? unit.Externs[^1].GetLastToken().GetNextToken(includeZeroWidth: true)
         : unit.GetFirstToken(includeZeroWidth: true);
      var codeRegions = regions.EnclosingCode(unit, next);
      var headerLength = GetHeaderLength(next.LeadingTrivia, regions, codeRegions);
      var header = SyntaxFactory.TriviaList(next.LeadingTrivia.Take(headerLength));
      var nextTrivia = SyntaxFactory.TriviaList(next.LeadingTrivia.Skip(headerLength));
      EnsureNoSymbolDirective(nextTrivia);

      var placed = newUsing.WithLeadingTrivia(header).WithTrailingTrivia(SyntaxFactory.EndOfLine(newLine));
      if (options.SeparateGroups && unit.Externs.Count > 0)
         placed = placed.WithLeadingTrivia(SeparateGroup(placed.GetLeadingTrivia(), newLine));
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

   /// <summary>
   /// Returns the line break that Roslyn's Organize Imports adds to a list: the first one that ends an entry, as
   /// https://github.com/dotnet/roslyn/blob/c6e86165cb14cec562b68124c6cef927a4fc8fb7/src/Workspaces/SharedUtilitiesAndExtensions/Compiler/CSharp/Utilities/UsingsAndExternAliasesOrganizer.cs#L27-L31
   /// shows. UsingToolsListLineBreakTests pins it.
   /// </summary>
   /// <remarks>
   /// When no entry ends with a line break, Roslyn takes the one of its formatting options, and these methods take the
   /// document's.
   /// </remarks>
   /// <param name="entries">The extern aliases and usings of a container that remain, in the order of the file.</param>
   /// <returns>The line break, or <paramref name="fallback"/> when no entry ends with one.</returns>
   private static string GetListNewLine(IEnumerable<SyntaxNode> entries, string fallback) =>
      entries.SelectMany(e => e.GetTrailingTrivia())
         .Where(t => t.IsKind(SyntaxKind.EndOfLineTrivia))
         .Select(t => t.ToFullString())
         .FirstOrDefault() ?? fallback;

   /// <summary>
   /// Returns the leading trivia of an entry that starts a group, with a line break in front when it holds none.
   /// </summary>
   private static SyntaxTriviaList SeparateGroup(SyntaxTriviaList leading, string newLine) =>
      leading.Any(t => t.IsKind(SyntaxKind.EndOfLineTrivia))
         ? leading
         : leading.Insert(0, SyntaxFactory.EndOfLine(newLine));

   private static T WithoutLeadingLineBreaks<T>(T entry)
      where T : SyntaxNode =>
      entry.WithLeadingTrivia(TrimLeadingLineBreaks(entry.GetLeadingTrivia()));

   private static SyntaxTriviaList TrimLeadingLineBreaks(SyntaxTriviaList trivia) =>
      SyntaxFactory.TriviaList(trivia.SkipWhile(t => t.IsKind(SyntaxKind.EndOfLineTrivia)));

   /// <summary>
   /// Returns whether the entries of a container keep the line breaks above them. Roslyn removes them only from a list
   /// that it sorts, so from neither a split list nor a list in order.
   /// </summary>
   private static bool KeepsBlankLines(
      bool split,
      List<Run<ExternAliasDirectiveSyntax>> externRuns,
      List<Run<UsingDirectiveSyntax>> usingRuns,
      IComparer<UsingDirectiveSyntax> usingOrder) =>
      split || (IsInOrder(externRuns, ExternList.Order) && IsInOrder(usingRuns, usingOrder));

   /// <summary>
   /// Returns whether a directive splits the extern aliases and usings of a container, which Roslyn takes as one list.
   /// A directive splits the list when it stands above an entry that remains, other than the first, or inside an entry.
   /// </summary>
   /// <remarks>
   /// The lead of a run whose entries were all removed stays above the next entry that remains.
   /// </remarks>
   private static bool IsSplit(
      List<Run<ExternAliasDirectiveSyntax>> externRuns,
      List<Run<UsingDirectiveSyntax>> usingRuns)
   {
      var runs = externRuns.Select(r => (r.Lead, Entries: r.Entries.Select(e => (SyntaxNode)e.Entry).ToList()))
         .Concat(usingRuns.Select(r => (r.Lead, Entries: r.Entries.Select(e => (SyntaxNode)e.Entry).ToList())));
      var entryAbove = false;
      var directiveAbove = false;
      foreach (var (lead, entries) in runs)
      {
         directiveAbove |= entryAbove && lead.Any(t => t.IsDirective);
         if (entries.Count == 0)
            continue;

         // The entries of a run hold no directive in their leading trivia, so a directive they hold is inside one.
         if (directiveAbove || entries.Any(e => e.ContainsDirectives))
            return true;
         entryAbove = true;
      }

      return false;
   }

   private static bool IsInOrder<T>(List<Run<T>> runs, IComparer<T> order)
      where T : SyntaxNode =>
      runs.All(r => r.Entries.Zip(r.Entries.Skip(1)).All(p => order.Compare(p.First.Entry, p.Second.Entry) <= 0));

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

   private static bool EndsWithLineBreak(SyntaxTrivia trivia) => trivia.ToFullString() is [.., var last] && SyntaxFacts.IsNewLine(last);

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

   /// <summary>
   /// Orders usings as Roslyn's Organize Imports does: the global usings, then the others, each as namespace, static and
   /// alias usings in turn.
   /// </summary>
   /// <remarks>
   /// Alias usings sort by alias name, then by target. Every other using sorts by name, and a global:: qualifier does not
   /// count as part of the name.
   /// </remarks>
   /// <param name="systemFirst">Whether a name that starts with System, and the alias name System, sort first.</param>
   private sealed class UsingComparer(bool systemFirst) : IComparer<UsingDirectiveSyntax>
   {
      /// <summary>
      /// Returns whether a blank line separates two adjacent usings as Roslyn's Organize Imports separates groups.
      /// </summary>
      /// <remarks>
      /// The usings are in different groups when they are of different kinds, or when both are namespace usings whose
      /// names start with different identifiers. Identifiers compare by value, with case, and global:: counts as the
      /// identifier global. Being global does not count.
      /// </remarks>
      public static bool StartsGroup(UsingDirectiveSyntax previous, UsingDirectiveSyntax current) =>
         GetKind(previous) != GetKind(current)
         || (GetKind(current) == 0
            && previous.NamespaceOrType.GetFirstToken().ValueText != current.NamespaceOrType.GetFirstToken().ValueText);

      public int Compare(UsingDirectiveSyntax? x, UsingDirectiveSyntax? y)
      {
         if (x is null || y is null)
            return x is null ? (y is null ? 0 : -1) : 1;

         // The compiler requires the global usings of a file to precede its other usings.
         var compared = IsGlobal(y).CompareTo(IsGlobal(x));
         if (compared == 0)
            compared = GetKind(x).CompareTo(GetKind(y));
         if (compared != 0)
            return compared;

         if (x.Alias is { } xAlias && y.Alias is { } yAlias)
         {
            compared = NameOrder.CompareText(
               xAlias.Name.Identifier.ValueText, yAlias.Name.Identifier.ValueText, systemFirst);
            return compared != 0 ? compared : NameOrder.CompareAliasTargets(x.NamespaceOrType, y.NamespaceOrType);
         }

         return NameOrder.CompareUsingNames(x.NamespaceOrType, y.NamespaceOrType, systemFirst);
      }

      private static bool IsGlobal(UsingDirectiveSyntax usingDirective) =>
         !usingDirective.GlobalKeyword.IsKind(SyntaxKind.None);

      /// <summary>
      /// Returns 0 for a namespace using, 1 for a static using and 2 for an alias using. A static using that declares an
      /// alias counts as an alias using.
      /// </summary>
      private static int GetKind(UsingDirectiveSyntax usingDirective) =>
         usingDirective.Alias is not null ? 2 : usingDirective.StaticKeyword.IsKind(SyntaxKind.None) ? 0 : 1;
   }

   /// <summary>
   /// Compares names and types as Roslyn's Organize Imports does.
   /// </summary>
   /// <remarks>
   /// <para>
   /// A name compares part by part, and a name whose parts all match the start of the other sorts first. Two parts
   /// compare by identifier, then by the number of type arguments, then type argument by type argument. A type argument
   /// that is not a name compares by its first token, and the ?, [] and * of a type argument count for nothing.
   /// </para>
   /// <para>
   /// Roslyn has no fixed order between a part without type arguments and a generic part with the same identifier, and
   /// this class puts the part without type arguments first.
   /// </para>
   /// </remarks>
   private static class NameOrder
   {
      private static readonly CompareInfo Invariant = CultureInfo.InvariantCulture.CompareInfo;

      /// <summary>
      /// Compares the names of two usings that declare no alias, with a global:: qualifier left out.
      /// </summary>
      /// <param name="systemFirst">Whether the identifier System sorts first in the first part.</param>
      public static int CompareUsingNames(TypeSyntax x, TypeSyntax y, bool systemFirst) =>
         x is NameSyntax xName && y is NameSyntax yName
            ? CompareParts(GetParts(xName, skipGlobal: true), GetParts(yName, skipGlobal: true), systemFirst)
            : CompareTypes(x, y);

      /// <summary>
      /// Compares the targets of two aliases with one name. Roslyn compares only targets that are both names.
      /// </summary>
      public static int CompareAliasTargets(TypeSyntax x, TypeSyntax y) =>
         x is NameSyntax xName && y is NameSyntax yName ? CompareNames(xName, yName) : 0;

      /// <summary>
      /// Compares two identifiers ignoring case, accents and width, then with case, in the invariant culture so the
      /// current culture does not change the order.
      /// </summary>
      /// <param name="systemFirst">Whether the identifier System sorts before every other.</param>
      public static int CompareText(string x, string y, bool systemFirst)
      {
         if (systemFirst && (x == "System") != (y == "System"))
            return x == "System" ? -1 : 1;

         var ignoringCase = Invariant.Compare(
            x, y, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth);
         return ignoringCase != 0
            ? ignoringCase
            : Invariant.Compare(x, y, CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth);
      }

      private static int CompareTypes(TypeSyntax x, TypeSyntax y)
      {
         x = GetElementType(x);
         y = GetElementType(y);
         return x is NameSyntax xName && y is NameSyntax yName
            ? CompareNames(xName, yName)
            : CompareText(x.GetFirstToken().ValueText, y.GetFirstToken().ValueText, systemFirst: false);
      }

      private static int CompareNames(NameSyntax x, NameSyntax y) =>
         CompareParts(GetParts(x, skipGlobal: false), GetParts(y, skipGlobal: false), systemFirst: false);

      /// <param name="systemFirst">Whether the identifier System sorts first in the first part.</param>
      private static int CompareParts(List<SimpleNameSyntax> x, List<SimpleNameSyntax> y, bool systemFirst)
      {
         for (var i = 0; i < x.Count && i < y.Count; i++)
         {
            var compared = CompareText(x[i].Identifier.ValueText, y[i].Identifier.ValueText, systemFirst && i == 0);
            if (compared == 0)
               compared = x[i].Arity.CompareTo(y[i].Arity);
            if (compared == 0 && x[i] is GenericNameSyntax xGeneric && y[i] is GenericNameSyntax yGeneric)
               compared = CompareTypeArguments(xGeneric.TypeArgumentList.Arguments, yGeneric.TypeArgumentList.Arguments);

            if (compared != 0)
               return compared;
         }

         return x.Count.CompareTo(y.Count);
      }

      private static int CompareTypeArguments(SeparatedSyntaxList<TypeSyntax> x, SeparatedSyntaxList<TypeSyntax> y)
      {
         for (var i = 0; i < x.Count && i < y.Count; i++)
         {
            var compared = CompareTypes(x[i], y[i]);
            if (compared != 0)
               return compared;
         }

         return x.Count.CompareTo(y.Count);
      }

      /// <param name="skipGlobal">Whether to leave out a global:: qualifier.</param>
      private static List<SimpleNameSyntax> GetParts(NameSyntax name, bool skipGlobal)
      {
         var parts = new List<SimpleNameSyntax>();
         AddParts(name, parts);
         if (skipGlobal && parts[0].Identifier.IsKind(SyntaxKind.GlobalKeyword))
            parts.RemoveAt(0);

         return parts;
      }

      private static void AddParts(NameSyntax name, List<SimpleNameSyntax> parts)
      {
         switch (name)
         {
            case QualifiedNameSyntax qualified:
               AddParts(qualified.Left, parts);
               parts.Add(qualified.Right);
               break;
            case AliasQualifiedNameSyntax aliasQualified:
               parts.Add(aliasQualified.Alias);
               parts.Add(aliasQualified.Name);
               break;
            case SimpleNameSyntax simple:
               parts.Add(simple);
               break;
            default:
               throw new InvalidOperationException($"Unexpected name: {name.Kind()}");
         }
      }

      private static TypeSyntax GetElementType(TypeSyntax type)
      {
         while (true)
         {
            switch (type)
            {
               case NullableTypeSyntax nullable:
                  type = nullable.ElementType;
                  break;
               case ArrayTypeSyntax array:
                  type = array.ElementType;
                  break;
               case PointerTypeSyntax pointer:
                  type = pointer.ElementType;
                  break;
               default:
                  return type;
            }
         }
      }
   }

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

   private static SyntaxNode WithExterns(SyntaxNode container, IEnumerable<ExternAliasDirectiveSyntax> externs) =>
      container switch
      {
         CompilationUnitSyntax unit => unit.WithExterns(SyntaxFactory.List(externs)),
         BaseNamespaceDeclarationSyntax declaration => declaration.WithExterns(SyntaxFactory.List(externs)),
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

/// <summary>
/// The layout of the usings of one document.
/// </summary>
/// <param name="NewLine">
/// The line break of the document: the one that ends its first line, or its formatting option when it has one line.
/// </param>
/// <param name="SystemFirst">Whether usings of System and its namespaces sort before the others.</param>
/// <param name="SeparateGroups">Whether a blank line separates groups of usings.</param>
internal sealed record UsingLayoutOptions(string NewLine, bool SystemFirst, bool SeparateGroups);
