using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace DotLens.Infrastructure;

/// <summary>
/// Reads the Compile items that a project file lists a source file in, and its text without them.
/// </summary>
/// <remarks>
/// It reads only the project file's own XML, so it does not see the items that imported files and SDK globs add.
/// It edits the text in place, so the rest of the file keeps its bytes.
/// </remarks>
internal sealed partial class ProjectFileCompileItems
{
   private readonly string _projectDirectory;
   private readonly string _text;
   private readonly Encoding _encoding;
   private readonly byte[] _preamble;
   private readonly List<ItemGroup> _itemGroups;

   private ProjectFileCompileItems(
      string projectPath, byte[] originalBytes, string text, Encoding encoding, byte[] preamble, List<ItemGroup> itemGroups)
   {
      OriginalBytes = originalBytes;
      _projectDirectory = Path.GetDirectoryName(projectPath) ?? throw new ArgumentException("The project path has no directory", nameof(projectPath));
      _text = text;
      _encoding = encoding;
      _preamble = preamble;
      _itemGroups = itemGroups;
   }

   /// <summary>
   /// Whether a Compile item includes a path that only MSBuild's evaluation can resolve, such as a property.
   /// </summary>
   public bool HasUnresolvedIncludes => _itemGroups.SelectMany(g => g.Items).Any(i => i.HasUnresolvedEntry);

   /// <exception cref="XmlException">
   /// The project file is not well-formed XML, or its encoding cannot write its text back to the same bytes.
   /// </exception>
   public static ProjectFileCompileItems Read(string projectPath)
   {
      var bytes = File.ReadAllBytes(projectPath);
      var encoding = DetectEncoding(bytes);
      var preamble = encoding.GetPreamble();
      if (preamble.Length == 0 || !bytes.AsSpan().StartsWith(preamble))
         preamble = [];

      var text = encoding.GetString(bytes, preamble.Length, bytes.Length - preamble.Length);
      if (!encoding.GetBytes(text).AsSpan().SequenceEqual(bytes.AsSpan(preamble.Length)))
         throw new XmlException($"The project file's bytes do not round-trip through its encoding {encoding.WebName}");

      return new ProjectFileCompileItems(projectPath, bytes, text, encoding, preamble, ParseItemGroups(text));
   }

   /// <summary>
   /// The project file's bytes when it was read.
   /// </summary>
   public byte[] OriginalBytes { get; }

   /// <summary>
   /// Returns how the project file's Compile items list the file. An entry with a wildcard does not count.
   /// </summary>
   public CompileItemListing Find(string filePath)
   {
      var listing = CompileItemListing.NotListed;
      foreach (var item in _itemGroups.SelectMany(g => g.Items))
      {
         if (!Lists(item, filePath))
            continue;
         if (item.Entries.Length > 1)
            return CompileItemListing.ListedWithOtherFiles;
         listing = CompileItemListing.Listed;
      }

      return listing;
   }

   /// <summary>
   /// The project file's text when it was read, without a byte order mark.
   /// </summary>
   public string Text => _text;

   /// <summary>
   /// Returns the text without each Compile item that lists one of the files as its only entry.
   /// </summary>
   /// <param name="filePaths">The files whose items all go.</param>
   /// <param name="listedOnce">The files whose items go except the first one.</param>
   /// <remarks>
   /// An item group left without elements goes too. An element that fills its lines takes those lines with it.
   /// </remarks>
   public string TextWithout(IReadOnlyCollection<string> filePaths, IReadOnlyCollection<string>? listedOnce = null)
   {
      var kept = new HashSet<string>(PathUtils.PathComparer);
      bool Removes(CompileItem item)
      {
         if (item.Entries.Length != 1)
            return false;
         if (filePaths.Any(p => Lists(item, p)))
            return true;
         return listedOnce?.FirstOrDefault(p => Lists(item, p)) is { } once && !kept.Add(once);
      }

      var spans = new List<(int Start, int End)>();
      foreach (var group in _itemGroups.OrderBy(g => g.Start))
      {
         var removed = group.Items.Where(Removes).ToList();
         if (removed.Count == 0)
            continue;

         if (removed.Count == group.ChildElementCount)
            spans.Add(ExpandToLines(group.Start, group.End));
         else
            spans.AddRange(removed.Select(i => ExpandToLines(i.Start, i.End)));
      }

      var text = new StringBuilder(_text);
      foreach (var (start, end) in spans.OrderByDescending(s => s.Start))
         text.Remove(start, end - start);
      return text.ToString();
   }

