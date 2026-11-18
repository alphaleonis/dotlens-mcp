using System.Text;
using System.Text.RegularExpressions;

namespace DotLens.Infrastructure;

/// <summary>
/// Prints declaration syntax on one line, as the file outline and full declarations show it.
/// </summary>
internal static partial class SyntaxText
{
   internal static string OneLine(SyntaxNode? node) =>
      node != null ? OneLine(node.DescendantTokens()) : "";

   /// <summary>
   /// Returns the tokens' source text with the whitespace, line breaks and comments between two tokens replaced by one
   /// space. No space goes after an opening bracket, before a closing bracket or a comma, or around a dot or <c>::</c>.
   /// </summary>
   internal static string OneLine(IEnumerable<SyntaxToken> tokens)
   {
      var text = new StringBuilder();
      SyntaxToken? previous = null;
      foreach (var token in tokens)
      {
         if (previous is { } before
            && (before.HasTrailingTrivia || token.HasLeadingTrivia)
            && !IsSpaceFreeAfter(before)
            && !IsSpaceFreeBefore(token))
         {
            text.Append(' ');
         }

         text.Append(SingleLineText(token));
         previous = token;
      }

      return text.ToString();
   }

   /// <summary>
   /// Returns the token's text, with a string written across lines turned into a regular string literal whose
   /// escapes stand for its line breaks, the form Roslyn's symbol display gives a default value.
   /// </summary>
   private static string SingleLineText(SyntaxToken token)
   {
      if (GetMultiLineInterpolatedString(token) is { } interpolated)
         return InterpolatedPartText(token, interpolated);

      if (!HasLineBreak(token.Text))
         return token.Text;

      return token.Kind() switch
      {
         SyntaxKind.StringLiteralToken or SyntaxKind.MultiLineRawStringLiteralToken =>
            SymbolDisplay.FormatLiteral(token.ValueText, quote: true),
         SyntaxKind.Utf8StringLiteralToken or SyntaxKind.Utf8MultiLineRawStringLiteralToken =>
            SymbolDisplay.FormatLiteral(token.ValueText, quote: true) + "u8",
         _ => LineBreakRun().Replace(token.Text, " "),
      };
   }

   /// <summary>
   /// Returns the interpolated string written across lines that the token belongs to as a delimiter, a text part,
   /// an interpolation brace or a format, or null.
   /// </summary>
   private static InterpolatedStringExpressionSyntax? GetMultiLineInterpolatedString(SyntaxToken token)
   {
      var interpolated = token.Parent switch
      {
         InterpolatedStringExpressionSyntax expression => expression,
         InterpolatedStringTextSyntax { Parent: InterpolatedStringExpressionSyntax expression } => expression,
         InterpolationSyntax { Parent: InterpolatedStringExpressionSyntax expression }
            when token.IsKind(SyntaxKind.OpenBraceToken) || token.IsKind(SyntaxKind.CloseBraceToken) => expression,
         InterpolationFormatClauseSyntax { Parent.Parent: InterpolatedStringExpressionSyntax expression }
            when token.IsKind(SyntaxKind.InterpolatedStringTextToken) => expression,
         _ => null,
      };

      return interpolated is not null && IsWrittenAcrossLines(interpolated) ? interpolated : null;
   }

   private static bool IsWrittenAcrossLines(InterpolatedStringExpressionSyntax interpolated) =>
      interpolated.StringStartToken.IsKind(SyntaxKind.InterpolatedMultiLineRawStringStartToken)
      || interpolated.Contents.OfType<InterpolatedStringTextSyntax>().Any(t => HasLineBreak(t.TextToken.Text));

   private static string InterpolatedPartText(SyntaxToken token, InterpolatedStringExpressionSyntax interpolated)
   {
      if (token == interpolated.StringStartToken)
         return "$\"";
      if (token == interpolated.StringEndToken)
         return "\"";
      if (token.Parent is InterpolationSyntax)
         return token.IsKind(SyntaxKind.OpenBraceToken) ? "{" : "}";

      // A verbatim string's text keeps its literal braces doubled; a raw string's text holds them single.
      var escaped = EscapeStringContent(token.ValueText);
      var isRaw = interpolated.StringStartToken.IsKind(SyntaxKind.InterpolatedMultiLineRawStringStartToken);
      return isRaw && token.Parent is not InterpolationFormatClauseSyntax ? escaped.Replace("{", "{{").Replace("}", "}}") : escaped;
   }

   private static string EscapeStringContent(string value) =>
      SymbolDisplay.FormatLiteral(value, quote: true)[1..^1];

   private static bool HasLineBreak(string text) => text.Contains('\n') || text.Contains('\r');

   [GeneratedRegex(@"[ \t]*(?:\r\n|\r|\n)\s*")]
   private static partial Regex LineBreakRun();

   private static bool IsSpaceFreeAfter(SyntaxToken token) => token.Kind() switch
   {
      SyntaxKind.OpenParenToken or SyntaxKind.OpenBracketToken or SyntaxKind.DotToken or SyntaxKind.ColonColonToken => true,
      SyntaxKind.LessThanToken => IsTypeListAngleBracket(token),
      _ => false,
   };

   private static bool IsSpaceFreeBefore(SyntaxToken token) => token.Kind() switch
   {
      SyntaxKind.CloseParenToken or SyntaxKind.CloseBracketToken or SyntaxKind.CommaToken or SyntaxKind.DotToken or SyntaxKind.ColonColonToken => true,
      SyntaxKind.GreaterThanToken => IsTypeListAngleBracket(token),
      _ => false,
   };

   // In a constant expression, such as a default parameter value, "<" and ">" compare and keep their spaces.
   private static bool IsTypeListAngleBracket(SyntaxToken token) =>
      token.Parent is TypeParameterListSyntax or TypeArgumentListSyntax or FunctionPointerParameterListSyntax;
}
