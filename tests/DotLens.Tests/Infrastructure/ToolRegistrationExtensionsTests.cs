using System.Reflection;
using DotLens.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Xunit;

namespace DotLens.Tests.Infrastructure;

public class ToolRegistrationExtensionsTests
{
   #region Worker Tool Attribute Tests

   [Fact]
   public void WorkerTools_AllHaveExpectedAttribute()
   {
      var workerToolTypes = GetWorkerToolTypes();

      Assert.NotEmpty(workerToolTypes);

      foreach (var type in workerToolTypes)
      {
         var attr = type.GetCustomAttribute<DotLensWorkerToolAttribute>();
         Assert.NotNull(attr);
      }
   }

   [Fact]
   public void WorkerTools_InternalToolsExist()
   {
      var internalTools = GetWorkerToolTypes()
         .Where(t => t.GetCustomAttribute<DotLensWorkerToolAttribute>()?.Internal == true)
         .ToList();

      // We should have at least the known internal tools
      Assert.NotEmpty(internalTools);
      Assert.Contains(internalTools, t => t.Name == "WorkerLoadSolutionTool");
      Assert.Contains(internalTools, t => t.Name == "WorkerHealthCheckTool");
   }

   [Fact]
   public void WorkerTools_PublicToolsExist()
   {
      var publicTools = GetWorkerToolTypes()
         .Where(t => t.GetCustomAttribute<DotLensWorkerToolAttribute>()?.Internal != true)
         .ToList();

      // Should have many public worker tools
      Assert.True(publicTools.Count >= 45, $"Expected >=45 public worker tools, got {publicTools.Count}");
   }

   #endregion

   #region Server Tool Attribute Tests

   [Fact]
   public void ServerTools_AllHaveExpectedAttribute()
   {
      var serverToolTypes = GetServerToolTypes();

      Assert.NotEmpty(serverToolTypes);

      foreach (var type in serverToolTypes)
      {
         var attr = type.GetCustomAttribute<DotLensServerToolAttribute>();
         Assert.NotNull(attr);
      }
   }

   [Fact]
   public void ServerTools_ExpectedToolsExist()
   {
      var serverToolTypes = GetServerToolTypes();

      // Verify expected server tools exist
      Assert.Contains(serverToolTypes, t => t.Name == "LoadSolutionTool");
      Assert.Contains(serverToolTypes, t => t.Name == "UnloadSolutionTool");
      Assert.Contains(serverToolTypes, t => t.Name == "ReloadSolutionTool");
      Assert.Contains(serverToolTypes, t => t.Name == "ListSolutionsTool");
      Assert.Contains(serverToolTypes, t => t.Name == "SetActiveSolutionTool");
      Assert.Contains(serverToolTypes, t => t.Name == "HealthCheckTool");
   }

   #endregion

   #region Tool Registration Tests

   [Fact]
   public void AddWorkerToolServices_RegistersAllWorkerToolTypes()
   {
      var services = new ServiceCollection();
      services.AddWorkerToolServices();

      var workerToolTypes = GetWorkerToolTypes();

      // Verify each tool type is registered in the service collection
      foreach (var toolType in workerToolTypes)
      {
         var descriptor = services.FirstOrDefault(d => d.ServiceType == toolType);
         Assert.NotNull(descriptor);
         Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
      }
   }

   [Fact]
   public void AddServerToolServices_RegistersAllServerToolTypes()
   {
      var services = new ServiceCollection();
      services.AddServerToolServices();

      var serverToolTypes = GetServerToolTypes();

      // Verify each tool type is registered in the service collection
      foreach (var toolType in serverToolTypes)
      {
         var descriptor = services.FirstOrDefault(d => d.ServiceType == toolType);
         Assert.NotNull(descriptor);
         Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
      }
   }

   [Fact]
   public void AddWorkerToolServices_RegistersExpectedCount()
   {
      var services = new ServiceCollection();
      services.AddWorkerToolServices();

      var workerToolTypes = GetWorkerToolTypes();

      // The number of registrations should match the number of worker tool types
      var registeredToolCount = services.Count(d => workerToolTypes.Contains(d.ServiceType));
      Assert.Equal(workerToolTypes.Length, registeredToolCount);
   }

   [Fact]
   public void AddServerToolServices_RegistersExpectedCount()
   {
      var services = new ServiceCollection();
      services.AddServerToolServices();

      var serverToolTypes = GetServerToolTypes();

      // The number of registrations should match the number of server tool types
      var registeredToolCount = services.Count(d => serverToolTypes.Contains(d.ServiceType));
      Assert.Equal(serverToolTypes.Length, registeredToolCount);
   }