   /// <summary>
   /// Returns the bytes of the text in the project file's encoding, with its byte order mark when it had one.
   /// </summary>
   public byte[] Encode(string text) => [.. _preamble, .. _encoding.GetBytes(text)];

   private bool Lists(CompileItem item, string filePath) =>
      item.Entries.Any(entry => !IsUnresolved(entry) && !IsWildcard(entry) &&
         string.Equals(ResolveEntry(entry), filePath, PathUtils.PathComparison));

   private string ResolveEntry(string entry)
   {
      var path = Unescape(entry);
      if (Path.DirectorySeparatorChar != '\\')
         path = path.Replace('\\', Path.DirectorySeparatorChar);
      return Path.GetFullPath(Path.Combine(_projectDirectory, path));
   }

   private (int Start, int End) ExpandToLines(int start, int end)
   {
      var lineStart = start;
      while (lineStart > 0 && _text[lineStart - 1] is ' ' or '\t')
         lineStart--;
      var lineEnd = end;
      while (lineEnd < _text.Length && _text[lineEnd] is ' ' or '\t')
         lineEnd++;

      var startsLine = lineStart == 0 || _text[lineStart - 1] is '\n' or '\r';
      if (!startsLine)
         return (start, end);
      if (lineEnd == _text.Length)
         return (lineStart, lineEnd);
      if (_text[lineEnd] == '\r' && lineEnd + 1 < _text.Length && _text[lineEnd + 1] == '\n')
         return (lineStart, lineEnd + 2);
      if (_text[lineEnd] is '\n' or '\r')
         return (lineStart, lineEnd + 1);
      return (start, end);
   }

