namespace CoreTestProject.Patterns;

// ===========================================
// Async/Await patterns for testing
// ===========================================

/// <summary>
/// Class with various async patterns.
/// </summary>
public class AsyncService
{
   private readonly HttpClient _httpClient = new();

   // Simple async method
   public async Task DoWorkAsync()
   {
      await Task.Delay(100);
   }

   // Async method with return value
   public async Task<int> CalculateAsync(int value)
   {
      await Task.Delay(10);
      return value * 2;
   }

   // Async method with cancellation token
   public async Task<string> FetchDataAsync(string url, CancellationToken cancellationToken = default)
   {
      await Task.Delay(100, cancellationToken);
      return $"Data from {url}";
   }

   // Async method with multiple awaits
   public async Task<int> ProcessSequentialAsync(int input)
   {
      var step1 = await CalculateAsync(input);
      var step2 = await CalculateAsync(step1);
      var step3 = await CalculateAsync(step2);
      return step3;
   }

   // ValueTask for performance-critical paths
   public ValueTask<int> GetCachedValueAsync(int key)
   {
      // Simulating cache hit returning synchronously
      if (key > 0)
      {
         return ValueTask.FromResult(key * 10);
      }

      // Cache miss - need async work
      return new ValueTask<int>(FetchValueAsync(key));
   }

   private async Task<int> FetchValueAsync(int key)
   {
      await Task.Delay(50);
      return key * 10;
   }

   // Async enumerable (IAsyncEnumerable)
   public async IAsyncEnumerable<int> GenerateNumbersAsync(
      int count,
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
   {
      for (int i = 0; i < count; i++)
      {
         cancellationToken.ThrowIfCancellationRequested();
         await Task.Delay(10, cancellationToken);
         yield return i;
      }
   }

   // Parallel async execution
   public async Task<int[]> ProcessParallelAsync(int[] inputs)
   {
      var tasks = inputs.Select(async input =>
      {
         await Task.Delay(10);
         return input * 2;
      });

      return await Task.WhenAll(tasks);
   }

   // Async method with try/catch
   public async Task<string?> SafeFetchAsync(string url)
   {
      try
      {
         return await FetchDataAsync(url);
      }
      catch (OperationCanceledException)
      {
         return null;
      }
      catch (Exception ex)
      {
         return $"Error: {ex.Message}";
      }
   }

   // Async method with using
   public async Task<string> ProcessWithResourceAsync()
   {
      await using var stream = new MemoryStream();
      await stream.WriteAsync(new byte[] { 1, 2, 3 });
      stream.Position = 0;

      using var reader = new StreamReader(stream);
      return await reader.ReadToEndAsync();
   }

   // Async method with ConfigureAwait (for testing CA2007)
   public async Task<string> FetchWithConfigureAwaitAsync(string url)
   {
      var result = await FetchDataAsync(url).ConfigureAwait(false);
      return result.ToUpper();
   }
}

/// <summary>
/// Interface for async factory pattern.
/// </summary>
public interface IAsyncFactory<T>
{
   Task<T> CreateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Async factory implementation.
/// </summary>
public class AsyncWidgetFactory : IAsyncFactory<Widget>
{
   public async Task<Widget> CreateAsync(CancellationToken cancellationToken = default)
   {
      await Task.Delay(50, cancellationToken);
      return new Widget { Id = Random.Shared.Next() };
   }
}

public class Widget
{
   public int Id { get; set; }
   public string Name { get; set; } = "";
}
