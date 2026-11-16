using System.ComponentModel;
using System.Globalization;
using System.Text;
using DotLens.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Search;

[McpServerToolType]
[DotLensWorkerTool]
public class GetFileOutlineTool
{
   private const int LineNumberWidth = 5;
   private const int IndentSpaces = 3;

   private static readonly string[] SuggestedNextTools =
   [
      "get_method_source to read a specific method's full implementation",
      "get_symbol_info for detailed semantic info about a symbol",
      "find_references to find all usages of a symbol"
   ];

   private readonly IReadOnlyWorkspace _workspace;

   public GetFileOutlineTool(IReadOnlyWorkspace workspace)
   {
      _workspace = workspace;
   }

   [McpServerTool(Name = "get_file_outline", ReadOnly = true, OpenWorld = false)]
   [Description("Get file outline as a C# skeleton with line numbers showing type declarations, member signatures, and hierarchy.")]
   public async Task<ToolResult<FileOutlineResponse>> GetFileOutlineAsync(
      [Description("File path (absolute or solution-relative).")] string filePath,
      [Description("Include private members.")]
      bool includePrivate = true,
      [Description("Include XML documentation comments above each declaration.")]
      bool includeDocComments = false,
      CancellationToken ct = default)
   {
      _ = _workspace.EnsureSolutionLoaded();

      var document = await _workspace.GetDocumentAsync(filePath, ct);

      var root = await document.GetSyntaxRootAsync(ct);
      if (root == null)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            "Could not analyze file",
            "The file may not be part of the loaded solution.");
      }

      var semanticModel = await document.GetSemanticModelAsync(ct);

      var builder = new StringBuilder();
      var visitor = new OutlineVisitor(builder, semanticModel, includePrivate, includeDocComments, LineNumberWidth, IndentSpaces);
      visitor.Visit(root);

      var formattedPath = document.FilePath != null
         ? _workspace.FormatPath(document.FilePath)
         : filePath;

      return ToolResult.Create(
         new FileOutlineResponse
         {
            FilePath = formattedPath,
            Outline = builder.ToString().TrimEnd()
         },
         suggestedNextTools: SuggestedNextTools);
   }

   private sealed class OutlineVisitor : CSharpSyntaxWalker
   {
      private readonly StringBuilder _builder;
      private readonly SemanticModel? _semanticModel;
      private readonly bool _includePrivate;
      private readonly bool _includeDocComments;
      private readonly int _lineNumberWidth;
      private readonly int _indentSpaces;
      private readonly string _lineNumberPadding;
      private int _indentLevel;

      public OutlineVisitor(
         StringBuilder builder,
         SemanticModel? semanticModel,
         bool includePrivate,
         bool includeDocComments,
         int lineNumberWidth,
         int indentSpaces)
      {
         _builder = builder;
         _semanticModel = semanticModel;
         _includePrivate = includePrivate;
         _includeDocComments = includeDocComments;
         _lineNumberWidth = lineNumberWidth;
         _indentSpaces = indentSpaces;
         _lineNumberPadding = new string(' ', lineNumberWidth);
      }

      private void AppendLine(int line, string text)
      {
         _builder.Append(line.ToString(CultureInfo.InvariantCulture).PadLeft(_lineNumberWidth));
         _builder.Append(": ");
         _builder.Append(' ', _indentLevel * _indentSpaces);
         _builder.AppendLine(text);
      }

      private void AppendDocComment(SyntaxNode node)
      {
         if (!_includeDocComments)
            return;

         var trivia = node.GetLeadingTrivia();
         foreach (var t in trivia)
         {
            if (t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            {
               // ToString leaves out the opening "///", which is leading trivia inside the comment's structure.
               var comment = t.ToFullString().TrimEnd();
               var sourceIndent = node.SyntaxTree.GetLineSpan(t.FullSpan).StartLinePosition.Character;
               var indent = new string(' ', _indentLevel * _indentSpaces);
               foreach (var commentLine in comment.Split('\n'))
               {
                  var trimmedLine = commentLine.TrimEnd('\r');
                  var leadingWhitespace = trimmedLine.Length - trimmedLine.TrimStart().Length;
                  trimmedLine = trimmedLine[Math.Min(leadingWhitespace, sourceIndent)..];
                  _builder.Append(_lineNumberPadding);
                  _builder.Append(": ");
                  _builder.Append(indent);
                  _builder.AppendLine(trimmedLine);
               }
            }
         }
      }

      private static int GetLine(SyntaxNode node) =>
         node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

      /// <summary>
      /// Checks if a declaration is private using the semantic model.
      /// This correctly handles all C# accessibility rules including interface members (public by default).
      /// </summary>
      private bool IsPrivate(SyntaxNode node)
      {
         if (_semanticModel == null)
            return false; // If no semantic model, include everything

         // A field or event field declares its symbols on its variables, which all share its modifiers.
         var declaration = node is BaseFieldDeclarationSyntax field ? field.Declaration.Variables.FirstOrDefault() : node;
         if (declaration == null)
            return false;

         var symbol = _semanticModel.GetDeclaredSymbol(declaration);
         if (symbol == null)
            return false; // If we can't get symbol info, include it

         return symbol.DeclaredAccessibility == Accessibility.Private;
      }

      public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
      {
         AppendDocComment(node);
         AppendLine(GetLine(node), $"namespace {OneLine(node.Name)}");
         AppendLine(node.OpenBraceToken.GetLocation().GetLineSpan().StartLinePosition.Line + 1, "{");
         _indentLevel++;
         base.VisitNamespaceDeclaration(node);
         _indentLevel--;
         AppendLine(node.CloseBraceToken.GetLocation().GetLineSpan().StartLinePosition.Line + 1, "}");
      }

      public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
      {
         AppendDocComment(node);
         AppendLine(GetLine(node), $"namespace {OneLine(node.Name)};");
         _builder.AppendLine();
         base.VisitFileScopedNamespaceDeclaration(node);
      }

      public override void VisitClassDeclaration(ClassDeclarationSyntax node) =>
         VisitTypeDeclaration(node, "class");

      public override void VisitStructDeclaration(StructDeclarationSyntax node) =>
         VisitTypeDeclaration(node, "struct");

      public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) =>
         VisitTypeDeclaration(node, "interface");

      public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
      {
         var keyword = node.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "record struct" : "record";
         VisitTypeDeclaration(node, keyword);
      }

      public override void VisitUnionDeclaration(UnionDeclarationSyntax node) =>
         VisitTypeDeclaration(node, "union");

      public override void VisitExtensionBlockDeclaration(ExtensionBlockDeclarationSyntax node) =>
         VisitTypeDeclaration(node, "extension");

      public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
      {
         if (!_includePrivate && IsPrivate(node))
            return;

         AppendDocComment(node);
         var modifiers = OneLine(node.Modifiers);
         var baseType = FormatBaseList(node.BaseList);
         AppendLine(GetLine(node), $"{modifiers} enum {node.Identifier}{baseType}".TrimStart());
         AppendLine(node.OpenBraceToken.GetLocation().GetLineSpan().StartLinePosition.Line + 1, "{");

         _indentLevel++;
         foreach (var member in node.Members)
         {
            var value = member.EqualsValue != null ? $" = {OneLine(member.EqualsValue.Value)}" : "";
            AppendLine(GetLine(member), $"{member.Identifier}{value},");
         }
         _indentLevel--;

         AppendLine(node.CloseBraceToken.GetLocation().GetLineSpan().StartLinePosition.Line + 1, "}");
      }

      private void VisitTypeDeclaration(TypeDeclarationSyntax node, string keyword)
      {
         if (!_includePrivate && IsPrivate(node))
            return;

         AppendDocComment(node);
         var modifiers = OneLine(node.Modifiers);
         var typeParamsStr = OneLine(node.TypeParameterList);
         var paramsStr = OneLine(node.ParameterList);
         var baseListStr = FormatBaseList(node.BaseList);
         var constraintsStr = FormatConstraints(node.ConstraintClauses);

         // An extension block has no name, so its type parameters follow the keyword directly.
         var nameStr = node.Identifier.IsKind(SyntaxKind.None) ? "" : $" {node.Identifier}";

         var header = $"{modifiers} {keyword}{nameStr}{typeParamsStr}{paramsStr}{baseListStr}{constraintsStr}".TrimStart();

         // A declaration without a body, such as a positional record, has no brace tokens to locate.
         if (node.OpenBraceToken.IsKind(SyntaxKind.None))
         {
            AppendLine(GetLine(node), header + ";");
            return;
         }

         AppendLine(GetLine(node), header);
         AppendLine(node.OpenBraceToken.GetLocation().GetLineSpan().StartLinePosition.Line + 1, "{");

         _indentLevel++;
         // Visit only the members, not the type itself (to avoid infinite recursion)
         foreach (var member in node.Members)
         {
            Visit(member);
         }
         _indentLevel--;

         AppendLine(node.CloseBraceToken.GetLocation().GetLineSpan().StartLinePosition.Line + 1, "}");
      }

      public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
      {
         if (!_includePrivate && IsPrivate(node))
            return;

         AppendDocComment(node);
         var modifiers = OneLine(node.Modifiers);
         var typeParams = OneLine(node.TypeParameterList);
         var constraints = FormatConstraints(node.ConstraintClauses);

         AppendLine(GetLine(node), $"{modifiers} {OneLine(node.ReturnType)} {node.Identifier}{typeParams}{OneLine(node.ParameterList)}{constraints};".TrimStart());
      }

      public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
      {
         if (!_includePrivate && IsPrivate(node))
            return;

         AppendDocComment(node);
         var modifiers = OneLine(node.Modifiers);
         var initializer = node.Initializer != null ? $" : {node.Initializer.ThisOrBaseKeyword}(...)" : "";

         AppendLine(GetLine(node), $"{modifiers} {node.Identifier}{OneLine(node.ParameterList)}{initializer};".TrimStart());
      }

      public override void VisitDestructorDeclaration(DestructorDeclarationSyntax node)
      {
         if (!_includePrivate && IsPrivate(node))
            return;

         AppendDocComment(node);
         AppendLine(GetLine(node), $"~{node.Identifier}();");
      }

      public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
      {
         if (!_includePrivate && IsPrivate(node))
            return;

         AppendDocComment(node);
         var modifiers = OneLine(node.Modifiers);
         var suffix = FormatPropertyBody(node.AccessorList, node.ExpressionBody);

         AppendLine(GetLine(node), $"{modifiers} {OneLine(node.Type)} {node.Identifier}{suffix}".TrimStart());
      }

      public override void VisitIndexerDeclaration(IndexerDeclarationSyntax node)
      {
         if (!_includePrivate && IsPrivate(node))
            return;

         AppendDocComment(node);
         var modifiers = OneLine(node.Modifiers);
         var suffix = FormatPropertyBody(node.AccessorList, node.ExpressionBody);

         AppendLine(GetLine(node), $"{modifiers} {OneLine(node.Type)} this{OneLine(node.ParameterList)}{suffix}".TrimStart());
      }

      public override void VisitEventDeclaration(EventDeclarationSyntax node)
      {
         if (!_includePrivate && IsPrivate(node))
            return;

         AppendDocComment(node);
         var modifiers = OneLine(node.Modifiers);
         AppendLine(GetLine(node), $"{modifiers} event {OneLine(node.Type)} {node.Identifier} {{ add; remove; }}".TrimStart());
      }

      public override void VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
      {
         if (!_includePrivate && IsPrivate(node))
            return;

         AppendDocComment(node);
         var modifiers = OneLine(node.Modifiers);
         var names = string.Join(", ", node.Declaration.Variables.Select(v => v.Identifier.ToString()));
         AppendLine(GetLine(node), $"{modifiers} event {OneLine(node.Declaration.Type)} {names};".TrimStart());
      }

      public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
      {
         if (!_includePrivate && IsPrivate(node))
            return;

         AppendDocComment(node);
         var modifiers = OneLine(node.Modifiers);
         var names = string.Join(", ", node.Declaration.Variables.Select(v => v.Identifier.ToString()));
         AppendLine(GetLine(node), $"{modifiers} {OneLine(node.Declaration.Type)} {names};".TrimStart());
      }

      public override void VisitDelegateDeclaration(DelegateDeclarationSyntax node)
      {
         if (!_includePrivate && IsPrivate(node))
            return;

         AppendDocComment(node);
         var modifiers = OneLine(node.Modifiers);
         var typeParams = OneLine(node.TypeParameterList);
         var constraints = FormatConstraints(node.ConstraintClauses);

         AppendLine(GetLine(node), $"{modifiers} delegate {OneLine(node.ReturnType)} {node.Identifier}{typeParams}{OneLine(node.ParameterList)}{constraints};".TrimStart());
      }

      public override void VisitOperatorDeclaration(OperatorDeclarationSyntax node)
      {
         if (!_includePrivate && IsPrivate(node))
            return;

         AppendDocComment(node);
         var modifiers = OneLine(node.Modifiers);
         AppendLine(GetLine(node), $"{modifiers} {OneLine(node.ReturnType)} operator {node.OperatorToken}{OneLine(node.ParameterList)};".TrimStart());
      }

      public override void VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
      {
         if (!_includePrivate && IsPrivate(node))
            return;

         AppendDocComment(node);
         var modifiers = OneLine(node.Modifiers);
         var implicitExplicit = node.ImplicitOrExplicitKeyword.ToString();
         AppendLine(GetLine(node), $"{modifiers} {implicitExplicit} operator {OneLine(node.Type)}{OneLine(node.ParameterList)};".TrimStart());
      }

      private static string FormatBaseList(BaseListSyntax? baseList) =>
         baseList != null ? " : " + string.Join(", ", baseList.Types.Select(t => OneLine(t))) : "";

      private static string FormatConstraints(SyntaxList<TypeParameterConstraintClauseSyntax> clauses) =>
         clauses.Count > 0 ? " " + string.Join(" ", clauses.Select(c => OneLine(c))) : "";

      private static string OneLine(SyntaxNode? node) =>
         node != null ? OneLine(node.DescendantTokens()) : "";

      /// <summary>
      /// Returns the tokens' source text with the whitespace, line breaks and comments between two tokens replaced by one
      /// space. No space goes after an opening bracket, before a closing bracket or a comma, or around a dot or <c>::</c>.
      /// </summary>
      private static string OneLine(IEnumerable<SyntaxToken> tokens)
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

            text.Append(token.Text);
            previous = token;
         }

         return text.ToString();
      }

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

      private static string FormatPropertyBody(AccessorListSyntax? accessorList, ArrowExpressionClauseSyntax? expressionBody)
      {
         if (accessorList != null)
            return " " + FormatAccessors(accessorList);

         return expressionBody != null ? " => ...;" : "";
      }

      private static string FormatAccessors(AccessorListSyntax accessorList)
      {
         var parts = new List<string>();
         foreach (var accessor in accessorList.Accessors)
         {
            var mods = OneLine(accessor.Modifiers);
            var prefix = string.IsNullOrEmpty(mods) ? "" : mods + " ";
            parts.Add($"{prefix}{accessor.Keyword};");
         }
         return "{ " + string.Join(" ", parts) + " }";
      }

      // Don't descend into method bodies, property accessors, etc.
      public override void VisitBlock(BlockSyntax node) { }
      public override void VisitArrowExpressionClause(ArrowExpressionClauseSyntax node) { }
   }
}

public record FileOutlineResponse
{
   [Description("Absolute or relative path to the file.")]
   public required string FilePath { get; init; }

   [Description("C# skeleton showing file structure with line numbers. Contains type declarations, member signatures, and hierarchy without implementation details.")]
   public required string Outline { get; init; }
}