   /// <summary>
   /// Returns the encoding that a byte order mark or the XML declaration names, or UTF-8 when neither does.
   /// </summary>
   private static Encoding DetectEncoding(byte[] bytes)
   {
      foreach (var withBom in new Encoding[] { new UTF8Encoding(true), new UnicodeEncoding(false, true), new UnicodeEncoding(true, true), new UTF32Encoding(false, true), new UTF32Encoding(true, true) })
      {
         var preamble = withBom.GetPreamble();
         if (bytes.AsSpan().StartsWith(preamble))
            return withBom;
      }

      // The declaration is ASCII in every encoding that MSBuild reads without a byte order mark.
      var declaration = XmlDeclarationEncoding().Match(Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 256)));
      if (!declaration.Success)
         return new UTF8Encoding(false);

      var name = declaration.Groups[1].Value;
      return CodePagesEncodingProvider.Instance.GetEncoding(name) ?? GetBuiltInEncoding(name)
         ?? throw new XmlException($"The project file declares the encoding {name}, which DotLens cannot read");

      static Encoding? GetBuiltInEncoding(string name)
      {
         try
         {
            var encoding = Encoding.GetEncoding(name);
            return encoding is UTF8Encoding ? new UTF8Encoding(false) : encoding;
         }
         catch (ArgumentException)
         {
            return null;
         }
      }
   }

   [GeneratedRegex("""^\s*<\?xml[^>]*?\sencoding\s*=\s*["']([^"']+)["']""")]
   private static partial Regex XmlDeclarationEncoding();

   private static bool IsUnresolved(string entry) =>
      entry.Contains("$(", StringComparison.Ordinal) || entry.Contains("@(", StringComparison.Ordinal) || entry.Contains("%(", StringComparison.Ordinal);

   private static bool IsWildcard(string entry) => entry.Contains('*', StringComparison.Ordinal) || entry.Contains('?', StringComparison.Ordinal);

   // MSBuild escapes special characters in item specs as %XX, with XX in hexadecimal.
   private static string Unescape(string entry)
   {
      if (!entry.Contains('%', StringComparison.Ordinal))
         return entry;

      var result = new StringBuilder(entry.Length);
      for (var i = 0; i < entry.Length; i++)
      {
         if (entry[i] == '%' && i + 2 < entry.Length && Uri.IsHexDigit(entry[i + 1]) && Uri.IsHexDigit(entry[i + 2]))
         {
            result.Append((char)Convert.ToInt32(entry.Substring(i + 1, 2), 16));
            i += 2;
         }
         else
         {
            result.Append(entry[i]);
         }
      }

      return result.ToString();
   }

   private static List<ItemGroup> ParseItemGroups(string text)
   {
      var lineStarts = LineStarts(text);
      var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
      using var reader = XmlReader.Create(new StringReader(text), settings);
      var lineInfo = (IXmlLineInfo)reader;

      var groups = new List<ItemGroup>();
      var open = new Stack<OpenElement>();
      while (reader.Read())
      {
         if (reader.NodeType == XmlNodeType.Element)
         {
            var nameOffset = lineStarts[lineInfo.LineNumber - 1] + lineInfo.LinePosition - 1;
            var start = nameOffset - 1;
            if (start < 0 || text[start] != '<')
               throw new XmlException($"Could not locate the element {reader.LocalName} in the project file");

            var parent = open.Count > 0 ? open.Peek() : null;
            var insideTarget = parent is { InsideTarget: true } || reader.LocalName == "Target";
            if (parent is not null)
               parent.ChildElementCount++;

            OpenElement element;
            if (!insideTarget && reader.LocalName == "ItemGroup")
            {
               element = new OpenElement(start, insideTarget, new ItemGroup());
            }
            else if (!insideTarget && reader.LocalName == "Compile" && parent?.Group is { } group &&
               reader.GetAttribute("Include") is { } include)
            {
               var entries = include.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
               element = new OpenElement(start, insideTarget, null) { Item = new CompileItem(entries) };
               group.Items.Add(element.Item);
            }
            else
            {
               element = new OpenElement(start, insideTarget, null);
            }

            if (reader.IsEmptyElement)
               Close(element, TagEnd(text, nameOffset));
            else
               open.Push(element);
         }
         else if (reader.NodeType == XmlNodeType.EndElement)
         {
            var nameOffset = lineStarts[lineInfo.LineNumber - 1] + lineInfo.LinePosition - 1;
            Close(open.Pop(), TagEnd(text, nameOffset));
         }
      }

      return groups;

      void Close(OpenElement element, int end)
      {
         if (element.Item is { } item)
         {
            item.Start = element.Start;
            item.End = end;
         }
         else if (element.Group is { } group)
         {
            group.Start = element.Start;
            group.End = end;
            group.ChildElementCount = element.ChildElementCount;
            groups.Add(group);
         }
      }
   }

   /// <returns>The offset just past the '>' that closes the tag whose name starts at the offset.</returns>
   private static int TagEnd(string text, int nameOffset)
   {
      char? quote = null;
      for (var i = nameOffset; i < text.Length; i++)
      {
         var c = text[i];
         if (quote is { } q)
         {
            if (c == q)
               quote = null;
         }
         else if (c is '"' or '\'')
         {
            quote = c;
         }
         else if (c == '>')
         {
            return i + 1;
         }
      }

      throw new XmlException("A tag in the project file has no closing '>'");
   }

   // XmlReader counts "\r\n", "\r" and "\n" each as one line break.
   private static List<int> LineStarts(string text)
   {
      var starts = new List<int> { 0 };
      for (var i = 0; i < text.Length; i++)
      {
         if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            i++;
         if (text[i] is '\n' or '\r')
            starts.Add(i + 1);
      }

      return starts;
   }

   private sealed class OpenElement(int start, bool insideTarget, ItemGroup? group)
   {
      public int Start { get; } = start;
      public bool InsideTarget { get; } = insideTarget;
      public ItemGroup? Group { get; } = group;
      public CompileItem? Item { get; init; }
      public int ChildElementCount { get; set; }
   }

   private sealed class ItemGroup
   {
      public List<CompileItem> Items { get; } = [];
      public int Start { get; set; }
      public int End { get; set; }
      public int ChildElementCount { get; set; }
   }

   private sealed class CompileItem(string[] entries)
   {
      public string[] Entries { get; } = entries;
      public bool HasUnresolvedEntry => Entries.Any(IsUnresolved);
      public int Start { get; set; }
      public int End { get; set; }
   }
}

/// <summary>
/// How a project file's own Compile items list a source file.
/// </summary>
internal enum CompileItemListing
{
   NotListed,
   Listed,

   /// <summary>A Compile item lists the file together with other entries in its Include attribute.</summary>
   ListedWithOtherFiles,
}
