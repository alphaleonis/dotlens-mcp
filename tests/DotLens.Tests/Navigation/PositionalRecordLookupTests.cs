using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Navigation;
using DotLens.Tools.Search;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DotLens.Tests.Navigation;

/// <summary>
/// Covers position lookups at a positional parameter, which in a record also declares the property that the record
/// synthesizes for it.
/// </summary>
[Collection("TestFixtures")]
public class PositionalRecordLookupTests : TestFixtureTestBase
{
   public PositionalRecordLookupTests(TestFixturesCollectionFixture fixture) : base(fixture) { }

   private const string FixtureFile = "Lookup/PositionalRecordLookups.cs";

   private SymbolLookupService _symbolLookup = null!;

   public override ValueTask InitializeAsync()
   {
      _symbolLookup = new SymbolLookupService(Workspace);
      return ValueTask.CompletedTask;
   }

   [Fact]
   public async Task SearchSymbols_ReportsEachPropertyOfTheFixtureOnce()
   {
      var properties = await SearchFixturePropertiesAsync();

      Assert.Equal(
         [
            "5:42 BadgeNumber",
            "5:62 BadgeHolder",
            "7:45 GaugeReading",
            "9:51 MeterTicks",
            "11:53 EnvelopePayload",
            "15:42 NestedLabel",
            "18:43 TallyCount",
            "22:15 DoubledTally",
            "25:36 SquareEdge",
            "27:15 SquareArea",
            "34:15 ExplicitCode",
            "37:40 TicketCode",
            "39:59 TicketSeat",
         ],
         properties.Select(p => $"{p.Line}:{p.Column} {p.Name}"));
   }

   [Fact]
   public async Task FindCallers_AtEachPropertyPositionSearchSymbolsReports_ResolvesThePropertyAndItsCallers()
   {
      var tool = new FindCallersTool(Workspace, _symbolLookup);
      var properties = await SearchFixturePropertiesAsync();
      Assert.NotEmpty(properties);

      var actual = new List<string>();
      foreach (var property in properties)
      {
         var result = await tool.FindCallersAsync(PositionOf(property), cancellationToken: CT);
         var callers = result.Data.Callers ?? throw new InvalidOperationException("Compact verbosity lists the callers");
         actual.Add($"{Describe(result.Data.Symbol)}: {string.Join(", ", callers.Select(c => $"{c.CallingSymbol.Name} {c.Location.Line}:{c.Location.Column}"))}");
      }

      // Find-references cascades from a positional property to its parameter, so SquareArea's initializer counts.
      Assert.Equal(
         [
            "Property BadgeNumber at 5:42: ReadAll 64:23",
            "Property BadgeHolder at 5:62: ReadAll 64:43",
            "Property GaugeReading at 7:45: ReadAll 64:63",
            "Property MeterTicks at 9:51: ReadAll 64:84",
            "Property EnvelopePayload at 11:53: ReadAll 65:25",
            "Property NestedLabel at 15:42: ReadAll 65:50",
            "Property TallyCount at 18:43: DoubledTally 22:31, ReadAll 65:70",
            "Property DoubledTally at 22:15: ReadAll 65:89",
            "Property SquareEdge at 25:36: ReadAll 66:91, <SquareArea>k__BackingField 27:37, <SquareArea>k__BackingField 27:50",
            "Property SquareArea at 27:15: ReadAll 66:111",
            "Property ExplicitCode at 34:15: ReadAll 66:29",
            "Property TicketCode at 37:40: ReadAll 66:51",
            "Property TicketSeat at 39:59: ReadAll 66:71",
         ],
         actual);
   }

   [Fact]
   public async Task FindReferences_AtEachPropertyPositionSearchSymbolsReports_ResolvesThePropertyAndItsReferences()
   {
      var tool = new FindReferencesTool(Workspace, _symbolLookup);
      var properties = await SearchFixturePropertiesAsync();
      Assert.NotEmpty(properties);

      var actual = new List<string>();
      foreach (var property in properties)
      {
         var result = await tool.FindReferencesAsync(PositionOf(property), cancellationToken: CT);
         var references = result.Data.References ?? throw new InvalidOperationException("Compact verbosity lists the references");
         actual.Add($"{Describe(result.Data.Symbol)}: {string.Join(", ", references.Select(r => $"{r.Line}:{r.Column}"))}");
      }

      // Find-references cascades from a positional property to its parameter, so SquareArea's initializer counts.
      Assert.Equal(
         [
            "Property BadgeNumber at 5:42: 64:23",
            "Property BadgeHolder at 5:62: 64:43",
            "Property GaugeReading at 7:45: 64:63",
            "Property MeterTicks at 9:51: 64:84",
            "Property EnvelopePayload at 11:53: 65:25",
            "Property NestedLabel at 15:42: 65:50",
            "Property TallyCount at 18:43: 22:31, 65:70",
            "Property DoubledTally at 22:15: 65:89",
            "Property SquareEdge at 25:36: 66:91, 27:37, 27:50",
            "Property SquareArea at 27:15: 66:111",
            "Property ExplicitCode at 34:15: 66:29",
            "Property TicketCode at 37:40: 66:51",
            "Property TicketSeat at 39:59: 66:71",
         ],
         actual);
   }

