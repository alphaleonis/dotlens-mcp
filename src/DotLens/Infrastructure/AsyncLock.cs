namespace DotLens.Infrastructure;

/// <summary>
/// Async-compatible mutual exclusion lock backed by <see cref="SemaphoreSlim"/>.
/// Use with <c>using</c> for automatic release:
/// <code>
/// using (await _lock.AcquireAsync(cancellationToken))
/// {
///    // exclusive access
/// }
/// </code>
/// </summary>
public sealed class AsyncLock : IDisposable
{
   private readonly SemaphoreSlim _semaphore = new(1, 1);

   /// <summary>
   /// Returns true if the lock is currently held (semaphore count is 0).
   /// For debug assertions only — not a synchronization primitive.
   /// </summary>
   internal bool IsHeld => _semaphore.CurrentCount == 0;

   public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
   {
      await _semaphore.WaitAsync(cancellationToken);
      return new Releaser(_semaphore);
   }

   public void Dispose() => _semaphore.Dispose();

   private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
   {
      private int _disposed;

      public void Dispose()
      {
         if (Interlocked.Exchange(ref _disposed, 1) == 0)
            semaphore.Release();
      }
   }
}
