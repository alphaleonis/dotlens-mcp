using DotLens.Infrastructure;
using DotLens.Tools.Editing;
using DotLens.Tools.Formatting;
using DotLens.Tools.Refactoring;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DotLens.Tests.Refactoring;

/// <summary>
/// Covers which usings add_using counts as in scope, where add_using and organize_usings find and place usings inside
/// namespace declarations and #if blocks, and that organize_usings leaves the code outside the usings unchanged.
/// </summary>
/// <remarks>
/// CoreTestProject enables implicit usings, so System.Linq is a global using of every file in it.
/// </remarks>
public class UsingToolsScopeTests : IsolatedProjectTestBase
{
   [Fact]
   public async Task UsingTools_Scope_AddUsing_NamespaceOnlyInAliasUsing_AddsUsing()
   {
      await AssertAddUsingAsync("System.Text", """
         using Text = System.Text;

         namespace CoreTestProject.Scope;

         public class AliasOnly
         {
         }

         """, """
         using Text = System.Text;
         using System.Text;

         namespace CoreTestProject.Scope;

         public class AliasOnly
         {
         }

         """);
   }

   /// <summary>
   /// A static using of a namespace does not compile, but a file in progress can hold one.
   /// </summary>
   [Fact]
   public async Task UsingTools_Scope_AddUsing_NamespaceOnlyInStaticUsing_AddsUsing()
   {
      await AssertAddUsingAsync("System.Text", """
         using static System.Text;

         namespace CoreTestProject.Scope;

         public class StaticOnly
         {
         }

         """, """
         using static System.Text;
         using System.Text;

         namespace CoreTestProject.Scope;

         public class StaticOnly
         {
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_AddUsing_TypeName_ThrowsInvalidParameter()
   {
      var source = """
         using static System.Math;

         namespace CoreTestProject.Scope;

         public class TypeName
         {
             public double Value => Abs(-1);
         }

         """;
      var path = await WriteSourceAsync("Scope/UsingTarget.cs", source);

      await AssertThrowsToolExceptionAsync(
         () => new AddUsingTool(WorkspaceManager).AddUsingAsync(path, "System.Math", preview: false, CT),
         ErrorCodes.InvalidParameter);

      Assert.Equal(source, await File.ReadAllTextAsync(path, CT));
   }

   [Fact]
   public async Task UsingTools_Scope_AddUsing_NamespaceInGlobalUsingOfOtherFile_ReportsPresent()
   {
      await WriteSourceAsync("Scope/ScopeGlobalUsings.cs", """
         global using System.Text;

         """);

      await AssertAddUsingReportsPresentAsync("System.Text", """
         namespace CoreTestProject.Scope;

         public class GlobalElsewhere
         {
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_AddUsing_NamespaceInImplicitGlobalUsing_ReportsPresent()
   {
      await AssertAddUsingReportsPresentAsync("System.Linq", """
         using System.Text;

         namespace CoreTestProject.Scope;

         public class ImplicitGlobal
         {
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_AddUsing_NamespaceInFileScopedNamespaceUsings_ReportsPresent()
   {
      await AssertAddUsingReportsPresentAsync("System.Text", """
         namespace CoreTestProject.Scope;

         using System.Text;

         public class InFileScoped
         {
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_AddUsing_NamespaceInBlockNamespaceUsings_ReportsPresent()
   {
      await AssertAddUsingReportsPresentAsync("System.Text", """
         namespace CoreTestProject.Scope
         {
             using System.Text;

             public class InBlock
             {
             }
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_AddUsing_UsingsInsideFileScopedNamespace_AddsUsingThere()
   {
      await AssertAddUsingAsync("System.Collections", """
         namespace CoreTestProject.Scope;

         using System.Text;

         public class AddInFileScoped
         {
         }

         """, """
         namespace CoreTestProject.Scope;

         using System.Collections;
         using System.Text;

         public class AddInFileScoped
         {
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_AddUsing_UsingsInsideBlockNamespace_AddsIndentedUsingThere()
   {
      await AssertAddUsingAsync("System.Collections", """
         namespace CoreTestProject.Scope
         {
             using System.Text;

             public class AddInBlock
             {
             }
         }

         """, """
         namespace CoreTestProject.Scope
         {
             using System.Collections;
             using System.Text;

             public class AddInBlock
             {
             }
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_AddUsing_UsingsInsideNestedNamespace_AddsUsingThere()
   {
      await AssertAddUsingAsync("System.Text", """
         namespace CoreTestProject
         {
             namespace Scope
             {
                 using System.Diagnostics;

                 public class AddInNested
                 {
                 }
             }
         }

         """, """
         namespace CoreTestProject
         {
             namespace Scope
             {
                 using System.Diagnostics;
                 using System.Text;

                 public class AddInNested
                 {
                 }
             }
         }

         """);
   }

   /// <summary>
   /// The using of the first namespace does not reach the second, so the new using goes at file level.
   /// </summary>
   [Fact]
   public async Task UsingTools_Scope_AddUsing_NamespaceInOneOfTwoNamespaces_AddsUsingAtFileLevel()
   {
      await AssertAddUsingAsync("System.Text", """
         namespace CoreTestProject.ScopeFirst
         {
             using System.Text;

             public class First
             {
             }
         }

         namespace CoreTestProject.ScopeSecond
         {
             public class Second
             {
             }
         }

         """, """
         using System.Text;

         namespace CoreTestProject.ScopeFirst
         {
             using System.Text;

             public class First
             {
             }
         }

         namespace CoreTestProject.ScopeSecond
         {
             public class Second
             {
             }
         }

         """);
   }

   /// <summary>
   /// The compiler requires every global using to precede the other usings of the file.
   /// </summary>
   [Fact]
   public async Task UsingTools_Scope_AddUsing_GlobalUsingInFile_AddsUsingBelowGlobalUsings()
   {
      await AssertAddUsingAsync("System.Collections", """
         global using System.Text;
         using System.Diagnostics;

         namespace CoreTestProject.Scope;

         public class AddBelowGlobal
         {
         }

         """, """
         global using System.Text;
         using System.Collections;
         using System.Diagnostics;

         namespace CoreTestProject.Scope;

         public class AddBelowGlobal
         {
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_AddUsing_IfWrapsWholeFile_AddsUsingInsideIf()
   {
      await AssertAddUsingAsync("System.Text", """
         #if !SCOPE_OFF
         using System.Diagnostics;

         namespace CoreTestProject.Scope;

         public class AddInWholeFileIf
         {
         }
         #endif

         """, """
         #if !SCOPE_OFF
         using System.Diagnostics;
         using System.Text;

         namespace CoreTestProject.Scope;

         public class AddInWholeFileIf
         {
         }
         #endif

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_AddUsing_IfWithDefineWrapsWholeFile_AddsUsingBelowDefine()
   {
      await AssertAddUsingAsync("System.Collections", """
         #if !SCOPE_OFF
         #define SCOPE
         using System.Diagnostics;

         namespace CoreTestProject.Scope;

         public class AddBelowDefineInWholeFileIf
         {
         }
         #endif

         """, """
         #if !SCOPE_OFF
         #define SCOPE
         using System.Collections;
         using System.Diagnostics;

         namespace CoreTestProject.Scope;

         public class AddBelowDefineInWholeFileIf
         {
         }
         #endif

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_AddUsing_NoUsingsIfWithDefineWrapsWholeFile_AddsUsingBelowDefine()
   {
      await AssertAddUsingAsync("System.Text", """
         #if !SCOPE_OFF
         #define SCOPE
         namespace CoreTestProject.Scope;

         public class AddNoUsingsInWholeFileIf
         {
         }
         #endif

         """, """
         #if !SCOPE_OFF
         #define SCOPE
         using System.Text;

         namespace CoreTestProject.Scope;

         public class AddNoUsingsInWholeFileIf
         {
         }
         #endif

         """);
   }

   /// <summary>
   /// The #if around the usings closes before the code, so a build without the symbol still needs the new using.
   /// </summary>
   [Fact]
   public async Task UsingTools_Scope_AddUsing_IfClosesBeforeCode_AddsUsingAboveIf()
   {
      await AssertAddUsingAsync("System.Text", """
         #if !SCOPE_OFF
         using System.Diagnostics;
         #endif

         namespace CoreTestProject.Scope;

         public class AddAboveClosedIf
         {
         }

         """, """
         using System.Text;
         #if !SCOPE_OFF
         using System.Diagnostics;
         #endif

         namespace CoreTestProject.Scope;

         public class AddAboveClosedIf
         {
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_OrganizeUsings_FileScopedNamespaceUsings_SortsAndRemovesUnused()
   {
      await AssertOrganizeUsingsAsync(1, """
         namespace CoreTestProject.Scope;

         using System.Text;
         using System.Collections.Concurrent;
         using System.Diagnostics;

         public class OrganizeFileScoped
         {
             private StringBuilder? _builder;
             private Stopwatch? _watch;
         }

         """, """
         namespace CoreTestProject.Scope;

         using System.Diagnostics;
         using System.Text;

         public class OrganizeFileScoped
         {
             private StringBuilder? _builder;
             private Stopwatch? _watch;
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_OrganizeUsings_FileAndBlockNamespaceUsings_OrganizesEachList()
   {
      await AssertOrganizeUsingsAsync(2, """
         using System.Text;
         using System.Collections.Concurrent;

         namespace CoreTestProject.ScopeFirst
         {
             using System.Diagnostics;
             using System.Collections;
             using System.Collections.Specialized;

             public class First
             {
                 private StringBuilder? _builder;
                 private Stopwatch? _watch;
                 private ArrayList? _list;
             }
         }

         namespace CoreTestProject.ScopeSecond
         {
             using System.Text.RegularExpressions;
             using System.ComponentModel;

             public class Second
             {
                 private Regex? _regex;
                 private Component? _component;
             }
         }

         """, """
         using System.Text;

         namespace CoreTestProject.ScopeFirst
         {
             using System.Collections;
             using System.Diagnostics;

             public class First
             {
                 private StringBuilder? _builder;
                 private Stopwatch? _watch;
                 private ArrayList? _list;
             }
         }

         namespace CoreTestProject.ScopeSecond
         {
             using System.ComponentModel;
             using System.Text.RegularExpressions;

             public class Second
             {
                 private Regex? _regex;
                 private Component? _component;
             }
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_OrganizeUsings_RemovesEveryNamespaceUsing_KeepsNamespaceBody()
   {
      await AssertOrganizeUsingsAsync(1, """
         namespace CoreTestProject.Scope
         {
             using System.Collections.Concurrent;

             public class OrganizeEmptied
             {
             }
         }

         """, """
         namespace CoreTestProject.Scope
         {
             public class OrganizeEmptied
             {
             }
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_OrganizeUsings_UnformattedCode_LeavesCodeUnchanged()
   {
      await AssertOrganizeUsingsAsync(0, """
         using System.Text;
         using System.Diagnostics;

         namespace CoreTestProject.Scope;

         public  class   OrganizeUnformatted
         {
             private Func<double, double> _abs = x  =>  Math.Abs( x );
           private StringBuilder? _builder;
                 private Stopwatch?   _watch;
         }

         """, """
         using System.Diagnostics;
         using System.Text;

         namespace CoreTestProject.Scope;

         public  class   OrganizeUnformatted
         {
             private Func<double, double> _abs = x  =>  Math.Abs( x );
           private StringBuilder? _builder;
                 private Stopwatch?   _watch;
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_OrganizeUsings_TabIndentedBlockNamespace_KeepsTabs()
   {
      await AssertOrganizeUsingsAsync(0, "namespace CoreTestProject.Scope\n{\n\tusing System.Text;\n\tusing System.Diagnostics;\n\n\tpublic class OrganizeTabs\n\t{\n\t\tprivate StringBuilder? _builder;\n\t\tprivate Stopwatch? _watch;\n\t}\n}\n",
         "namespace CoreTestProject.Scope\n{\n\tusing System.Diagnostics;\n\tusing System.Text;\n\n\tpublic class OrganizeTabs\n\t{\n\t\tprivate StringBuilder? _builder;\n\t\tprivate Stopwatch? _watch;\n\t}\n}\n");
   }

   [Fact]
   public async Task UsingTools_Scope_AddUsing_IfElseWrapsWholeFile_AddsUsingAboveIf()
   {
      await AssertAddUsingAsync("System.Text", """
         #if !SCOPE_OFF
         using System.Diagnostics;

         namespace CoreTestProject.Scope;

         public class AddIfElse
         {
         }
         #else
         namespace CoreTestProject.Scope;

         public class AddIfElse
         {
         }
         #endif

         """, """
         using System.Text;
         #if !SCOPE_OFF
         using System.Diagnostics;

         namespace CoreTestProject.Scope;

         public class AddIfElse
         {
         }
         #else
         namespace CoreTestProject.Scope;

         public class AddIfElse
         {
         }
         #endif

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_AddUsing_IfElifWrapsWholeFile_AddsUsingAboveIf()
   {
      await AssertAddUsingAsync("System.Text", """
         #if !SCOPE_OFF
         using System.Diagnostics;

         namespace CoreTestProject.Scope;

         public class AddIfElif
         {
         }
         #elif SCOPE_OTHER
         namespace CoreTestProject.Scope;

         public class AddIfElif
         {
         }
         #endif

         """, """
         using System.Text;
         #if !SCOPE_OFF
         using System.Diagnostics;

         namespace CoreTestProject.Scope;

         public class AddIfElif
         {
         }
         #elif SCOPE_OTHER
         namespace CoreTestProject.Scope;

         public class AddIfElif
         {
         }
         #endif

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_AddUsing_NoUsingsIfElseWrapsWholeFile_AddsUsingAboveIf()
   {
      await AssertAddUsingAsync("System.Text", """
         #if !SCOPE_OFF
         namespace CoreTestProject.Scope;

         public class AddNoUsingsIfElse
         {
         }
         #else
         namespace CoreTestProject.Scope;

         public class AddNoUsingsIfElse
         {
         }
         #endif

         """, """
         using System.Text;

         #if !SCOPE_OFF
         namespace CoreTestProject.Scope;

         public class AddNoUsingsIfElse
         {
         }
         #else
         namespace CoreTestProject.Scope;

         public class AddNoUsingsIfElse
         {
         }
         #endif

         """);
   }

   /// <summary>
   /// The new using can stay below neither #define, since each holds for one branch only.
   /// </summary>
   [Fact]
   public async Task UsingTools_Scope_AddUsing_NoUsingsIfElseWithDefinesWrapsWholeFile_ThrowsNotSupported()
   {
      await AssertAddUsingThrowsAsync("System.Text", ErrorCodes.NotSupported, """
         #if !SCOPE_OFF
         #define SCOPE
         namespace CoreTestProject.Scope;

         public class AddNoUsingsIfElseDefines
         {
         }
         #else
         #define SCOPE_OTHER
         namespace CoreTestProject.Scope;

         public class AddNoUsingsIfElseDefines
         {
         }
         #endif

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_AddUsing_IfElseWithDefineWrapsWholeFile_ThrowsNotSupported()
   {
      await AssertAddUsingThrowsAsync("System.Text", ErrorCodes.NotSupported, """
         #if !SCOPE_OFF
         #define SCOPE
         using System.Diagnostics;

         namespace CoreTestProject.Scope;

         public class AddIfElseDefine
         {
         }
         #else
         namespace CoreTestProject.Scope;

         public class AddIfElseDefine
         {
         }
         #endif

         """);
   }

   /// <summary>
   /// Inside namespace CoreTestProject.Scope, a plain "using Contoso.Tools;" would import CoreTestProject.Scope.Contoso.Tools.
   /// </summary>
   [Fact]
   public async Task UsingTools_Scope_AddUsing_FirstSegmentCollidesInBlockNamespace_AddsGlobalQualifiedUsing()
   {
      await WriteSourceAsync("Scope/ContosoTools.cs", ContosoToolsSource);
      await WriteSourceAsync("Scope/ScopeContoso.cs", """
         namespace CoreTestProject.Scope.Contoso.Tools
         {
             public class Other
             {
             }
         }

         """);

      await AssertAddUsingTwiceAsync("Contoso.Tools", """
         namespace CoreTestProject.Scope
         {
             using System.Text;

             public class CollideBlock
             {
             }
         }

         """, """
         namespace CoreTestProject.Scope
         {
             using System.Text;
             using global::Contoso.Tools;

             public class CollideBlock
             {
             }
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_AddUsing_FirstSegmentCollidesInFileScopedNamespace_AddsGlobalQualifiedUsing()
   {
      await WriteSourceAsync("Scope/ContosoTools.cs", ContosoToolsSource);
      await WriteSourceAsync("Scope/ScopeContoso.cs", """
         namespace CoreTestProject.Scope.Contoso
         {
             public class Other
             {
             }
         }

         """);

      await AssertAddUsingTwiceAsync("Contoso.Tools", """
         namespace CoreTestProject.Scope;

         using System.Text;

         public class CollideFileScoped
         {
         }

         """, """
         namespace CoreTestProject.Scope;

         using System.Text;
         using global::Contoso.Tools;

         public class CollideFileScoped
         {
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_AddUsing_FirstSegmentCollidesInOuterNamespace_AddsGlobalQualifiedUsing()
   {
      await WriteSourceAsync("Scope/ContosoTools.cs", ContosoToolsSource);
      await WriteSourceAsync("Scope/ScopeContoso.cs", """
         namespace CoreTestProject.Contoso
         {
             public class Other
             {
             }
         }

         """);

      await AssertAddUsingTwiceAsync("Contoso.Tools", """
         namespace CoreTestProject
         {
             namespace Scope
             {
                 using System.Diagnostics;

                 public class CollideOuter
                 {
                 }
             }
         }

         """, """
         namespace CoreTestProject
         {
             namespace Scope
             {
                 using System.Diagnostics;
                 using global::Contoso.Tools;

                 public class CollideOuter
                 {
                 }
             }
         }

         """);
   }

   /// <summary>
   /// CoreTestProject.Scope.Tools shares only the last segment, so "using Contoso.Tools;" still finds Contoso first.
   /// </summary>
   [Fact]
   public async Task UsingTools_Scope_AddUsing_LaterSegmentCollides_AddsPlainUsing()
   {
      await WriteSourceAsync("Scope/ContosoTools.cs", ContosoToolsSource);
      await WriteSourceAsync("Scope/ScopeTools.cs", """
         namespace CoreTestProject.Scope.Tools
         {
             public class Other
             {
             }
         }

         """);

      await AssertAddUsingTwiceAsync("Contoso.Tools", """
         namespace CoreTestProject.Scope
         {
             using System.Text;

             public class CollideLater
             {
             }
         }

         """, """
         namespace CoreTestProject.Scope
         {
             using System.Text;
             using Contoso.Tools;

             public class CollideLater
             {
             }
         }

         """);
   }

   /// <summary>
   /// Fabrikam.Missing stands for a namespace of a package the project does not reference yet.
   /// </summary>
   [Fact]
   public async Task UsingTools_Scope_AddUsing_UnboundNameFirstSegmentCollides_AddsGlobalQualifiedUsing()
   {
      await WriteSourceAsync("Scope/ScopeFabrikam.cs", """
         namespace CoreTestProject.Scope.Fabrikam
         {
             public class Other
             {
             }
         }

         """);

      await AssertAddUsingTwiceAsync("Fabrikam.Missing", """
         namespace CoreTestProject.Scope
         {
             using System.Text;

             public class CollideUnbound
             {
             }
         }

         """, """
         namespace CoreTestProject.Scope
         {
             using System.Text;
             using global::Fabrikam.Missing;

             public class CollideUnbound
             {
             }
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_AddUsing_UnboundNameInNamespace_AddsPlainUsing()
   {
      await AssertAddUsingTwiceAsync("Fabrikam.Missing", """
         namespace CoreTestProject.Scope
         {
             using System.Text;

             public class Unbound
             {
             }
         }

         """, """
         namespace CoreTestProject.Scope
         {
             using System.Text;
             using Fabrikam.Missing;

             public class Unbound
             {
             }
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_AddUsing_UsingsSharingLine_KeepsCodeOffUsingLine()
   {
      await AssertAddUsingAsync("System.Collections", """
         using System.Text; using System.Diagnostics;

         namespace CoreTestProject.Scope;

         public class AddSharedLine
         {
         }

         """, """
         using System.Collections;
         using System.Diagnostics;
         using System.Text;

         namespace CoreTestProject.Scope;

         public class AddSharedLine
         {
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_OrganizeUsings_SortsUsingsSharingLine_KeepsCodeOffUsingLine()
   {
      await AssertOrganizeUsingsAsync(0, """
         using System.Text; using System.Diagnostics;
         namespace CoreTestProject.Scope;

         public class OrganizeSharedLine
         {
             private StringBuilder? _builder;
             private Stopwatch? _watch;
         }

         """, """
         using System.Diagnostics;
         using System.Text;
         namespace CoreTestProject.Scope;

         public class OrganizeSharedLine
         {
             private StringBuilder? _builder;
             private Stopwatch? _watch;
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_OrganizeUsings_UsingsSharingLineInOrder_StayOnOneLine()
   {
      await AssertOrganizeUsingsAsync(1, """
         using System.Diagnostics; using System.Text;
         using System.Collections.Concurrent;

         namespace CoreTestProject.Scope;

         public class OrganizeSharedLineInOrder
         {
             private StringBuilder? _builder;
             private Stopwatch? _watch;
         }

         """, """
         using System.Diagnostics; using System.Text;

         namespace CoreTestProject.Scope;

         public class OrganizeSharedLineInOrder
         {
             private StringBuilder? _builder;
             private Stopwatch? _watch;
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_OrganizeUsings_UsingsSharingLineInBlockNamespace_KeepsIndentation()
   {
      await AssertOrganizeUsingsAsync(0, """
         namespace CoreTestProject.Scope
         {
             using System.Text; using System.Diagnostics;

             public class OrganizeSharedLineIndented
             {
                 private StringBuilder? _builder;
                 private Stopwatch? _watch;
             }
         }

         """, """
         namespace CoreTestProject.Scope
         {
             using System.Diagnostics;
             using System.Text;

             public class OrganizeSharedLineIndented
             {
                 private StringBuilder? _builder;
                 private Stopwatch? _watch;
             }
         }

         """);
   }

   [Fact]
   public async Task UsingTools_Scope_OrganizeUsings_UsingsSharingLineInTabIndentedBlockNamespace_KeepsTabsAndCrLf()
   {
      await AssertOrganizeUsingsAsync(0,
         "namespace CoreTestProject.Scope\r\n{\r\n\tusing System.Text; using System.Diagnostics;\r\n\r\n\tpublic class OrganizeSharedLineTabs\r\n\t{\r\n\t\tprivate StringBuilder? _builder;\r\n\t\tprivate Stopwatch? _watch;\r\n\t}\r\n}\r\n",
         "namespace CoreTestProject.Scope\r\n{\r\n\tusing System.Diagnostics;\r\n\tusing System.Text;\r\n\r\n\tpublic class OrganizeSharedLineTabs\r\n\t{\r\n\t\tprivate StringBuilder? _builder;\r\n\t\tprivate Stopwatch? _watch;\r\n\t}\r\n}\r\n");
   }

   [Fact]
   public async Task UsingTools_Scope_AddUsing_UsingsSharingLineInBlockNamespace_KeepsIndentation()
   {
      await AssertAddUsingAsync("System.Collections", """
         namespace CoreTestProject.Scope
         {
             using System.Text; using System.Diagnostics;

             public class AddSharedLineIndented
             {
             }
         }

         """, """
         namespace CoreTestProject.Scope
         {
             using System.Collections;
             using System.Diagnostics;
             using System.Text;

             public class AddSharedLineIndented
             {
             }
         }

         """);
   }

   /// <summary>
   /// The two usings keep their order, so the second stays on the shared line without indentation of its own.
   /// </summary>
   [Fact]
   public async Task UsingTools_Scope_OrganizeUsings_UsingsSharingLineInOrderInBlockNamespace_StayOnOneLine()
   {
      await AssertOrganizeUsingsAsync(1, """
         namespace CoreTestProject.Scope
         {
             using System.Diagnostics; using System.Text;
             using System.Collections.Concurrent;

             public class OrganizeSharedLineInOrderIndented
             {
                 private StringBuilder? _builder;
                 private Stopwatch? _watch;
             }
         }

         """, """
         namespace CoreTestProject.Scope
         {
             using System.Diagnostics; using System.Text;

             public class OrganizeSharedLineInOrderIndented
             {
                 private StringBuilder? _builder;
                 private Stopwatch? _watch;
             }
         }

         """);
   }

   private const string ContosoToolsSource = """
      namespace Contoso.Tools
      {
          public class Marker
          {
          }
      }

      """;

   private async Task AssertAddUsingTwiceAsync(string @namespace, string source, string expected)
   {
      await AssertAddUsingAsync(@namespace, source, expected);
      var path = GetCoreTestProjectPath("Scope/UsingTarget.cs");

      var second = await new AddUsingTool(WorkspaceManager).AddUsingAsync(path, @namespace, preview: false, CT);

      Assert.False(second.Data.Added);
      Assert.True(second.Data.AlreadyExists);
      Assert.Equal(expected, await File.ReadAllTextAsync(path, CT));
   }

   private async Task AssertAddUsingThrowsAsync(string @namespace, string errorCode, string source)
   {
      var path = await WriteSourceAsync("Scope/UsingTarget.cs", source);

      await AssertThrowsToolExceptionAsync(
         () => new AddUsingTool(WorkspaceManager).AddUsingAsync(path, @namespace, preview: false, CT),
         errorCode);

      Assert.Equal(source, await File.ReadAllTextAsync(path, CT));
   }

   private async Task AssertAddUsingAsync(string @namespace, string source, string expected)
   {
      var path = await WriteSourceAsync("Scope/UsingTarget.cs", source);

      var result = await new AddUsingTool(WorkspaceManager).AddUsingAsync(path, @namespace, preview: false, CT);

      Assert.True(result.Data.Added);
      Assert.True(result.Data.Applied);
      var written = await File.ReadAllTextAsync(path, CT);
      Assert.Equal(expected, written);
      Assert.Empty(ParseDiagnostics(written));
   }

   private async Task AssertAddUsingReportsPresentAsync(string @namespace, string source)
   {
      var path = await WriteSourceAsync("Scope/UsingTarget.cs", source);

      var result = await new AddUsingTool(WorkspaceManager).AddUsingAsync(path, @namespace, preview: false, CT);

      Assert.False(result.Data.Added);
      Assert.True(result.Data.AlreadyExists);
      Assert.Null(result.Data.Applied);
      Assert.Equal(source, await File.ReadAllTextAsync(path, CT));
   }

   private async Task AssertOrganizeUsingsAsync(int expectedRemoved, string source, string expected)
   {
      var path = await WriteSourceAsync("Scope/UsingTarget.cs", source);

      var result = await new OrganizeUsingsTool(WorkspaceManager).OrganizeUsingsAsync(
         new FileScope { Path = path }, preview: false, cancellationToken: CT);

      Assert.True(result.Data.Applied);
      Assert.Equal(1, result.Data.FilesChanged);
      Assert.Equal(expectedRemoved, result.Data.TotalUsingsRemoved);
      var written = await File.ReadAllTextAsync(path, CT);
      Assert.Equal(expected, written);
      Assert.Empty(ParseDiagnostics(written));
   }

   private static List<string> ParseDiagnostics(string text) =>
      [.. CSharpSyntaxTree.ParseText(text, cancellationToken: TestContext.Current.CancellationToken)
         .GetDiagnostics()
         .Select(d => d.ToString())];

   /// <summary>
   /// Writes the source and checks that it parses without diagnostics, so a diagnostic in the result comes from the tool.
   /// </summary>
   private async Task<string> WriteSourceAsync(string relativePath, string text)
   {
      Assert.Empty(ParseDiagnostics(text));
      var path = GetCoreTestProjectPath(relativePath);
      var written = await new WriteFileTool(WorkspaceManager).WriteFileAsync(path, text, CT);
      Assert.True(written.Data.IsNewFile);
      return path;
   }
}