   /// <summary>
   /// Every lookup that accepts both the parameter and the property resolves the property, which search_symbols reports
   /// there. See SymbolLookupService.FindByPositionAsync.
   /// </summary>
   [Theory]
   [InlineData(5, 42, "Property CoreTestProject.Lookup.PositionalBadge.BadgeNumber")]
   [InlineData(7, 45, "Property CoreTestProject.Lookup.PositionalGauge.GaugeReading")]
   [InlineData(9, 51, "Property CoreTestProject.Lookup.PositionalMeter.MeterTicks")]
   [InlineData(11, 53, "Property CoreTestProject.Lookup.PositionalEnvelope<TPayload>.EnvelopePayload")]
   [InlineData(15, 42, "Property CoreTestProject.Lookup.PositionalOuter.PositionalNested.NestedLabel")]
   [InlineData(18, 43, "Property CoreTestProject.Lookup.PositionalTally.TallyCount")]
   [InlineData(25, 36, "Property CoreTestProject.Lookup.PositionalSquare.SquareEdge")]
   [InlineData(32, 38, "Parameter int ExplicitCode")]
   [InlineData(39, 43, "Parameter int TicketCode")]
   [InlineData(41, 41, "Parameter int primaryLength")]
   [InlineData(46, 43, "Parameter int primaryDepth")]
   public async Task FindSingle_AnySymbolAtPositionalParameter_ResolvesThePropertyWhereTheParameterDeclaresOne(int line, int column, string expected)
   {
      var result = await _symbolLookup.FindSingleAsync<ISymbol>(Position(line, column), cancellationToken: CT);

      Assert.Equal(expected, $"{result.Symbol.Kind} {result.Symbol.ToDisplayString()}");
   }

   [Theory]
   [InlineData(5, 42, "BadgeNumber")]
   [InlineData(9, 51, "MeterTicks")]
   [InlineData(11, 53, "EnvelopePayload")]
   [InlineData(32, 38, "ExplicitCode")]
   [InlineData(41, 41, "primaryLength")]
   public async Task FindSingle_ParameterAtPositionalParameter_ResolvesTheParameter(int line, int column, string expected)
   {
      var byType = await _symbolLookup.FindSingleAsync<IParameterSymbol>(Position(line, column), cancellationToken: CT);
      var byFilter = await _symbolLookup.FindSingleAsync<ISymbol>(Position(line, column), s => s is IParameterSymbol, CT);

      Assert.Equal(expected, byType.Symbol.Name);
      Assert.Equal(MethodKind.Constructor, Assert.IsAssignableFrom<IMethodSymbol>(byType.Symbol.ContainingSymbol).MethodKind);
      Assert.Same(byType.Symbol, byFilter.Symbol);
   }

   /// <summary>
   /// A use of the parameter in an initializer refers to the parameter alone.
   /// </summary>
   [Theory]
   [InlineData(27, 37)]
   [InlineData(27, 50)]
   public async Task FindSingle_AnySymbolAtParameterUse_ResolvesTheParameter(int line, int column)
   {
      var result = await _symbolLookup.FindSingleAsync<ISymbol>(Position(line, column), cancellationToken: CT);

      Assert.Equal("Parameter int SquareEdge", $"{result.Symbol.Kind} {result.Symbol.ToDisplayString()}");
   }

   [Theory]
   [InlineData(32, 38)]
   [InlineData(39, 43)]
   [InlineData(41, 41)]
   [InlineData(46, 43)]
   public async Task FindCallers_AtPositionalParameterThatDeclaresNoProperty_ReportsSymbolNotFound(int line, int column)
   {
      var tool = new FindCallersTool(Workspace, _symbolLookup);

      var exception = await Assert.ThrowsAsync<ToolException>(() => tool.FindCallersAsync(Position(line, column), cancellationToken: CT));

      Assert.Equal(ErrorCodes.SymbolNotFound, exception.Code);
   }

   private async Task<List<(string Name, int Line, int Column)>> SearchFixturePropertiesAsync()
   {
      var search = new SearchSymbolsTool(Workspace);
      var result = await search.SearchSymbolsAsync("*", kind: SymbolKindFilter.Property, project: "CoreTestProject", maxResults: 1000, cancellationToken: CT);
      Assert.False(result.Data.HasMore);

      var fixturePath = GetCoreTestProjectPath(FixtureFile);
      return result.Data.Items
         .Select(item => item.Location ?? throw new InvalidOperationException($"{item.Name} has no location"))
         .Zip(result.Data.Items)
         .Where(pair => GetAbsolutePath(pair.First.FilePath) == fixturePath)
         .Select(pair => (pair.Second.Name, pair.First.Line, pair.First.Column))
         .OrderBy(p => p.Line)
         .ThenBy(p => p.Column)
         .ToList();
   }

   private LookupByPosition PositionOf((string Name, int Line, int Column) property) => Position(property.Line, property.Column);

   private LookupByPosition Position(int line, int column) =>
      new() { FilePath = GetCoreTestProjectPath(FixtureFile), Line = line, Column = column };

   private static string Describe(SymbolResponse symbol) =>
      symbol.Location is { } location
         ? $"{symbol.GetType().Name.Replace("SymbolResponse", "")} {symbol.Name} at {location.Line}:{location.Column}"
         : throw new InvalidOperationException($"{symbol.Name} has no location");
}
