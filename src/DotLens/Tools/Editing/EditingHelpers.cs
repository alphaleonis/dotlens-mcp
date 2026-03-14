using System.ComponentModel;
using System.Globalization;
using DotLens.Infrastructure;
using DotLens.Tools.Shared;

namespace DotLens.Tools.Editing;

#region Response Types

public record EditDiagnosticsResult
{
   [Description("Number of error-level diagnostics.")]
   public required int ErrorCount { get; init; }

   [Description("Number of warning-level diagnostics.")]
   public required int WarningCount { get; init; }

   [Description("Diagnostics found in the file after editing.")]
   public required IReadOnlyList<DiagnosticEntry> Diagnostics { get; init; }
}

#endregion

internal static class EditingHelpers
{
   public static async Task<EditDiagnosticsResult> GetFileDiagnosticsAsync(
      Document document,
      IReadOnlyWorkspace workspace,
      int maxDiagnostics,
      CancellationToken cancellationToken)
   {
      var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
      if (semanticModel is null)
      {
         return new EditDiagnosticsResult
         {
            ErrorCount = 0,
            WarningCount = 0,
            Diagnostics = []
         };
      }

      var rawDiagnostics = semanticModel.GetDiagnostics(cancellationToken: cancellationToken);

      int errorCount = 0;
      int warningCount = 0;
      var entries = new List<DiagnosticEntry>();

      foreach (var d in rawDiagnostics)
      {
         if (d.Severity == DiagnosticSeverity.Hidden)
            continue;

         if (d.Severity == DiagnosticSeverity.Error)
            errorCount++;
         else if (d.Severity == DiagnosticSeverity.Warning)
            warningCount++;

         if (entries.Count < maxDiagnostics)
         {
            var lineSpan = d.Location.GetLineSpan();
            entries.Add(new DiagnosticEntry
            {
               Id = d.Id,
               Severity = d.Severity.ToDtoSeverity(),
               Message = d.GetMessage(CultureInfo.InvariantCulture),
               Location = lineSpan.ToSourceSpan(workspace.FormatPath)
            });
         }
      }

      return new EditDiagnosticsResult
      {
         ErrorCount = errorCount,
         WarningCount = warningCount,
         Diagnostics = entries
      };
   }

}
