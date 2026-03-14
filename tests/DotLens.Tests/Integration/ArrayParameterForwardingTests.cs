using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace DotLens.Tests.Integration;

/// <summary>
/// Tests for array parameter handling in forwarded worker tools.
/// Addresses the issue where array parameters cause "An error occurred invoking..." errors.
/// </summary>
public class ArrayParameterForwardingTests : IAsyncLifetime
{
   private const int TimeoutSeconds = 60; // Generous timeout for CI machines

   private string _executablePath = null!;
   private string _solutionPath = null!;

   public ValueTask InitializeAsync()
   {
      var projectDir = FindProjectDirectory();
      var config = "Debug";
#if RELEASE
      config = "Release";
#endif
      var binDir = Path.Combine(projectDir, "src", "DotLens", "bin", config, "net10.0");

      // On Windows the native host is DotLens.exe; on Linux it's DotLens (no extension).
      // Both may exist in a shared filesystem (e.g. WSL), so pick the right one for the current OS.
      var nativeHost = OperatingSystem.IsWindows() ? "DotLens.exe" : "DotLens";
      _executablePath = Path.Combine(binDir, nativeHost);

      if (!File.Exists(_executablePath))
      {
         _executablePath = Path.Combine(binDir, "DotLens.dll");
      }

      _solutionPath = Path.Combine(projectDir, "DotLens.slnx");

      return ValueTask.CompletedTask;
   }

   public ValueTask DisposeAsync() => ValueTask.CompletedTask;

   [Fact]
   public async Task SyncDocuments_WithArrayParameter_ShouldNotReturnGenericError()
   {
      if (!File.Exists(_executablePath))
      {
         throw new SkipException($"DotLens executable not found at {_executablePath}. Build the project first.");
      }

      using var process = StartMcpServer();

      try
      {
         await InitializeMcpSessionAsync(process);

         // Now call sync_documents with an array parameter
         var syncResult = await SendAndReceiveAsync(process, new
         {
            jsonrpc = "2.0",
            id = 3,
            method = "tools/call",
            @params = new
            {
               name = "sync_documents",
               arguments = new
               {
                  filePaths = new[] { Path.Combine(Path.GetDirectoryName(_solutionPath)!, "src", "DotLens", "Program.cs") }
               }
            }
         });

         Assert.NotNull(syncResult);

         // Check if we got the generic error
         if (syncResult.Value.TryGetProperty("error", out var error))
         {
            var errorMessage = error.TryGetProperty("message", out var msg) ? msg.GetString() : "unknown";
            Assert.Fail($"Got MCP protocol error: {errorMessage}. This suggests the array parameter forwarding failed.");
         }

         // Should have a result
         Assert.True(syncResult.Value.TryGetProperty("result", out var result),
            "sync_documents should return a result, not an error");

         // Content must exist and have entries
         Assert.True(result.TryGetProperty("content", out var content),
            "Result must have 'content' property");
         Assert.True(content.GetArrayLength() > 0,
            "Content array must not be empty");

         var text = content[0].GetProperty("text").GetString();
         Assert.NotNull(text);

         // Check it's not the generic error
         Assert.DoesNotContain("An error occurred invoking", text);

         // Parse the JSON response
         var responseDoc = JsonDocument.Parse(text);
         var responseRoot = responseDoc.RootElement;

         // If success property exists, it must be true
         if (responseRoot.TryGetProperty("success", out var success))
         {
            Assert.True(success.GetBoolean(), $"sync_documents failed: {text}");
         }

         // Must have data with totalSynced field (could be nested in 'data' or at root)
         JsonElement data;
         if (responseRoot.TryGetProperty("data", out var dataElement))
         {
            data = dataElement;
         }
         else
         {
            // Response might not have 'data' wrapper
            data = responseRoot;
         }

         Assert.True(data.TryGetProperty("totalSynced", out var totalSynced),
            $"Response must have 'totalSynced' field. Got: {text}");
         Assert.True(totalSynced.GetInt32() >= 1,
            "Should have synced at least 1 file (Program.cs)");
      }
      finally
      {
         CleanupProcess(process);
      }
   }

