using System.Text.Json;
using DotLens.Infrastructure;
using DotLens.Infrastructure.Results;
using DotLens.Tools.Server;
using FakeItEasy;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DotLens.Tests;

public class DiagnosticSerializationTest
{
   [Fact]
   public void ToolResult_Serializes_AllProperties()
   {
      var result = ToolResult.Create(new { status = "Ready", count = 5 });

      // Serialize with camelCase
      var jsonCamelCase = JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase);

      Assert.Contains("status", jsonCamelCase);
      Assert.Contains("Ready", jsonCamelCase);
   }

   [Fact]
   public void HealthCheckResult_Serializes_Correctly()
   {
      var result = ToolResult.Create(new HealthCheckResponse
      {
         Status = "Ready",
         TotalSolutions = 1,
         ActiveSolution = "test.sln",
         Solutions = new List<SolutionSummary>
         {
            new SolutionSummary { Path = "test.sln", IsActive = true, LoadedAt = DateTime.UtcNow }
         },
         ActiveWorkerHealth = new { status = "Ready", projectCount = 5 }
      });

      var json = JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase);

      // This should contain "Ready" from Status property and activeWorkerHealth
      Assert.Contains("status", json);  // From HealthCheckResult.Status
      Assert.Contains("Ready", json);   // The value "Ready"
      Assert.Contains("totalSolutions", json);
   }

   [Fact]
   public async Task HealthCheckTool_WithMockedManager_SerializesCorrectly()
   {
      // Replicate the exact mock setup from the failing test
      var workerManager = A.Fake<IMultiWorkerManager>();
      var workers = new Dictionary<string, WorkerInstance>
      {
         [@"C:\Test\Solution.sln"] = new WorkerInstance
         {
            SolutionPath = @"C:\Test\Solution.sln",
            Client = null!,
            LoadedAt = DateTime.UtcNow
         }
      };
      A.CallTo(() => workerManager.Workers).Returns(workers);
      A.CallTo(() => workerManager.ActiveSolutionPath).Returns(@"C:\Test\Solution.sln");
      A.CallTo(() => workerManager.CallToolAsync<object>("worker_health_check", A<Dictionary<string, object?>>._, A<CancellationToken>._))
         .Returns(new { status = "Ready", projectCount = 5 });

      var tool = new HealthCheckTool(workerManager, NullLogger<HealthCheckTool>.Instance);

      var result = await tool.HealthCheckAsync(CancellationToken.None);

      // Polymorphism issue: result is ToolResult, not ToolResult<T>
      // Let's try serializing with the actual runtime type
      var jsonAsToolResult = JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase);
      var jsonAsActualType = JsonSerializer.Serialize(result, result.GetType(), TestJsonOptions.CamelCase);

      Assert.True(jsonAsActualType.Length > 20, $"JSON with actual type too short: {jsonAsActualType}");
      Assert.Contains("Ready", jsonAsActualType);
   }
}
