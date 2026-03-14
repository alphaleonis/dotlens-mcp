using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Intelligence;

[McpServerToolType]
[DotLensWorkerTool]
public class GetMethodSourceTool
{
   private static readonly string[] SuggestedNextTools =
   [
      "analyze_method for call analysis",
      "find_callers to see usages"
   ];

   private readonly IReadOnlyWorkspace _workspace;
   private readonly SymbolLookupService _symbolLookup;

   public GetMethodSourceTool(IReadOnlyWorkspace workspace, SymbolLookupService symbolLookup)
   {
      _workspace = workspace;
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "get_method_source", ReadOnly = true, OpenWorld = false)]
   [Description("Get method source code. Use instead of reading entire files.")]
   public async Task<ToolResult<MethodSourceResponse>> GetMethodSourceAsync(
      [Description("Target method.")] SymbolLookup target,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      CancellationToken cancellationToken = default)
   {
      _workspace.EnsureSolutionLoaded();

      var lookupResult = await _symbolLookup.FindSingleAsync<IMethodSymbol>(
         target,
         m => m.IsCallableMethod(),
         cancellationToken);

      var method = lookupResult.Symbol;

      var location = method.Locations.FirstOrDefault(l => l.IsInSource);
      if (location == null)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            "Method is defined in metadata, not source code",
            "This method comes from a compiled assembly. Only source methods can be retrieved.");
      }

      var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
      if (syntaxRef == null)
      {
         throw new ToolException(
            ErrorCodes.AnalysisFailed,
            "Could not get method syntax");
      }

      var methodSyntax = await syntaxRef.GetSyntaxAsync(cancellationToken);
      var sourceCode = methodSyntax.ToFullString();

      // Apply compact output for non-full verbosity
      if (verbosity != ToolResultVerbosity.Full)
      {
         sourceCode = RoslynHelpers.CompactSourceOutput(sourceCode);
      }

      var lineSpan = location.GetLineSpan();

      return ToolResult.Create(
         new MethodSourceResponse
         {
            Location = lineSpan.ToSourceSpan(_workspace.FormatPath),
            Source = sourceCode
         },
         SuggestedNextTools);
   }
}

/// <summary>
/// Result containing method source code and location.
/// </summary>
public record MethodSourceResponse
{
   /// <summary>Source location for navigation and editing.</summary>
   [Description("File path and line/column range.")]
   public required SourceSpan Location { get; init; }

   /// <summary>Method source code.</summary>
   [Description("Complete method source code.")]
   public required string Source { get; init; }
}
