using System.ComponentModel;
using DotLens.Infrastructure;
using DotLens.Infrastructure.SymbolLookup;
using DotLens.Tools.Shared;
using Microsoft.CodeAnalysis;
using ModelContextProtocol.Server;

namespace DotLens.Tools.Search;

/// <summary>
/// Get method signatures for all overloads.
/// </summary>
[McpServerToolType]
[DotLensWorkerTool]
public class GetMethodInfoTool
{
   private readonly IReadOnlyWorkspace _workspace;
   private readonly SymbolLookupService _symbolLookup;

   public GetMethodInfoTool(IReadOnlyWorkspace workspace, SymbolLookupService symbolLookup)
   {
      _workspace = workspace;
      _symbolLookup = symbolLookup;
   }

   [McpServerTool(Name = "get_method_info", ReadOnly = true, OpenWorld = false)]
   [Description("Get method signatures for all overloads. Returns code-style declarations that are easy to read and use.")]
   public async Task<ToolResult<MethodInfoResponse>> GetMethodInfoAsync(
      [Description("Method to look up.")] SymbolLookup target,
      CancellationToken cancellationToken = default)
   {
      // Find any matching method (we'll get all overloads from its containing type)
      var results = await _symbolLookup.FindAsync<IMethodSymbol>(
         target,
         m => m.IsCallableMethod(),
         cancellationToken);

      if (results.Count == 0)
      {
         throw new ToolException(
            ErrorCodes.SymbolNotFound,
            "No method found matching the lookup criteria",
            "Check the method name or position. Use search_symbols to find methods.");
      }

      var method = results[0].Symbol;
      var containingType = method.ContainingType;

      if (containingType is null)
      {
         throw new ToolException(
            ErrorCodes.NotSupported,
            "Method has no containing type (e.g., top-level function)",
            "Use get_method_source for top-level functions instead.");
      }

      // Get all overloads of this method
      var overloads = containingType.GetMembers(method.Name)
         .OfType<IMethodSymbol>()
         .Where(m => m.IsCallableMethod())
         .Select(m =>
         {
            var lineSpan = m.Locations.FirstOrDefault(loc => loc.IsInSource)?.GetLineSpan();

            return new MethodOverloadSignature
            {
               Signature = FormatMethodDeclaration(m),
               Location = lineSpan.ToSourceSpan(_workspace.FormatPath)
            };
         })
         .ToList();

      var suggestions = new List<string>
      {
         "get_method_source to see the implementation",
         "find_callers to see what calls this method"
      };

      return ToolResult.Create(
         new MethodInfoResponse
         {
            MethodName = method.Name,
            ContainingType = containingType.ToDisplayString(),
            Overloads = overloads
         },
         suggestedNextTools: suggestions.ToArray());
   }

   /// <summary>
   /// Formats a method as a C# declaration using Roslyn's built-in formatting.
   /// Note: async is added manually since it's not part of the IL signature but is useful context.
   /// </summary>
   private static string FormatMethodDeclaration(IMethodSymbol method)
   {
      var display = RoslynDisplayFormats.ToDeclarationString(method, RoslynDisplayFormats.MethodSignatureFull);

      // async isn't part of the method signature in IL, but it's useful context
      // Insert it after the modifiers (static/virtual/etc.) but before the return type
      if (method.IsAsync)
      {
         display = InsertAsyncModifier(display);
      }

      return display;
   }

   /// <summary>
   /// Known C# modifiers that can appear before the return type.
   /// </summary>
   private static readonly string[] KnownModifiers =
      ["public", "private", "protected", "internal", "static", "virtual", "override", "abstract", "sealed", "new", "extern", "unsafe", "partial", "readonly", "file"];

   /// <summary>
   /// Inserts "async" after accessibility and other modifiers, before the return type.
   /// Pattern: "public [static|virtual|override|abstract|sealed]* RETURN_TYPE Method(...)"
   /// </summary>
   private static string InsertAsyncModifier(string display)
   {
      var span = display.AsSpan();
      var insertPosition = 0;

      // Scan through space-separated tokens until we find one that's not a modifier
      while (insertPosition < span.Length)
      {
         // Find the end of the current token
         var remaining = span[insertPosition..];
         var spaceIndex = remaining.IndexOf(' ');
         var token = spaceIndex >= 0 ? remaining[..spaceIndex] : remaining;

         // Check if this token is a known modifier
         var isModifier = false;
         foreach (var modifier in KnownModifiers)
         {
            if (token.SequenceEqual(modifier.AsSpan()))
            {
               isModifier = true;
               break;
            }
         }

         if (!isModifier)
            break;

         // Move past this modifier and its trailing space
         insertPosition += token.Length;
         if (insertPosition < span.Length && span[insertPosition] == ' ')
            insertPosition++;
      }

      // Insert "async " at the found position
      return string.Concat(span[..insertPosition], "async ", span[insertPosition..]);
   }
}

/// <summary>
/// Response for get_method_info tool.
/// </summary>
public record MethodInfoResponse
{
   [Description("Method name.")]
   public required string MethodName { get; init; }

   [Description("Fully qualified containing type.")]
   public required string ContainingType { get; init; }

   [Description("All overloads with their signatures.")]
   public required IReadOnlyList<MethodOverloadSignature> Overloads { get; init; }
}

/// <summary>
/// A method overload's signature and location.
/// </summary>
public record MethodOverloadSignature
{
   [Description("Method declaration as C# code (e.g., 'public static string Format(string value)').")]
   public required string Signature { get; init; }

   [Description("Source location span (null for metadata methods).")]
   public SourceSpan? Location { get; init; }
}
