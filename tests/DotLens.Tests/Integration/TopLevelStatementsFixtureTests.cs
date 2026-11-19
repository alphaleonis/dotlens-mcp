using System.Text.Json;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Navigation;
using DotLens.Tools.Search;
using DotLens.Tools.Shared;
using Xunit;

namespace DotLens.Tests.Integration;

/// <summary>
/// Covers how find_callers and get_containing_member report code in top-level statements, which belongs to a method
/// that the compiler generates.
/// </summary>
public class TopLevelStatementsFixtureTests : IClassFixture<TopLevelStatementsFixtureWorkspace>, IClassFixture<UnusedCodeFixtureWorkspace>
{
   private const string TopLevel = "top-level statements";

   private readonly TopLevelStatementsFixtureWorkspace _fixture;
   private readonly UnusedCodeFixtureWorkspace _unusedCode;

   public TopLevelStatementsFixtureTests(TopLevelStatementsFixtureWorkspace fixture, UnusedCodeFixtureWorkspace unusedCode)
   {
      _fixture = fixture;
      _unusedCode = unusedCode;
   }

   private static CancellationToken CT => TestContext.Current.CancellationToken;

   [Fact]
   public async Task FindCallers_CallInTopLevelStatements_ReportsNoCallingSymbol()
   {
      var result = await FindCallersAsync(_unusedCode.Workspace, "Startup.Run");

      Assert.Equal(["top-level statements <- Program.cs:5:9"], Describe(result));
      Assert.Equal(1, result.TotalCallers);
   }

   [Fact]
   public async Task FindCallers_CallInTopLevelStatements_SerializesTheCallerWithoutCallingSymbol()
   {
      var result = await FindCallersAsync(_unusedCode.Workspace, "Startup.Run");

      var json = JsonSerializer.SerializeToNode(result, DotLensJsonOptions.Default);

      var caller = Assert.Single(json.GetArray("callers"));
      Assert.Equal(["location"], caller.AsObject().Select(property => property.Key));
   }

   /// <summary>
   /// A lambda in the top-level statements belongs to them, and a local function declared there is its own caller.
   /// </summary>
   [Fact]
   public async Task FindCallers_TopLevelStatementsBesideUserMethods_ReportsNoCallingSymbolOnlyForTheStatements()
   {
      var result = await FindCallersAsync(_fixture.Workspace, "Library.Ping");

      Assert.Equal(
         [
            "Method Main Library.cs:15:23 <- Library.cs:15:41",
            "Method Helper Program.Members.cs:4:22 <- Program.Members.cs:4:72",
            "top-level statements <- Program.cs:3:9",
            "top-level statements <- Program.cs:4:36",
            "Method Local Program.cs:9:5 <- Program.cs:9:24",
         ],
         Describe(result));
      Assert.Equal(5, result.TotalCallers);
   }

   [Fact]
   public async Task FindCallers_IndexerInTopLevelStatements_ReportsNoCallingSymbol()
   {
      var result = await FindCallersAsync(_fixture.Workspace, "Table.this[]");

      Assert.Equal(["top-level statements <- Program.cs:6:11"], Describe(result));
      Assert.Equal(1, result.TotalCallers);
   }

   [Fact]
   public async Task FindCallers_TopLevelStatements_GroupUnderOneCallerInSourceOrder()
   {
      var solution = _fixture.Workspace.EnsureSolutionLoaded();
      var lookup = await new SymbolLookupService(_fixture.Workspace).FindSingleAsync<Microsoft.CodeAnalysis.ISymbol>(
         new LookupByName { Name = "Library.Ping" }, cancellationToken: CT);

      var callers = await ReferenceSearch.FindCallersAsync(lookup.Symbol, solution, CT);

      Assert.Equivalent(
         new[] { (TopLevel, "3:9 4:36"), ("Local", "9:24"), ("Main", "15:41"), ("Helper", "4:72") },
         callers.Select(c => (
            c.CallingSymbol?.Name ?? TopLevel,
            string.Join(" ", c.Locations.Select(l => $"{l.GetLineSpan().StartLinePosition.Line + 1}:{l.GetLineSpan().StartLinePosition.Character + 1}")))),
         strict: true);
   }

   [Theory]
   [InlineData(3, 1)]
   [InlineData(4, 30)]
   public async Task GetContainingMember_InTopLevelStatements_ReportsNoMember(int line, int column)
   {
      var tool = new GetContainingMemberTool(_fixture.Workspace);

      var result = await tool.GetContainingMemberAsync(_fixture.GetPath("Program.cs"), line, column, includeHierarchy: true, cancellationToken: CT);

      Assert.Null(result.Data.Member);
      Assert.Null(result.Data.Hierarchy);
      Assert.Null(result.Data.ContextPath);
   }

   [Fact]
   public async Task GetContainingMember_InTopLevelStatementsOfUnusedCodeProgram_ReportsNoMember()
   {
      var tool = new GetContainingMemberTool(_unusedCode.Workspace);

      var result = await tool.GetContainingMemberAsync(_unusedCode.GetPath("Program.cs"), 5, 1, cancellationToken: CT);

      Assert.Null(result.Data.Member);
   }

   [Fact]
   public async Task GetContainingMember_InLocalFunctionOfTopLevelStatements_ReportsTheLocalFunction()
   {
      var tool = new GetContainingMemberTool(_fixture.Workspace);

      var result = await tool.GetContainingMemberAsync(_fixture.GetPath("Program.cs"), 9, 20, includeHierarchy: true, cancellationToken: CT);

      Assert.Equal("Method Local", Describe(result.Data.Member));
      Assert.Equal(["Method Local"], (result.Data.Hierarchy ?? throw new InvalidOperationException("The hierarchy was requested")).Select(Describe));
      Assert.Equal("Local", result.Data.ContextPath);
   }

   [Fact]
   public async Task GetContainingMember_AtUsingDirectiveBesideTopLevelStatements_ReportsSymbolNotFound()
   {
      var tool = new GetContainingMemberTool(_fixture.Workspace);

      var exception = await Assert.ThrowsAsync<ToolException>(
         () => tool.GetContainingMemberAsync(_fixture.GetPath("Program.cs"), 1, 1, cancellationToken: CT));

      Assert.Equal(ErrorCodes.SymbolNotFound, exception.Code);
   }

   private static async Task<FindCallersResponse> FindCallersAsync(IReadOnlyWorkspace workspace, string name)
   {
      var tool = new FindCallersTool(workspace, new SymbolLookupService(workspace));
      var result = await tool.FindCallersAsync(new LookupByName { Name = name }, cancellationToken: CT);
      return result.Data;
   }

   private static List<string> Describe(FindCallersResponse result) =>
      (result.Callers ?? throw new InvalidOperationException("Compact verbosity lists callers"))
         .OrderBy(c => c.Location.FilePath, StringComparer.Ordinal).ThenBy(c => c.Location.Line).ThenBy(c => c.Location.Column)
         .Select(c => $"{(c.CallingSymbol is { } symbol ? DescribeWithLocation(symbol) : TopLevel)} <- {c.Location.FilePath}:{c.Location.Line}:{c.Location.Column}")
         .ToList();

   private static string Describe(SymbolResponse? symbol) =>
      symbol is null ? TopLevel : $"{symbol.GetType().Name.Replace("SymbolResponse", "")} {symbol.Name}";

   private static string DescribeWithLocation(SymbolResponse symbol) =>
      symbol.Location is { } location
         ? $"{Describe(symbol)} {location.FilePath}:{location.Line}:{location.Column}"
         : $"{Describe(symbol)} (no location)";
}
