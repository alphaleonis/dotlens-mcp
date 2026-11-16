using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Intelligence;

#region Response Types

/// <summary>
/// Result for GetTypeOverviewTool.
/// </summary>
public record TypeOverviewResponse
{
   public required string Name { get; init; }
   public required string FullyQualifiedName { get; init; }
   public required string TypeKind { get; init; }
   public required string Accessibility { get; init; }
   public required bool IsAbstract { get; init; }
   public required bool IsSealed { get; init; }
   public required bool IsStatic { get; init; }
   public required bool IsRecord { get; init; }
   /// <summary>First 3 base types (null for summary verbosity).</summary>
   public IReadOnlyList<string>? BaseTypes { get; init; }
   /// <summary>Directly implemented interfaces (null for summary verbosity).</summary>
   public IReadOnlyList<string>? Interfaces { get; init; }
   public required TypeMemberCounts MemberCounts { get; init; }
   /// <summary>Source location (null for metadata types or summary verbosity).</summary>
   public TypeLocationInfo? Location { get; init; }
   /// <summary>True if type is from metadata (NuGet package, framework). Location will be null for metadata types.</summary>
   public bool IsMetadataType { get; init; }
   /// <summary>XML documentation (only for full verbosity).</summary>
   public string? Documentation { get; init; }
}

public record TypeMemberCounts
{
   public required int Methods { get; init; }
   public required int Properties { get; init; }
   public required int Fields { get; init; }
   public required int Events { get; init; }
   public required int Constructors { get; init; }
}

public record TypeLocationInfo
{
   public required string FilePath { get; init; }
   public required int Line { get; init; }
}

#endregion

[McpServerToolType]
[DotLensWorkerTool]
public class GetTypeOverviewTool
{
   private static readonly string[] SuggestedNextTools =
   [
      "get_type_members for detailed member list",
      "get_derived_types to find implementations"
   ];

   private readonly IReadOnlyWorkspace _workspace;
   private readonly SymbolLookupService _symbolLookup;

   public GetTypeOverviewTool(IReadOnlyWorkspace workspace, SymbolLookupService symbolLookup)
   {
      _workspace = workspace;
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "get_type_overview", ReadOnly = true, OpenWorld = false)]
   [Description("Get type overview: modifiers, base types (first 3), interfaces, and member counts.")]
   public async Task<ToolResult<TypeOverviewResponse>> GetTypeOverviewAsync(
      [Description("Type to get overview for")] SymbolLookup target,
      [Description("Response detail level.")]
      ToolResultVerbosity verbosity = ToolResultVerbosity.Compact,
      CancellationToken cancellationToken = default)
   {
      var lookupResult = await _symbolLookup.FindSingleAsync<INamedTypeSymbol>(target, cancellationToken: cancellationToken);
      var typeSymbol = lookupResult.Symbol;

      // Always compute member counts (needed for all verbosity levels)
      var members = typeSymbol.GetMembers();
      var memberCounts = new TypeMemberCounts
      {
         Methods = members.OfType<IMethodSymbol>().Count(m => m.MethodKind == MethodKind.Ordinary),
         Properties = members.OfType<IPropertySymbol>().Count(),
         Fields = members.OfType<IFieldSymbol>().Count(f => !f.IsImplicitlyDeclared),
         Events = members.OfType<IEventSymbol>().Count(),
         Constructors = members.OfType<IMethodSymbol>().Count(m => m.MethodKind == MethodKind.Constructor)
      };

      // Base types and interfaces only for Compact and Full
      List<string>? baseTypes = null;
      List<string>? interfaces = null;

      if (verbosity != ToolResultVerbosity.Summary)
      {
         baseTypes = [];
         var current = typeSymbol.BaseType;
         while (current != null && current.SpecialType != SpecialType.System_Object && baseTypes.Count < 3)
         {
            baseTypes.Add(current.ToDisplayString());
            current = current.BaseType;
         }

         interfaces = typeSymbol.Interfaces.Select(i => i.ToDisplayString()).ToList();
      }

      // Location - check if type is from source or metadata
      var sourceLocation = typeSymbol.Locations.FirstOrDefault(l => l.IsInSource);
      var isMetadataType = sourceLocation == null;
      TypeLocationInfo? locationInfo = null;

      if (sourceLocation != null)
      {
         var lineSpan = sourceLocation.GetLineSpan();
         var formattedPath = _workspace.FormatPath(lineSpan.Path);
         if (formattedPath != null)
         {
            locationInfo = new TypeLocationInfo
            {
               FilePath = formattedPath,
               Line = lineSpan.StartLinePosition.Line + 1
            };
         }
      }

      // Documentation only for Full verbosity
      string? documentation = null;
      if (verbosity == ToolResultVerbosity.Full)
      {
         documentation = typeSymbol.GetDocumentationCommentXml(cancellationToken: cancellationToken);
         if (string.IsNullOrWhiteSpace(documentation))
            documentation = null;
      }

      return ToolResult.Create(
         new TypeOverviewResponse
         {
            Name = RoslynHelpers.GetName(typeSymbol),
            FullyQualifiedName = typeSymbol.ToDisplayString(),
            TypeKind = typeSymbol.TypeKind.ToString(),
            Accessibility = typeSymbol.DeclaredAccessibility.ToString(),
            IsAbstract = typeSymbol.IsAbstract,
            IsSealed = typeSymbol.IsSealed,
            IsStatic = typeSymbol.IsStatic,
            IsRecord = typeSymbol.IsRecord,
            BaseTypes = baseTypes,
            Interfaces = interfaces,
            MemberCounts = memberCounts,
            Location = locationInfo,
            IsMetadataType = isMetadataType,
            Documentation = documentation
         },
         suggestedNextTools: SuggestedNextTools,
         verbosity: verbosity);
   }
}
