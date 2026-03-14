using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Intelligence;

[McpServerToolType]
[DotLensWorkerTool]
public class GetMissingMembersTool
{
   private static readonly string[] SuggestedNextToolsWithMissing = ["implement_missing_members to generate stubs"];
   private static readonly string[] SuggestedNextToolsComplete = ["All interface and abstract members are implemented"];

   private readonly IReadOnlyWorkspace _workspace;
   private readonly SymbolLookupService _symbolLookup;

   public GetMissingMembersTool(IReadOnlyWorkspace workspace, SymbolLookupService symbolLookup)
   {
      _workspace = workspace;
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "get_missing_members", ReadOnly = true, OpenWorld = false)]
   [Description("Get interface and abstract members that must be implemented for a type. Use implement_missing_members to generate stub implementations.")]
   public async Task<ToolResult<MissingMembersResponse>> GetMissingMembersAsync(
      [Description("Type to analyze.")] SymbolLookup target,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      CancellationToken cancellationToken = default)
   {
      var _ = _workspace.EnsureSolutionLoaded();

      // Find the type
      var lookupResult = await _symbolLookup.FindSingleAsync<INamedTypeSymbol>(
         target,
         t => t.TypeKind is TypeKind.Class or TypeKind.Struct,
         cancellationToken);

      var typeSymbol = lookupResult.Symbol;

      var interfaceMembers = new List<MissingMemberInfo>();
      var abstractMembers = new List<MissingMemberInfo>();

      foreach (var missing in MissingMemberAnalyzer.GetMissingMembers(typeSymbol))
      {
         var info = CreateMemberInfo(missing.Member, missing.SourceType, missing.IsAbstract, verbosity);
         if (missing.IsAbstract)
            abstractMembers.Add(info);
         else
            interfaceMembers.Add(info);
      }

      var totalMissing = interfaceMembers.Count + abstractMembers.Count;

      return ToolResult.Create(
         new MissingMembersResponse
         {
            TypeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            InterfaceMemberCount = interfaceMembers.Count,
            AbstractMemberCount = abstractMembers.Count,
            InterfaceMembers = verbosity == ToolResultVerbosity.Summary ? null : interfaceMembers,
            AbstractMembers = verbosity == ToolResultVerbosity.Summary ? null : abstractMembers
         },
         totalMissing > 0
            ? SuggestedNextToolsWithMissing
            : SuggestedNextToolsComplete,
         totalMissing,
         totalMissing);
   }

   private static MissingMemberInfo CreateMemberInfo(
      ISymbol member,
      INamedTypeSymbol sourceType,
      bool isAbstract,
      ToolResultVerbosity verbosity)
   {
      return new MissingMemberInfo
      {
         Name = member.Name,
         Kind = member.ToSymbolKindDto(),
         Source = sourceType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
         IsAbstract = isAbstract,
         Signature = verbosity == ToolResultVerbosity.Full
            ? member.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
            : null
      };
   }
}

#region Result Types

/// <summary>
/// Response for get_missing_members tool.
/// </summary>
public record MissingMembersResponse
{
   [Description("Type name (minimally qualified).")]
   public required string TypeName { get; init; }

   [Description("Count of missing interface members.")]
   public required int InterfaceMemberCount { get; init; }

   [Description("Count of missing abstract members from base classes.")]
   public required int AbstractMemberCount { get; init; }

   [Description("Missing interface members. Null when verbosity is 'summary'.")]
   public IReadOnlyList<MissingMemberInfo>? InterfaceMembers { get; init; }

   [Description("Missing abstract members from base classes. Null when verbosity is 'summary'.")]
   public IReadOnlyList<MissingMemberInfo>? AbstractMembers { get; init; }
}

/// <summary>
/// Information about a missing member.
/// </summary>
public record MissingMemberInfo
{
   [Description("Member name.")]
   public required string Name { get; init; }

   [Description("Member kind.")]
   public required SymbolKindDto Kind { get; init; }

   [Description("Interface or base class where the member is declared.")]
   public required string Source { get; init; }

   [Description("True if from an abstract base class (needs 'override'), false if from interface.")]
   public required bool IsAbstract { get; init; }

   [Description("Full member signature. Only included when verbosity is 'full'.")]
   public string? Signature { get; init; }
}

#endregion
