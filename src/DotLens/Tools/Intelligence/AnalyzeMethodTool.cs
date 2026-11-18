using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;
namespace DotLens.Tools.Intelligence;

[McpServerToolType]
[DotLensWorkerTool]
public class AnalyzeMethodTool
{
   private static readonly string[] SuggestedNextTools = ["get_method_source for full source code"];

   private readonly IReadOnlyWorkspace _workspaceManager;
   private readonly SymbolLookupService _symbolLookup;

   public AnalyzeMethodTool(IReadOnlyWorkspace workspaceManager, SymbolLookupService symbolLookup)
   {
      _workspaceManager = workspaceManager;
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "analyze_method", ReadOnly = true, OpenWorld = false)]
   [Description("Get comprehensive method analysis in ONE CALL: signature + callers + outgoing calls + location.")]
   public async Task<ToolResult<AnalyzeMethodResponse>> AnalyzeMethodAsync(
      [Description("Method to analyze.")] SymbolLookup target,
      [Description("Which call relationships to include in the analysis.")]
      IncludeCalls includeCalls = IncludeCalls.Incoming,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      [Description("Max incoming calls to return.")]
      [PositiveInteger]
      int maxIncomingCalls = 20,
      [Description("Max outgoing calls to return.")]
      [PositiveInteger]
      int maxOutgoingCalls = 50,
      CancellationToken cancellationToken = default)
   {
      var lookupResult = await _symbolLookup.FindSingleAsync<IMethodSymbol>(
         target,
         m => m.IsCallableMethod(),
         cancellationToken);

      var method = lookupResult.Symbol;
      var containingType = method.ContainingType;

      var location = method.Locations.FirstOrDefault(l => l.IsInSource);
      var lineSpan = location?.GetLineSpan();

      int? callerCount = null;
      List<CallerLocationInfo>? callers = null;
      if (includeCalls is IncludeCalls.Incoming or IncludeCalls.Both)
      {
         (callerCount, callers) = await GetCallersAsync(method, maxIncomingCalls, verbosity, cancellationToken);
      }

      int? outgoingCallCount = null;
      List<OutgoingCallInfo>? outgoingCalls = null;
      if (includeCalls is IncludeCalls.Outgoing or IncludeCalls.Both)
      {
         (outgoingCallCount, outgoingCalls) = await GetOutgoingCallsAsync(method, maxOutgoingCalls, verbosity, cancellationToken);
      }

      return ToolResult.Create(
         new AnalyzeMethodResponse
         {
            TypeName = containingType?.ToDisplayString() ?? "(global)",
            MethodName = method.Name,
            Signature = RoslynDisplayFormats.ToDeclarationString(method, RoslynDisplayFormats.MethodSignatureCompact),
            Location = lineSpan.ToSymbolLocation(_workspaceManager.FormatPath),
            CallerCount = callerCount,
            Callers = callers,
            OutgoingCallCount = outgoingCallCount,
            OutgoingCalls = outgoingCalls
         },
         SuggestedNextTools);
   }

   private async Task<(int count, List<CallerLocationInfo>? callers)> GetCallersAsync(
      IMethodSymbol method,
      int maxIncomingCalls,
      ToolResultVerbosity verbosity,
      CancellationToken cancellationToken)
   {
      var solution = _workspaceManager.EnsureSolutionLoaded();
      var allLocations = await ReferenceSearch.FindReferencesAsync(method, solution, cancellationToken);
      var count = allLocations.Count;

      if (verbosity == ToolResultVerbosity.Summary)
      {
         return (count, null);
      }

      var callers = new List<CallerLocationInfo>();
      foreach (var loc in allLocations.Take(maxIncomingCalls))
      {
         if (loc.Location.SourceTree == null)
            continue;

         var lineSpan = loc.Location.GetLineSpan();
         var text = loc.Location.SourceTree.GetText(cancellationToken);
         var lineText = text.Lines[lineSpan.StartLinePosition.Line].ToString().Trim();

         callers.Add(new CallerLocationInfo
         {
            FilePath = _workspaceManager.FormatPath(lineSpan.Path),
            Line = lineSpan.StartLinePosition.Line + 1,
            Column = lineSpan.StartLinePosition.Character + 1,
            LineText = lineText,
            IsGenerated = loc.Document is SourceGeneratedDocument
         });
      }

      return (count, callers);
   }

   private async Task<(int count, List<OutgoingCallInfo>? calls)> GetOutgoingCallsAsync(
      IMethodSymbol method,
      int maxOutgoingCalls,
      ToolResultVerbosity verbosity,
      CancellationToken cancellationToken)
   {
      var solution = _workspaceManager.EnsureSolutionLoaded();
      var (totalCount, rawCalls) = await IntelligenceHelpers.GetOutgoingCallsWithLocationsAsync(
         method, solution, maxOutgoingCalls, cancellationToken);

      if (verbosity == ToolResultVerbosity.Summary)
      {
         return (totalCount, null);
      }

      var calls = rawCalls
         .Select(call => new OutgoingCallInfo
         {
            Symbol = RoslynHelpers.CreateSymbolResponse(
               call.Symbol, verbosity, _workspaceManager.SolutionPath, _workspaceManager.UseAbsolutePaths),
            CallSite = call.CallSite.ToSymbolLocation(_workspaceManager.FormatPath)
         })
         .ToList();

      return (totalCount, calls);
   }
}

/// <summary>
/// Result for AnalyzeMethodTool.
/// </summary>
public record AnalyzeMethodResponse
{
   [Description("Fully qualified name of the containing type.")]
   public required string TypeName { get; init; }

   public required string MethodName { get; init; }

   [Description("Full method signature including modifiers, return type, and parameters.")]
   public required string Signature { get; init; }

   [Description("Source location of the method definition.")]
   public SymbolLocation? Location { get; init; }

   [Description("Total caller count (useful when Callers list is truncated or null in Summary mode).")]
   public int? CallerCount { get; init; }

   [Description("Call sites where this method is invoked. Null when verbosity is Summary.")]
   public IReadOnlyList<CallerLocationInfo>? Callers { get; init; }

   [Description("Total outgoing call count (useful when OutgoingCalls list is truncated or null in Summary mode).")]
   public int? OutgoingCallCount { get; init; }

   [Description("Methods, properties, and constructors called by this method. Null when verbosity is Summary.")]
   public IReadOnlyList<OutgoingCallInfo>? OutgoingCalls { get; init; }
}

/// <summary>
/// Information about an outgoing call from a method, including the call site location.
/// </summary>
public record OutgoingCallInfo
{
   [Description("The symbol being called (method, property, constructor, etc.).")]
   public required SymbolResponse Symbol { get; init; }

   [Description("Location within the calling method where the call is made.")]
   public required SymbolLocation CallSite { get; init; }
}

/// <summary>
/// Specifies which call relationships to include in method analysis.
/// </summary>
public enum IncludeCalls
{
   /// <summary>No call analysis - only signature and location.</summary>
   None,
   /// <summary>Include incoming calls (who calls this method).</summary>
   Incoming,
   /// <summary>Include outgoing calls (what this method calls).</summary>
   Outgoing,
   /// <summary>Include both incoming and outgoing calls.</summary>
   Both
}