   [Fact]
   public async Task FindSymbols_WithArrayParameter_ShouldNotReturnGenericError()
   {
      if (!File.Exists(_executablePath))
      {
         throw new SkipException($"DotLens executable not found at {_executablePath}. Build the project first.");
      }

      using var process = StartMcpServer();

      try
      {
         await InitializeMcpSessionAsync(process);

         // Call find_symbols with attributes array parameter
         var findResult = await SendAndReceiveAsync(process, new
         {
            jsonrpc = "2.0",
            id = 3,
            method = "tools/call",
            @params = new
            {
               name = "find_symbols",
               arguments = new
               {
                  attributes = new[] { "McpServerToolType" },
                  maxResults = 5
               }
            }
         });

         Assert.NotNull(findResult);

         // Check for generic error
         if (findResult.Value.TryGetProperty("error", out var error))
         {
            var errorMessage = error.TryGetProperty("message", out var msg) ? msg.GetString() : "unknown";
            Assert.Fail($"Got MCP protocol error: {errorMessage}. Array parameter forwarding failed.");
         }

         Assert.True(findResult.Value.TryGetProperty("result", out var result),
            "find_symbols should return a result");

         // Content must exist and have entries
         Assert.True(result.TryGetProperty("content", out var content),
            "Result must have 'content' property");
         Assert.True(content.GetArrayLength() > 0,
            "Content array must not be empty");

         var text = content[0].GetProperty("text").GetString();
         Assert.NotNull(text);
         Assert.DoesNotContain("An error occurred invoking", text);
      }
      finally
      {
         CleanupProcess(process);
      }
   }

   /// <summary>
   /// Common MCP session initialization: initialize, send notification, load solution.
   /// </summary>
   private async Task InitializeMcpSessionAsync(Process process)
   {
      // Initialize
      var initResult = await SendAndReceiveAsync(process, new
      {
         jsonrpc = "2.0",
         id = 1,
         method = "initialize",
         @params = new
         {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test", version = "1.0" }
         }
      });
      Assert.NotNull(initResult);

      // Send initialized notification
      await SendNotificationAsync(process, new
      {
         jsonrpc = "2.0",
         method = "notifications/initialized"
      });

      // Load solution
      var loadResult = await SendAndReceiveAsync(process, new
      {
         jsonrpc = "2.0",
         id = 2,
         method = "tools/call",
         @params = new
         {
            name = "load_solution",
            arguments = new { solutionPath = _solutionPath }
         }
      });

      Assert.NotNull(loadResult);
      Assert.True(loadResult.Value.TryGetProperty("result", out _),
         "load_solution should return a result");
   }

   private Process StartMcpServer()
   {
      var process = new Process
      {
         StartInfo = new ProcessStartInfo
         {
            FileName = _executablePath.EndsWith(".dll") ? "dotnet" : _executablePath,
            Arguments = _executablePath.EndsWith(".dll") ? $"\"{_executablePath}\"" : "",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
         }
      };

      process.Start();
      return process;
   }

   private static void CleanupProcess(Process process)
   {
      try
      {
         process.Kill();
         // Wait briefly for process to exit to avoid orphans
         process.WaitForExit(1000);
      }
      catch (InvalidOperationException)
      {
         // Process already exited
      }
   }

   private static async Task<JsonElement?> SendAndReceiveAsync(Process process, object request)
   {
      var json = JsonSerializer.Serialize(request);
      await process.StandardInput.WriteLineAsync(json);
      await process.StandardInput.FlushAsync();

      using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
      var nonJsonLines = new StringBuilder();

      while (!cts.Token.IsCancellationRequested)
      {
         string? line;
         try
         {
            line = await process.StandardOutput.ReadLineAsync(cts.Token);
         }
         catch (OperationCanceledException)
         {
            throw new TimeoutException(
               $"Timeout waiting for MCP response after {TimeoutSeconds}s. " +
               $"Non-JSON output received: {nonJsonLines}");
         }

         if (string.IsNullOrEmpty(line))
            continue;

         try
         {
            var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("id", out _))
            {
               return doc.RootElement.Clone();
            }
         }
         catch (JsonException)
         {
            // Not valid JSON - accumulate for diagnostics
            nonJsonLines.AppendLine(line);
         }
      }

      throw new TimeoutException(
         $"Timeout waiting for MCP response after {TimeoutSeconds}s. " +
         $"Non-JSON output received: {nonJsonLines}");
   }

   private static async Task SendNotificationAsync(Process process, object notification)
   {
      var json = JsonSerializer.Serialize(notification);
      await process.StandardInput.WriteLineAsync(json);
      await process.StandardInput.FlushAsync();
   }

   private static string FindProjectDirectory()
   {
      var dir = Directory.GetCurrentDirectory();
      while (dir != null)
      {
         if (File.Exists(Path.Combine(dir, "DotLens.slnx")))
            return dir;
         dir = Directory.GetParent(dir)?.FullName;
      }
      throw new InvalidOperationException("Could not find project directory");
   }
}
