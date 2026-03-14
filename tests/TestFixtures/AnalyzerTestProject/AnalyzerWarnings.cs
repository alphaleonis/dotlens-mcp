// This file intentionally contains code that triggers analyzer warnings
// for testing roslyn_get_diagnostics with includeAnalyzers=true

using System.Net.Http;

namespace AnalyzerTestProject;

public class AnalyzerWarnings
{
   // CA2007: Consider calling ConfigureAwait on the awaited task
   // This method awaits without ConfigureAwait(false)
   public async Task<string> MissingConfigureAwait()
   {
      using var client = new HttpClient();
      var response = await client.GetStringAsync("https://example.com");
      return response;
   }

   // CA2016: Forward the CancellationToken parameter to methods
   // This method accepts a token but doesn't forward it
   public async Task<string> MissingCancellationForwarding(CancellationToken cancellationToken)
   {
      using var client = new HttpClient();
      // Not forwarding cancellationToken to GetStringAsync
      var response = await client.GetStringAsync("https://example.com");
      return response;
   }

   // CA1822: Member does not access instance data and can be marked as static
   public int CouldBeStatic(int x, int y)
   {
      return x + y;
   }

   // CA2000: Dispose objects before losing scope
   public HttpClient CreateWithoutDispose()
   {
      var client = new HttpClient(); // Not using 'using' or disposing
      return client;
   }

   // IDE0060: Remove unused parameter (may show as CA1801 depending on config)
   public void UnusedParameter(int used, int unused)
   {
      Console.WriteLine(used);
      // 'unused' parameter is never used
   }
}

// CA1052: Static holder types should be Static or NotInheritable
public class StaticHolder
{
   public static void DoSomething() { }
}
