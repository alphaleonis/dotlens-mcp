using DotLens.Infrastructure;
using DotLens.Tools.Analysis;
using DotLens.Tools.Shared;
using Xunit;

namespace DotLens.Tests.Integration;

/// <summary>
/// Covers how find_unused_code treats members that a framework calls and members that only reach each other.
/// </summary>
public class UnusedCodeFixtureTests : IClassFixture<UnusedCodeFixtureWorkspace>
{
   private readonly UnusedCodeFixtureWorkspace _fixture;

   public UnusedCodeFixtureTests(UnusedCodeFixtureWorkspace fixture)
   {
      _fixture = fixture;
   }

   private static CancellationToken CT => TestContext.Current.CancellationToken;

   /// <summary>
   /// Test methods, the test class's constructor, controller actions, a page handler and a JSON constructor have no
   /// reference in source.
   /// </summary>
   [Fact]
   public async Task FindUnusedCode_ReflectionInvokedMembers_ReportsOnlyMembersNoFrameworkCalls()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(
         includeInternal: true, verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equivalent(
         new[]
         {
            (typeof(ClassSymbolResponse), "Jobs", 74),
            (typeof(MethodSymbolResponse), "NotATest", 25),
            (typeof(MethodSymbolResponse), "Audit", 40),
            (typeof(MethodSymbolResponse), "Unreachable", 44),
            (typeof(MethodSymbolResponse), "Describe", 55),
            (typeof(MethodSymbolResponse), "Nightly", 77),
            (typeof(PropertySymbolResponse), "Unnamed", 86),
         },
         UnusedSymbolsIn(result.Data, "ReflectionInvoked.cs"),
         strict: true);
   }

   [Theory]
   [InlineData("Scheduled")]
   [InlineData("scheduledAttribute")]
   [InlineData("UnusedCodeTestProject.ScheduledAttribute")]
   public async Task FindUnusedCode_ReflectionInvokedByNamedAttribute_NotReported(string attributeName)
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(
         includeInternal: true,
         entryPointAttributes: [attributeName],
         verbosity: ToolResultVerbosity.Full,
         maxResults: 500,
         cancellationToken: CT);

