using System.Text.Json;
using Xunit;

namespace DotLens.Tests.Integration;

/// <summary>
/// E2E tests that spawn actual server/worker processes and verify tool lists via MCP protocol.
/// Uses the MCP SDK client for proper protocol handling.
/// </summary>
public class ToolListE2ETests
{
   [Fact]
   public async Task Worker_ToolsList_IncludesInternalTools()
   {
      await using var fixture = await DotLensMcpFixture.CreateAsync(workerMode: true);

      var tools = await fixture.Client.ListToolsAsync();
      var toolNames = tools.Select(t => t.Name).ToHashSet();

      // Worker mode should include internal tools
      Assert.Contains("worker_load_solution", toolNames);
      Assert.Contains("worker_health_check", toolNames);

      // Should also include regular worker tools
      Assert.Contains("find_symbols", toolNames);
      Assert.Contains("go_to_definition", toolNames);
      Assert.Contains("get_diagnostics", toolNames);
   }

   [Fact]
   public async Task Worker_ToolsList_IncludesAllExpectedWorkerTools()
   {
      await using var fixture = await DotLensMcpFixture.CreateAsync(workerMode: true);

      var tools = await fixture.Client.ListToolsAsync();
      var toolNames = tools.Select(t => t.Name).ToHashSet();

      // Should have a significant number of tools (we have ~47 public worker tools + 2 internal)
      // Note: Consolidated 4 code action tools into 2 unified tools (get_code_actions, apply_code_action)
      Assert.True(tools.Count >= 47, $"Expected >=47 tools, got {tools.Count}");

      // Spot check expected tools across different categories
      var expectedTools = new[]
      {
         // Navigation
         "go_to_definition", "find_references", "find_callers",
         // Search
         "find_symbols", "get_type_members", "get_file_outline",
         // Analysis
         "get_diagnostics", "get_complexity_metrics",
         // Refactoring
         "rename_symbol",
         // Internal (should be present in worker)
         "worker_load_solution", "worker_health_check"
      };

      foreach (var tool in expectedTools)
      {
         Assert.Contains(tool, toolNames);
      }
   }

   [Fact]
   public async Task Server_ToolsList_ExcludesInternalTools()
   {
      await using var fixture = await DotLensMcpFixture.CreateAsync(workerMode: false);

      var tools = await fixture.Client.ListToolsAsync();
      var toolNames = tools.Select(t => t.Name).ToHashSet();

      // Server mode should NOT include internal worker tools (they're for server-worker communication only)
      Assert.DoesNotContain("worker_load_solution", toolNames);
      Assert.DoesNotContain("worker_health_check", toolNames);
   }

   [Fact]
   public async Task Server_DumpToolSchemas()
   {
      await using var fixture = await DotLensMcpFixture.CreateAsync(workerMode: false);
      var tools = await fixture.Client.ListToolsAsync();

      var outputPath = Path.Combine(Path.GetTempPath(), "dotlens-schema-dump.txt");
      using var writer = new StreamWriter(outputPath);

      // Calculate totals
      var totalDescriptionChars = 0;
      var totalSchemaChars = 0;

      foreach (var tool in tools.OrderBy(t => t.Name))
      {
         var schemaJson = tool.JsonSchema.ToString();
         totalDescriptionChars += tool.Description.Length;
         totalSchemaChars += schemaJson.Length;

         writer.WriteLine($"=== {tool.Name} ===");
         writer.WriteLine($"Description ({tool.Description.Length} chars):");
         writer.WriteLine(tool.Description);
         writer.WriteLine($"Schema ({schemaJson.Length} chars):");
         writer.WriteLine(JsonSerializer.Serialize(
            JsonSerializer.Deserialize<JsonElement>(schemaJson),
            new JsonSerializerOptions { WriteIndented = true }));
         writer.WriteLine();
      }

      writer.WriteLine($"=== TOTALS ===");
      writer.WriteLine($"Tools: {tools.Count}");
      writer.WriteLine($"Total description chars: {totalDescriptionChars}");
      writer.WriteLine($"Total schema chars: {totalSchemaChars}");
      writer.WriteLine($"Total chars: {totalDescriptionChars + totalSchemaChars}");

      TestContext.Current.TestOutputHelper!.WriteLine($"Schema dump written to: {outputPath}");
   }

   [Fact]
   public async Task Server_ToolsList_IncludesServerAndForwardedTools()
   {
      await using var fixture = await DotLensMcpFixture.CreateAsync(workerMode: false);

      var tools = await fixture.Client.ListToolsAsync();
      var toolNames = tools.Select(t => t.Name).ToHashSet();

      // Should include server-side tools
      Assert.Contains("load_solution", toolNames);
      Assert.Contains("unload_solution", toolNames);
      Assert.Contains("health_check", toolNames);
      Assert.Contains("list_solutions", toolNames);

      // Should include forwarded worker tools (non-internal)
      Assert.Contains("find_symbols", toolNames);
      Assert.Contains("go_to_definition", toolNames);
      Assert.Contains("get_diagnostics", toolNames);
   }
}

/// <summary>
/// Custom exception for skipping tests when prerequisites are not met.
/// </summary>
public class SkipException : Exception
{
   public SkipException(string message) : base(message) { }
}
