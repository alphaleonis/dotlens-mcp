using DotLens.Infrastructure;
using DotLens.Tools.Editing;
using DotLens.Tools.Formatting;
using DotLens.Tools.Refactoring;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DotLens.Tests.Refactoring;

/// <summary>
/// Covers that add_using and organize_usings keep the file's own line ending, keep the file header first, and leave
/// no blank first line.
/// </summary>
/// <remarks>
/// Each test writes its source with the line ending under test, so the text does not depend on the checkout's endings.
/// </remarks>
public class UsingToolsLineEndingsTests : IsolatedProjectTestBase
{
   public static TheoryData<string> NewLines => ["\n", "\r\n"];

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_AddUsing_ExistingUsings_KeepsFileLineEnding(string newLine)
   {
      await AssertAddUsingAsync(newLine, "System.Collections.Concurrent", """
         using System.Text;

         namespace CoreTestProject.LineEndings;

         public class AddToExisting
         {
         }

         """, """
         using System.Collections.Concurrent;
         using System.Text;

         namespace CoreTestProject.LineEndings;

         public class AddToExisting
         {
         }

         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_AddUsing_NoUsings_SeparatesUsingFromNamespace(string newLine)
   {
      await AssertAddUsingAsync(newLine, "System.Text", """
         namespace CoreTestProject.LineEndings;

         public class AddToEmpty
         {
         }

         """, """
         using System.Text;

         namespace CoreTestProject.LineEndings;

         public class AddToEmpty
         {
         }

         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_AddUsing_NoUsingsAfterFileHeader_AddsUsingBelowHeader(string newLine)
   {
      await AssertAddUsingAsync(newLine, "System.Text", """
         // File header

         namespace CoreTestProject.LineEndings;

         public class AddBelowHeader
         {
         }

         """, """
         // File header

         using System.Text;

         namespace CoreTestProject.LineEndings;

         public class AddBelowHeader
         {
         }

         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_AddUsing_NoUsingsAfterDefineDirective_AddsUsingBelowDirective(string newLine)
   {
      await AssertAddUsingAsync(newLine, "System.Text", """
         #define LINE_ENDINGS
         namespace CoreTestProject.LineEndings;

         public class AddBelowDirective
         {
         }

         """, """
         #define LINE_ENDINGS
         using System.Text;

         namespace CoreTestProject.LineEndings;

         public class AddBelowDirective
         {
         }

         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_AddUsing_NoUsingsBeforeDocComment_KeepsDocCommentOnType(string newLine)
   {
      await AssertAddUsingAsync(newLine, "System.Text", """
         /// <summary>A type in the global namespace.</summary>
         public class AddAboveDocComment
         {
         }

         """, """
         using System.Text;

         /// <summary>A type in the global namespace.</summary>
         public class AddAboveDocComment
         {
         }

         """);
   }

   /// <summary>
   /// The using must stay outside the conditional block, so a build without the symbol still has it.
   /// </summary>
   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_AddUsing_NoUsingsBeforeIfDirective_AddsUsingAboveDirective(string newLine)
   {
      await AssertAddUsingAsync(newLine, "System.Text", """
         #if DEBUG

         public class AddAboveIf
         {
         }
         #endif

         """, """
         using System.Text;

         #if DEBUG

         public class AddAboveIf
         {
         }
         #endif

         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_AddUsing_SortsBeforeFirstUsing_KeepsHeaderFirst(string newLine)
   {
      await AssertAddUsingAsync(newLine, "System.Text", """
         // File header

         using CoreTestProject.Types;

         namespace CoreTestProject.LineEndings;

         public class AddAboveHeaderUsing
         {
         }

         """, """
         // File header

         using System.Text;
         using CoreTestProject.Types;

         namespace CoreTestProject.LineEndings;

         public class AddAboveHeaderUsing
         {
         }

         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_RemovesEveryUsing_FileScopedNamespace(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 2, """
         using System;
         using System.Linq;

         namespace CoreTestProject.LineEndings;

         public class OrganizeFileScoped
         {
         }

         """, """
         namespace CoreTestProject.LineEndings;

         public class OrganizeFileScoped
         {
         }

         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_RemovesEveryUsing_BlockNamespace(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 1, """
         using System;

         namespace CoreTestProject.LineEndings
         {
             public class OrganizeBlock
             {
             }
         }

         """, """
         namespace CoreTestProject.LineEndings
         {
             public class OrganizeBlock
             {
             }
         }

         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_RemovesEveryUsing_NoNamespace(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 1, """
         using System;

         /// <summary>A type in the global namespace.</summary>
         public class OrganizeGlobal
         {
         }

         """, """
         /// <summary>A type in the global namespace.</summary>
         public class OrganizeGlobal
         {
         }

         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_RemovesEveryUsing_KeepsFileHeader(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 1, """
         // File header

         using System;

         namespace CoreTestProject.LineEndings;

         public class OrganizeHeader
         {
         }

         """, """
         // File header

         namespace CoreTestProject.LineEndings;

         public class OrganizeHeader
         {
         }

         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_RemovesEveryUsing_KeepsNullableDirective(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 1, """
         #nullable enable
         using System;

         namespace CoreTestProject.LineEndings;

         public class OrganizeNullable
         {
         }

         """, """
         #nullable enable
         namespace CoreTestProject.LineEndings;

         public class OrganizeNullable
         {
         }

         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_RemovesEveryUsing_KeepsPragmaDirective(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 1, """
         #pragma warning disable CS0168

         using System;

         namespace CoreTestProject.LineEndings;

         public class OrganizePragma
         {
         }

         """, """
         #pragma warning disable CS0168

         namespace CoreTestProject.LineEndings;

         public class OrganizePragma
         {
         }

         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_RemovesEveryUsing_FileOfOnlyUsings(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 1, """
         // File header
         using System;

         """, """
         // File header

         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_RemovesFirstUsing_KeepsHeaderOnNextUsing(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 1, """
         // File header

         using System;
         using CoreTestProject.Types;

         namespace CoreTestProject.LineEndings;

         public class OrganizeKeepOne
         {
             private SampleClass? _sample;
         }

         """, """
         // File header

         using CoreTestProject.Types;

         namespace CoreTestProject.LineEndings;

         public class OrganizeKeepOne
         {
             private SampleClass? _sample;
         }

         """);
   }

   /// <summary>
   /// The blank line that separates the two using groups leads the kept using, which then becomes the first line.
   /// </summary>
   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_RemovesFirstUsingGroup_LeavesNoBlankFirstLine(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 1, """
         using System;

         using CoreTestProject.Types;

         namespace CoreTestProject.LineEndings;

         public class OrganizeGroups
         {
             private SampleClass? _sample;
         }

         """, """
         using CoreTestProject.Types;

         namespace CoreTestProject.LineEndings;

         public class OrganizeGroups
         {
             private SampleClass? _sample;
         }

         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_SortsFirstUsingDown_KeepsHeaderFirst(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 0, """
         // File header

         using CoreTestProject.Types;
         using System.Text;

         namespace CoreTestProject.LineEndings;

         public class OrganizeSort
         {
             private SampleClass? _sample;
             private StringBuilder? _builder;
         }

         """, """
         // File header

         using System.Text;
         using CoreTestProject.Types;

         namespace CoreTestProject.LineEndings;

         public class OrganizeSort
         {
             private SampleClass? _sample;
             private StringBuilder? _builder;
         }

         """);
   }

   /// <summary>
   /// Directive tests define their own symbol, so the active branch does not depend on the build configuration.
   /// </summary>
   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_AddUsing_IfWrapsFirstUsing_AddsUsingAboveIf(string newLine)
   {
      await AssertAddUsingAsync(newLine, "System.Text", """
         #define LINE_ENDINGS
         #if LINE_ENDINGS
         using System.Diagnostics;
         #endif

         namespace CoreTestProject.LineEndings;

         public class AddIfFirst
         {
         }


         """, """
         #define LINE_ENDINGS
         using System.Text;
         #if LINE_ENDINGS
         using System.Diagnostics;
         #endif

         namespace CoreTestProject.LineEndings;

         public class AddIfFirst
         {
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_AddUsing_IfWrapsLaterUsing_SortsOutsideIf(string newLine)
   {
      await AssertAddUsingAsync(newLine, "System.Collections", """
         #define LINE_ENDINGS
         using System.Text;
         #if LINE_ENDINGS
         using System.Diagnostics;
         #endif

         namespace CoreTestProject.LineEndings;

         public class AddIfLater
         {
         }


         """, """
         #define LINE_ENDINGS
         using System.Collections;
         using System.Text;
         #if LINE_ENDINGS
         using System.Diagnostics;
         #endif

         namespace CoreTestProject.LineEndings;

         public class AddIfLater
         {
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_AddUsing_UsingAfterIfBlock_AddsUsingAfterBlock(string newLine)
   {
      await AssertAddUsingAsync(newLine, "System.Collections", """
         #define LINE_ENDINGS
         #if LINE_ENDINGS
         using System.Diagnostics;
         #endif
         using System.Text;

         namespace CoreTestProject.LineEndings;

         public class AddAfterIf
         {
         }


         """, """
         #define LINE_ENDINGS
         #if LINE_ENDINGS
         using System.Diagnostics;
         #endif
         using System.Collections;
         using System.Text;

         namespace CoreTestProject.LineEndings;

         public class AddAfterIf
         {
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_AddUsing_IfElseBlock_AddsUsingAboveIf(string newLine)
   {
      await AssertAddUsingAsync(newLine, "System.Collections", """
         #define LINE_ENDINGS
         #if LINE_ENDINGS
         using System.Diagnostics;
         #else
         using System.Text;
         #endif

         namespace CoreTestProject.LineEndings;

         public class AddIfElse
         {
         }


         """, """
         #define LINE_ENDINGS
         using System.Collections;
         #if LINE_ENDINGS
         using System.Diagnostics;
         #else
         using System.Text;
         #endif

         namespace CoreTestProject.LineEndings;

         public class AddIfElse
         {
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_AddUsing_NestedIf_AddsUsingAboveOuterIf(string newLine)
   {
      await AssertAddUsingAsync(newLine, "System.Text", """
         #define LINE_ENDINGS
         #if LINE_ENDINGS
         #if LINE_ENDINGS
         using System.Diagnostics;
         #endif
         #endif

         namespace CoreTestProject.LineEndings;

         public class AddNestedIf
         {
         }


         """, """
         #define LINE_ENDINGS
         using System.Text;
         #if LINE_ENDINGS
         #if LINE_ENDINGS
         using System.Diagnostics;
         #endif
         #endif

         namespace CoreTestProject.LineEndings;

         public class AddNestedIf
         {
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_AddUsing_UsingsInsideRegion_AddsUsingInsideRegion(string newLine)
   {
      await AssertAddUsingAsync(newLine, "System.Collections", """
         #region Usings
         using System.Text;
         #endregion

         namespace CoreTestProject.LineEndings;

         public class AddInRegion
         {
         }


         """, """
         #region Usings
         using System.Collections;
         using System.Text;
         #endregion

         namespace CoreTestProject.LineEndings;

         public class AddInRegion
         {
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_AddUsing_DefineInsideIfAboveUsings_ThrowsNotSupported(string newLine)
   {
      var source = """
         #if !LINE_ENDINGS_OFF
         #define LINE_ENDINGS
         using System.Diagnostics;
         #endif

         namespace CoreTestProject.LineEndings;

         public class AddDefineInIf
         {
         }

         """.ReplaceLineEndings(newLine);
      var path = await WriteSourceAsync(source);

      await AssertThrowsToolExceptionAsync(
         () => new AddUsingTool(WorkspaceManager).AddUsingAsync(path, "System.Text", preview: false, CT),
         ErrorCodes.NotSupported);

      Assert.Equal(source, await File.ReadAllTextAsync(path, CT));
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_AddUsing_NoUsingsDefineInsideIf_ThrowsNotSupported(string newLine)
   {
      var source = """
         #if !LINE_ENDINGS_OFF
         #define LINE_ENDINGS
         namespace CoreTestProject.LineEndings;
         #endif

         public class AddDefineInIfNoUsings
         {
         }

         """.ReplaceLineEndings(newLine);
      var path = await WriteSourceAsync(source);

      await AssertThrowsToolExceptionAsync(
         () => new AddUsingTool(WorkspaceManager).AddUsingAsync(path, "System.Text", preview: false, CT),
         ErrorCodes.NotSupported);

      Assert.Equal(source, await File.ReadAllTextAsync(path, CT));
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_RemovesEveryUsing_IfWrapsFirstUsing_KeepsIfBalanced(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 2, """
         #define LINE_ENDINGS
         #if LINE_ENDINGS
         using System.Linq;
         #endif
         using System;

         namespace CoreTestProject.LineEndings;

         public class OrganizeIfFirst
         {
         }


         """, """
         #define LINE_ENDINGS
         #if LINE_ENDINGS
         #endif
         namespace CoreTestProject.LineEndings;

         public class OrganizeIfFirst
         {
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_RemovesUsingAfterIfBlock_KeepsEndif(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 1, """
         #define LINE_ENDINGS
         #if LINE_ENDINGS
         using CoreTestProject.Types;
         #endif
         using System;

         namespace CoreTestProject.LineEndings;

         public class OrganizeAfterIf
         {
             private SampleClass? _sample;
         }


         """, """
         #define LINE_ENDINGS
         #if LINE_ENDINGS
         using CoreTestProject.Types;
         #endif

         namespace CoreTestProject.LineEndings;

         public class OrganizeAfterIf
         {
             private SampleClass? _sample;
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_RemovesSomeUsings_IfWrapsFirstUsing_SortsWithinBlocks(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 2, """
         #define LINE_ENDINGS
         #if LINE_ENDINGS
         using System.Linq;
         using CoreTestProject.Types;
         #endif
         using System;
         using System.Text;

         namespace CoreTestProject.LineEndings;

         public class OrganizeIfSome
         {
             private SampleClass? _sample;
             private StringBuilder? _builder;
         }


         """, """
         #define LINE_ENDINGS
         #if LINE_ENDINGS
         using CoreTestProject.Types;
         #endif
         using System.Text;

         namespace CoreTestProject.LineEndings;

         public class OrganizeIfSome
         {
             private SampleClass? _sample;
             private StringBuilder? _builder;
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_IfElseBlock_KeepsDirectives(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 1, """
         #define LINE_ENDINGS
         using CoreTestProject.Types;
         #if LINE_ENDINGS
         using System.Text;
         #else
         using System.Xml;
         #endif
         using System;

         namespace CoreTestProject.LineEndings;

         public class OrganizeIfElse
         {
             private SampleClass? _sample;
             private StringBuilder? _builder;
         }


         """, """
         #define LINE_ENDINGS
         using CoreTestProject.Types;
         #if LINE_ENDINGS
         using System.Text;
         #else
         using System.Xml;
         #endif

         namespace CoreTestProject.LineEndings;

         public class OrganizeIfElse
         {
             private SampleClass? _sample;
             private StringBuilder? _builder;
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_RemovesEveryUsing_NestedIf_KeepsDirectives(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 2, """
         #define LINE_ENDINGS
         #if LINE_ENDINGS
         using System.Linq;
         #if LINE_ENDINGS
         using System;
         #endif
         #endif

         namespace CoreTestProject.LineEndings;

         public class OrganizeNestedIf
         {
         }


         """, """
         #define LINE_ENDINGS
         #if LINE_ENDINGS
         #if LINE_ENDINGS
         #endif
         #endif

         namespace CoreTestProject.LineEndings;

         public class OrganizeNestedIf
         {
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_UsingInsideRegion_StaysInsideRegion(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 1, """
         using CoreTestProject.Types;
         using System;
         #region Text
         using System.Text;
         #endregion

         namespace CoreTestProject.LineEndings;

         public class OrganizeRegion
         {
             private SampleClass? _sample;
             private StringBuilder? _builder;
         }


         """, """
         using CoreTestProject.Types;
         #region Text
         using System.Text;
         #endregion

         namespace CoreTestProject.LineEndings;

         public class OrganizeRegion
         {
             private SampleClass? _sample;
             private StringBuilder? _builder;
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_RemovesUsingsAboveDirective_LeavesNoBlankFirstLine(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 2, """
         using System;

         #pragma warning disable CS0168
         using System.Linq;

         using CoreTestProject.Types;

         namespace CoreTestProject.LineEndings;

         public class OrganizeAbovePragma
         {
             private SampleClass? _sample;
         }


         """, """
         #pragma warning disable CS0168
         using CoreTestProject.Types;

         namespace CoreTestProject.LineEndings;

         public class OrganizeAbovePragma
         {
             private SampleClass? _sample;
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_RemovesEveryUsing_BlankLineAboveDirective_LeavesNoBlankFirstLine(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 2, """
         using System;

         #pragma warning disable CS0168
         using System.Linq;

         namespace CoreTestProject.LineEndings;

         public class OrganizeAllAbovePragma
         {
         }


         """, """
         #pragma warning disable CS0168
         namespace CoreTestProject.LineEndings;

         public class OrganizeAllAbovePragma
         {
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_RemovesUsingBetweenHeaderAndDirective_LeavesOneBlankLine(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 1, """
         // File header

         using System;

         #pragma warning disable CS0168
         using CoreTestProject.Types;

         namespace CoreTestProject.LineEndings;

         public class OrganizeHeaderPragma
         {
             private SampleClass? _sample;
         }


         """, """
         // File header

         #pragma warning disable CS0168
         using CoreTestProject.Types;

         namespace CoreTestProject.LineEndings;

         public class OrganizeHeaderPragma
         {
             private SampleClass? _sample;
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_RemovesUsingAfterDirectiveAndBlankLine_LeavesOneBlankLine(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 1, """
         using CoreTestProject.Types;
         #pragma warning disable CS0168

         using System;

         namespace CoreTestProject.LineEndings;

         public class OrganizePragmaBlank
         {
             private SampleClass? _sample;
         }


         """, """
         using CoreTestProject.Types;
         #pragma warning disable CS0168

         namespace CoreTestProject.LineEndings;

         public class OrganizePragmaBlank
         {
             private SampleClass? _sample;
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_AddUsing_HeaderDirectlyAboveIfWrappingUsings_KeepsHeaderFirst(string newLine)
   {
      await AssertAddUsingAsync(newLine, "System.Text", """
         // Copyright (c) Example
         #if !LINE_ENDINGS_OFF
         using System.Diagnostics;
         #endif

         namespace CoreTestProject.LineEndings;

         public class AddHeaderAboveIf
         {
         }


         """, """
         // Copyright (c) Example
         using System.Text;
         #if !LINE_ENDINGS_OFF
         using System.Diagnostics;
         #endif

         namespace CoreTestProject.LineEndings;

         public class AddHeaderAboveIf
         {
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_AddUsing_NoUsingsHeaderDirectlyAboveIf_KeepsHeaderFirst(string newLine)
   {
      await AssertAddUsingAsync(newLine, "System.Text", """
         // Copyright (c) Example
         #if !LINE_ENDINGS_OFF
         namespace CoreTestProject.LineEndings;
         #endif

         public class AddNoUsingsHeaderAboveIf
         {
         }


         """, """
         // Copyright (c) Example
         using System.Text;

         #if !LINE_ENDINGS_OFF
         namespace CoreTestProject.LineEndings;
         #endif

         public class AddNoUsingsHeaderAboveIf
         {
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_AddUsing_NoUsingsDefineInsideRegion_AddsUsingBelowDefine(string newLine)
   {
      await AssertAddUsingAsync(newLine, "System.Text", """
         #region Everything
         #define LINE_ENDINGS
         namespace CoreTestProject.LineEndings;

         public class AddDefineInRegion
         {
         }
         #endregion

         """, """
         #region Everything
         #define LINE_ENDINGS
         using System.Text;

         namespace CoreTestProject.LineEndings;

         public class AddDefineInRegion
         {
         }
         #endregion

         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_AddUsing_DefineInsideRegionAboveIf_AddsUsingBelowDefine(string newLine)
   {
      await AssertAddUsingAsync(newLine, "System.Text", """
         #region Everything
         #define LINE_ENDINGS
         #if LINE_ENDINGS
         using System.Diagnostics;
         #endif

         namespace CoreTestProject.LineEndings;

         public class AddDefineInRegionIf
         {
         }
         #endregion

         """, """
         #region Everything
         #define LINE_ENDINGS
         using System.Text;
         #if LINE_ENDINGS
         using System.Diagnostics;
         #endif

         namespace CoreTestProject.LineEndings;

         public class AddDefineInRegionIf
         {
         }
         #endregion

         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_RemovesFirstUsingWithSameLineComment_KeepsPragmaOnItsLine(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 1, """
         /* c */ using System;
         #pragma warning disable CS0168
         using CoreTestProject.Types;

         namespace CoreTestProject.LineEndings;

         public class OrganizeCommentPragma
         {
             private SampleClass? _sample;
         }


         """, """
         #pragma warning disable CS0168
         using CoreTestProject.Types;

         namespace CoreTestProject.LineEndings;

         public class OrganizeCommentPragma
         {
             private SampleClass? _sample;
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_RemovesFirstUsingWithSameLineComment_KeepsIfOnItsLine(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 1, """
         /* c */ using System;
         #if !LINE_ENDINGS_OFF
         using CoreTestProject.Types;
         #endif

         namespace CoreTestProject.LineEndings;

         public class OrganizeCommentIf
         {
             private SampleClass? _sample;
         }


         """, """
         #if !LINE_ENDINGS_OFF
         using CoreTestProject.Types;
         #endif

         namespace CoreTestProject.LineEndings;

         public class OrganizeCommentIf
         {
             private SampleClass? _sample;
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_RemovesFirstUsingWithSameLineCommentBelowHeader_KeepsHeader(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 1, """
         // File header

         /* c */ using System;
         #pragma warning disable CS0168
         using CoreTestProject.Types;

         namespace CoreTestProject.LineEndings;

         public class OrganizeHeaderCommentPragma
         {
             private SampleClass? _sample;
         }


         """, """
         // File header

         #pragma warning disable CS0168
         using CoreTestProject.Types;

         namespace CoreTestProject.LineEndings;

         public class OrganizeHeaderCommentPragma
         {
             private SampleClass? _sample;
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_RemovesFirstUsingWithCommentLineAbove_KeepsCommentAsHeader(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 1, """
         // About System
         using System;
         #pragma warning disable CS0168
         using CoreTestProject.Types;

         namespace CoreTestProject.LineEndings;

         public class OrganizeCommentLinePragma
         {
             private SampleClass? _sample;
         }


         """, """
         // About System
         #pragma warning disable CS0168
         using CoreTestProject.Types;

         namespace CoreTestProject.LineEndings;

         public class OrganizeCommentLinePragma
         {
             private SampleClass? _sample;
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_OrganizeUsings_SortsFirstUsingWithSameLineComment_MovesCommentWithIt(string newLine)
   {
      await AssertOrganizeUsingsAsync(newLine, 0, """
         /* c */ using CoreTestProject.Types;
         using System.Text;

         namespace CoreTestProject.LineEndings;

         public class OrganizeCommentSort
         {
             private SampleClass? _sample;
             private StringBuilder? _builder;
         }


         """, """
         using System.Text;
         /* c */ using CoreTestProject.Types;

         namespace CoreTestProject.LineEndings;

         public class OrganizeCommentSort
         {
             private SampleClass? _sample;
             private StringBuilder? _builder;
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_AddUsing_SameLineCommentOnFirstUsing_MovesCommentWithIt(string newLine)
   {
      await AssertAddUsingAsync(newLine, "System.Text", """
         /* c */ using CoreTestProject.Types;

         namespace CoreTestProject.LineEndings;

         public class AddCommentFirst
         {
         }


         """, """
         using System.Text;
         /* c */ using CoreTestProject.Types;

         namespace CoreTestProject.LineEndings;

         public class AddCommentFirst
         {
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_AddUsing_CommentAboveIfBelowHeader_AddsUsingAboveComment(string newLine)
   {
      await AssertAddUsingAsync(newLine, "System.Text", """
         // Copyright

         // Diagnostics only when enabled
         #if !LINE_ENDINGS_OFF
         using System.Diagnostics;
         #endif

         namespace CoreTestProject.LineEndings;

         public class AddCommentAboveIf
         {
         }


         """, """
         // Copyright

         using System.Text;
         // Diagnostics only when enabled
         #if !LINE_ENDINGS_OFF
         using System.Diagnostics;
         #endif

         namespace CoreTestProject.LineEndings;

         public class AddCommentAboveIf
         {
         }


         """);
   }

   [Theory]
   [MemberData(nameof(NewLines))]
   public async Task UsingTools_LineEndings_AddUsing_NoUsingsCommentAboveIfBelowHeader_AddsUsingAboveComment(string newLine)
   {
      await AssertAddUsingAsync(newLine, "System.Text", """
         // Copyright

         // Only when enabled
         #if !LINE_ENDINGS_OFF
         namespace CoreTestProject.LineEndings;
         #endif

         public class AddNoUsingsCommentAboveIf
         {
         }


         """, """
         // Copyright

         using System.Text;

         // Only when enabled
         #if !LINE_ENDINGS_OFF
         namespace CoreTestProject.LineEndings;
         #endif

         public class AddNoUsingsCommentAboveIf
         {
         }


         """);
   }

   public static TheoryData<string, string> NewLinesAndIndents => new()
   {
      { "\n", "  " },
      { "\r\n", "  " },
      { "\n", "\t" },
      { "\r\n", "\t" },
   };

   [Theory]
   [MemberData(nameof(NewLinesAndIndents))]
   public async Task UsingTools_LineEndings_OrganizeUsings_RemovesIndentedFirstUsing_LeavesNoBlankFirstLine(
      string newLine,
      string indent)
   {
      await AssertOrganizeUsingsAsync(newLine, 1, $$"""
         {{indent}}using System;

         #pragma warning disable CS0168
         using CoreTestProject.Types;

         namespace CoreTestProject.LineEndings;

         public class OrganizeIndented
         {
             private SampleClass? _sample;
         }

         """, """
         #pragma warning disable CS0168
         using CoreTestProject.Types;

         namespace CoreTestProject.LineEndings;

         public class OrganizeIndented
         {
             private SampleClass? _sample;
         }

         """);
   }

   private async Task AssertAddUsingAsync(string newLine, string @namespace, string source, string expected)
   {
      var path = await WriteSourceAsync(source.ReplaceLineEndings(newLine));

      var result = await new AddUsingTool(WorkspaceManager).AddUsingAsync(path, @namespace, preview: false, CT);

      Assert.True(result.Data.Added);
      Assert.True(result.Data.Applied);
      var written = await File.ReadAllTextAsync(path, CT);
      Assert.Equal(expected.ReplaceLineEndings(newLine), written);
      Assert.Empty(ParseDiagnostics(written));
   }

   private async Task AssertOrganizeUsingsAsync(string newLine, int expectedRemoved, string source, string expected)
   {
      var path = await WriteSourceAsync(source.ReplaceLineEndings(newLine));

      var result = await new OrganizeUsingsTool(WorkspaceManager).OrganizeUsingsAsync(
         new FileScope { Path = path }, preview: false, cancellationToken: CT);

      Assert.True(result.Data.Applied);
      Assert.Equal(1, result.Data.FilesChanged);
      Assert.Equal(expectedRemoved, result.Data.TotalUsingsRemoved);
      var written = await File.ReadAllTextAsync(path, CT);
      Assert.Equal(expected.ReplaceLineEndings(newLine), written);
      Assert.Empty(ParseDiagnostics(written));
   }

   private static List<string> ParseDiagnostics(string text) =>
      [.. CSharpSyntaxTree.ParseText(text, cancellationToken: TestContext.Current.CancellationToken)
         .GetDiagnostics()
         .Select(d => d.ToString())];

   /// <summary>
   /// Writes the source and checks that it parses without diagnostics, so a diagnostic in the result comes from the tool.
   /// </summary>
   private async Task<string> WriteSourceAsync(string text)
   {
      Assert.Empty(ParseDiagnostics(text));
      var path = GetCoreTestProjectPath("LineEndings/UsingTarget.cs");
      var written = await new WriteFileTool(WorkspaceManager).WriteFileAsync(path, text, CT);
      Assert.True(written.Data.IsNewFile);
      return path;
   }
}
