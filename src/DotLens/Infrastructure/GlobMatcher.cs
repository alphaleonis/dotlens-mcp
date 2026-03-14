using System.Text.RegularExpressions;

namespace DotLens.Infrastructure;

/// <summary>
/// Matches strings against a glob pattern (supports * and ? wildcards).
/// Create one instance per pattern and reuse for multiple matches.
/// Uses compiled regex with timeout for ReDoS protection.
/// </summary>
public sealed class GlobMatcher
{
   private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

   private readonly Regex? _regex;
   private readonly bool _matchAll;

   /// <summary>
   /// Creates a new glob matcher for the specified pattern.
   /// </summary>
   /// <param name="pattern">
   /// Glob pattern with wildcards: * (any characters), ? (single character).
   /// Null or empty pattern matches everything.
   /// </param>
   public GlobMatcher(string? pattern)
   {
      if (string.IsNullOrEmpty(pattern))
      {
         _matchAll = true;
         return;
      }

      // Convert glob pattern to regex
      // Escape regex special chars except * and ?
      var regexPattern = "^" + Regex.Escape(pattern)
          .Replace("\\*", ".*")  // * matches any characters
          .Replace("\\?", ".")   // ? matches single character
          + "$";

      _regex = new Regex(regexPattern,
          RegexOptions.IgnoreCase | RegexOptions.Compiled,
          MatchTimeout);
   }

   /// <summary>
   /// Tests if the input string matches the glob pattern.
   /// </summary>
   public bool IsMatch(string input)
   {
      if (_matchAll)
         return true;
      if (_regex == null)
         return false;

      try
      {
         return _regex.IsMatch(input);
      }
      catch (RegexMatchTimeoutException)
      {
         // Pattern took too long - treat as no match
         return false;
      }
   }
}