   #endregion

   #region Internal Tool Filtering Tests

   [Fact]
   public void InternalToolFiltering_ExcludesInternalTrue()
   {
      var publicTools = GetWorkerToolTypes()
         .Where(t => t.GetCustomAttribute<DotLensWorkerToolAttribute>()?.Internal != true)
         .ToList();

      // Internal tools should NOT be in the public list
      Assert.DoesNotContain(publicTools, t => t.Name == "WorkerLoadSolutionTool");
      Assert.DoesNotContain(publicTools, t => t.Name == "WorkerHealthCheckTool");
   }

   [Fact]
   public void InternalToolFiltering_IncludesDefaultAttribute()
   {
      var publicTools = GetWorkerToolTypes()
         .Where(t => t.GetCustomAttribute<DotLensWorkerToolAttribute>()?.Internal != true)
         .ToList();

      // Tools with [DotLensWorkerTool] (no Internal flag) should be included
      // Check for known public tools
      Assert.Contains(publicTools, t => t.Name == "FindSymbolsTool");
      Assert.Contains(publicTools, t => t.Name == "GoToDefinitionTool");
      Assert.Contains(publicTools, t => t.Name == "GetDiagnosticsTool");
   }

   [Fact]
   public void InternalToolFiltering_InternalToolsAllStartWithWorker()
   {
      var internalTools = GetWorkerToolTypes()
         .Where(t => t.GetCustomAttribute<DotLensWorkerToolAttribute>()?.Internal == true)
         .ToList();

      // All internal tools should follow the naming convention
      foreach (var tool in internalTools)
      {
         Assert.StartsWith("Worker", tool.Name);
      }
   }

   [Fact]
   public void InternalToolFiltering_CountMatches()
   {
      var allWorkerTools = GetWorkerToolTypes();
      var publicWorkerTools = allWorkerTools
         .Where(t => t.GetCustomAttribute<DotLensWorkerToolAttribute>()?.Internal != true)
         .ToList();
      var internalWorkerTools = allWorkerTools
         .Where(t => t.GetCustomAttribute<DotLensWorkerToolAttribute>()?.Internal == true)
         .ToList();

      // Counts should add up
      Assert.Equal(allWorkerTools.Length, publicWorkerTools.Count + internalWorkerTools.Count);

      // We know exactly how many internal tools there are
      Assert.Equal(2, internalWorkerTools.Count);
   }

   #endregion

   #region McpServerTool Attribute Tests

   [Fact]
   public void WorkerTools_AllHaveMcpServerToolMethods()
   {
      var workerToolTypes = GetWorkerToolTypes();

      foreach (var toolType in workerToolTypes)
      {
         var methods = toolType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
         var toolMethods = methods.Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null).ToList();

         Assert.True(toolMethods.Count > 0, $"Tool {toolType.Name} has no methods with [McpServerTool] attribute");
      }
   }

   [Fact]
   public void ServerTools_AllHaveMcpServerToolMethods()
   {
      var serverToolTypes = GetServerToolTypes();

      foreach (var toolType in serverToolTypes)
      {
         var methods = toolType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
         var toolMethods = methods.Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null).ToList();

         Assert.True(toolMethods.Count > 0, $"Tool {toolType.Name} has no methods with [McpServerTool] attribute");
      }
   }

   [Fact]
   public void InternalTools_HaveWorkerPrefix()
   {
      var internalTools = GetWorkerToolTypes()
         .Where(t => t.GetCustomAttribute<DotLensWorkerToolAttribute>()?.Internal == true)
         .ToList();

      foreach (var toolType in internalTools)
      {
         var methods = toolType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
         var toolMethods = methods.Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null).ToList();

         foreach (var method in toolMethods)
         {
            var attr = method.GetCustomAttribute<McpServerToolAttribute>()!;
            Assert.StartsWith("worker_", attr.Name, StringComparison.OrdinalIgnoreCase);
         }
      }
   }

   #endregion

   #region Helper Methods

   private static Type[] GetWorkerToolTypes()
   {
      return Assembly.GetAssembly(typeof(DotLensWorkerToolAttribute))!
         .GetTypes()
         .Where(t => t.IsClass && !t.IsAbstract && t.GetCustomAttribute<DotLensWorkerToolAttribute>() != null)
         .ToArray();
   }

   private static Type[] GetServerToolTypes()
   {
      return Assembly.GetAssembly(typeof(DotLensServerToolAttribute))!
         .GetTypes()
         .Where(t => t.IsClass && !t.IsAbstract && t.GetCustomAttribute<DotLensServerToolAttribute>() != null)
         .ToArray();
   }

   #endregion
}