      Assert.Equivalent(
         new[]
         {
            (typeof(MethodSymbolResponse), "NotATest", 25),
            (typeof(MethodSymbolResponse), "Audit", 40),
            (typeof(MethodSymbolResponse), "Unreachable", 44),
            (typeof(MethodSymbolResponse), "Describe", 55),
            (typeof(PropertySymbolResponse), "Unnamed", 86),
         },
         UnusedSymbolsIn(result.Data, "ReflectionInvoked.cs"),
         strict: true);
   }

   /// <summary>
   /// The top-level statements register SystemClock and TimingMiddleware with generic arguments and create Plugin from
   /// its typeof, so the framework calls their constructors and the middleware's InvokeAsync. Nothing names
   /// UnregisteredService.
   /// </summary>
   [Fact]
   public async Task FindUnusedCode_ReflectionInvokedThroughTypeUse_ReportsOnlyUnnamedType()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(
         includeInternal: true, verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equivalent(
         new[]
         {
            (typeof(ClassSymbolResponse), "UnregisteredService", 54),
            (typeof(ConstructorSymbolResponse), ".ctor", 56),
            (typeof(MethodSymbolResponse), "Prepare", 61),
         },
         UnusedSymbolsIn(result.Data, "DependencyInjection.cs"),
         strict: true);
   }

   /// <summary>
   /// The public Parse and the top-level statements reach two cycles, and nothing reaches the others.
   /// </summary>
   [Fact]
   public async Task FindUnusedCode_MutualRecursion_ReportsCyclesNoRootReaches()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(
         includeInternal: true, verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equivalent(
         new[]
         {
            (typeof(ClassSymbolResponse), "Ping", 28),
            (typeof(ClassSymbolResponse), "Pong", 33),
            (typeof(FieldSymbolResponse), "First", 5),
            (typeof(FieldSymbolResponse), "Second", 7),
            (typeof(MethodSymbolResponse), "IsEven", 15),
            (typeof(MethodSymbolResponse), "IsOdd", 17),
            (typeof(PropertySymbolResponse), "Seed", 19),
            (typeof(MethodSymbolResponse), "Next", 21),
            (typeof(MethodSymbolResponse), "Send", 30),
            (typeof(MethodSymbolResponse), "Reply", 35),
         },
         UnusedSymbolsIn(result.Data, "MutualRecursion.cs"),
         strict: true);
   }

   /// <summary>
   /// The library's internals are visible only to a project of the solution, which does not use them.
   /// </summary>
   [Fact]
   public async Task FindUnusedCode_MutualRecursionOfInternalsWithFriendInSolution_ReportsCycle()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(
         includeInternal: true, verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equivalent(
         new[]
         {
            (typeof(ClassSymbolResponse), "Ping", 3),
            (typeof(MethodSymbolResponse), "Send", 5),
            (typeof(ClassSymbolResponse), "Pong", 8),
            (typeof(MethodSymbolResponse), "Reply", 10),
         },
         UnusedSymbolsIn(result.Data, "TestedCycles.cs"),
         strict: true);
   }

   /// <summary>
   /// The library's internals are visible to an assembly outside the solution, which could call them.
   /// </summary>
   [Fact]
   public async Task FindUnusedCode_MutualRecursionOfInternalsWithFriendAssembly_ReportsOnlyPrivateCycle()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(
         includeInternal: true, verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equivalent(
         new[]
         {
            (typeof(ClassSymbolResponse), "Counter", 13),
            (typeof(MethodSymbolResponse), "Up", 15),
            (typeof(MethodSymbolResponse), "Down", 17),
         },
         UnusedSymbolsIn(result.Data, "FriendCycles.cs"),
         strict: true);
   }

   /// <summary>
   /// Nothing names DeadHandle. The top-level statements dispose a Handle with using, a call to IDisposable.Dispose, so
   /// every Dispose counts as called. They format a Handle, which calls ToString without a reference to object.ToString,
   /// so DeadHandle.ToString runs only through its type.
   /// </summary>
   [Fact]
   public async Task FindUnusedCode_InterfaceImplementationsAndOverrides_RunWhenTheirTypeIsUsedOrTheirBaseIsCalled()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(
         includeInternal: true, verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equivalent(
         new[]
         {
            (typeof(ClassSymbolResponse), "DeadHandle", 16),
            (typeof(MethodSymbolResponse), "Describe", 26),
         },
         UnusedSymbolsIn(result.Data, "DispatchRoots.cs"),
         strict: true);
   }

   /// <summary>
   /// Only an assembly scan creates ScannedHandler, and live code calls Handle through the interface.
   /// </summary>
   [Fact]
   public async Task FindUnusedCode_ReflectionInvokedThroughInterface_ReportsOnlyScannedType()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(
         includeInternal: true, verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equivalent(
         new[] { (typeof(ClassSymbolResponse), "ScannedHandler", 8) },
         UnusedSymbolsIn(result.Data, "Scanning.cs"),
         strict: true);
   }

   /// <summary>
   /// Money names itself in its base list and Svc in a field type, which create neither. Live code names Outer only to
   /// qualify Inner in a typeof, and hands Payload to a generic method inside another type argument.
   /// </summary>
   [Fact]
   public async Task FindUnusedCode_ReflectionInvokedThroughTypeArgument_CountsOnlyTypesHandedOver()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(
         includeInternal: true, verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equivalent(
         new[]
         {
            (typeof(ConstructorSymbolResponse), ".ctor", 11),
            (typeof(MethodSymbolResponse), "Init", 18),
            (typeof(ConstructorSymbolResponse), ".ctor", 31),
            (typeof(MethodSymbolResponse), "Boot", 38),
            (typeof(ConstructorSymbolResponse), ".ctor", 45),
            (typeof(MethodSymbolResponse), "OuterInit", 50),
         },
         UnusedSymbolsIn(result.Data, "TypeArguments.cs"),
         strict: true);
   }

   /// <summary>
   /// Only a test in the executable, which depends on the library, names Rows in a MemberData string.
   /// </summary>
   [Fact]
   public async Task FindUnusedCode_ReflectionInvokedDataSourceNamedFromDependentProject_NotReported()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(
         projectName: "UnusedCodeTestedLibrary",
         includeInternal: true,
         verbosity: ToolResultVerbosity.Full,
         maxResults: 500,
         cancellationToken: CT);

      Assert.Equivalent(
         new[] { (typeof(PropertySymbolResponse), "Unread", 7) },
         UnusedSymbolsIn(result.Data, "LibraryData.cs"),
         strict: true);
   }

   /// <summary>
   /// Live code hands each item type to a framework or a generic type in a different shape. It names DeclaredItem only
   /// in a field type, which creates none.
   /// </summary>
   [Fact]
   public async Task FindUnusedCode_ReflectionInvokedThroughHandoverShapes_ReportsOnlyDeclaredType()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(
         includeInternal: true, verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equivalent(
         new[]
         {
            (typeof(ConstructorSymbolResponse), ".ctor", 71),
            (typeof(MethodSymbolResponse), "DeclaredInit", 73),
         },
         UnusedSymbolsIn(result.Data, "HandoverShapes.cs"),
         strict: true);
   }

   /// <summary>
   /// Live code uses each item type only inside an argument of a generic call: as a created argument, in a lambda and
   /// as an argument whose type the call infers. None of them hands the type over.
   /// </summary>
   [Fact]
   public async Task FindUnusedCode_ReflectionInvokedTypeOnlyInGenericCallArguments_ReportsUnusedConstructors()
   {
      var tool = new FindUnusedCodeTool(_fixture.Workspace);

      var result = await tool.FindUnusedCodeAsync(
         includeInternal: true, verbosity: ToolResultVerbosity.Full, maxResults: 500, cancellationToken: CT);

      Assert.Equivalent(
         new[]
         {
            (typeof(ConstructorSymbolResponse), ".ctor", 9),
            (typeof(MethodSymbolResponse), "AddedInit", 11),
            (typeof(ConstructorSymbolResponse), ".ctor", 22),
            (typeof(MethodSymbolResponse), "SelectedInit", 26),
            (typeof(ConstructorSymbolResponse), ".ctor", 37),
            (typeof(MethodSymbolResponse), "ComparedInit", 41),
         },
         UnusedSymbolsIn(result.Data, "ArgumentUses.cs"),
         strict: true);
   }

   private static List<(Type Kind, string Name, int Line)> UnusedSymbolsIn(FindUnusedCodeResponse data, string fileName) =>
      data.UnusedSymbols
         .Where(s => Path.GetFileName(s.Location?.FilePath) == fileName)
         .Select(s => (s.GetType(), s.Name, s.Location?.Line ?? 0))
         .ToList();
}
