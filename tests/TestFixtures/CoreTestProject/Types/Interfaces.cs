namespace CoreTestProject.Types;

// ===========================================
// Interfaces with various features
// ===========================================

/// <summary>
/// Basic interface with methods and properties.
/// </summary>
public interface IBasicService
{
   string Name { get; }

   void Execute();

   Task ExecuteAsync(CancellationToken cancellationToken = default);

   int Calculate(int x, int y);
}

/// <summary>
/// Interface with default implementations (C# 8+).
/// </summary>
public interface IServiceWithDefaults
{
   string Name { get; }

   // Default implementation
   void LogName()
   {
      Console.WriteLine($"Name: {Name}");
   }

   // Abstract method
   void Process();

   // Default implementation with virtual behavior
   int GetValue() => 42;
}

/// <summary>
/// Generic interface with constraints.
/// </summary>
public interface IRepository<T> where T : class
{
   T? GetById(int id);

   IEnumerable<T> GetAll();

   void Add(T entity);

   void Update(T entity);

   void Delete(int id);

   Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface extending other interfaces.
/// </summary>
public interface IAuditableRepository<T> : IRepository<T> where T : class
{
   DateTime? GetLastModified(int id);

   string? GetModifiedBy(int id);
}

/// <summary>
/// Interface with events.
/// </summary>
public interface INotifier
{
   event EventHandler<NotificationEventArgs>? NotificationSent;

   void SendNotification(string message);
}

public class NotificationEventArgs : EventArgs
{
   public string Message { get; }

   public NotificationEventArgs(string message)
   {
      Message = message;
   }
}

// ===========================================
// Interface implementations
// ===========================================

/// <summary>
/// Class implementing IBasicService with implicit implementation.
/// </summary>
public class BasicServiceImpl : IBasicService
{
   public string Name => "BasicService";

   public void Execute()
   {
      Console.WriteLine("Executing...");
   }

   public async Task ExecuteAsync(CancellationToken cancellationToken = default)
   {
      await Task.Delay(100, cancellationToken);
   }

   public int Calculate(int x, int y) => x + y;
}

/// <summary>
/// Class with explicit interface implementation.
/// </summary>
public class ExplicitImplementation : IBasicService, IServiceWithDefaults
{
   // Explicit implementation of IBasicService.Name
   string IBasicService.Name => "ExplicitBasic";

   // Explicit implementation of IServiceWithDefaults.Name
   string IServiceWithDefaults.Name => "ExplicitDefaults";

   // Explicit implementation
   void IBasicService.Execute()
   {
      Console.WriteLine("Explicit execute");
   }

   // Explicit implementation
   Task IBasicService.ExecuteAsync(CancellationToken cancellationToken)
   {
      return Task.CompletedTask;
   }

   // Explicit implementation
   int IBasicService.Calculate(int x, int y) => x * y;

   // Explicit implementation
   void IServiceWithDefaults.Process()
   {
      Console.WriteLine("Processing");
   }
}

/// <summary>
/// Generic repository implementation.
/// </summary>
public class InMemoryRepository<T> : IRepository<T> where T : class
{
   private readonly Dictionary<int, T> _storage = new();
   private int _nextId = 1;

   public T? GetById(int id)
   {
      return _storage.TryGetValue(id, out var entity) ? entity : null;
   }

   public IEnumerable<T> GetAll()
   {
      return _storage.Values;
   }

   public void Add(T entity)
   {
      _storage[_nextId++] = entity;
   }

   public void Update(T entity)
   {
      // Update logic would go here
   }

   public void Delete(int id)
   {
      _storage.Remove(id);
   }

   public Task<T?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
   {
      return Task.FromResult(GetById(id));
   }
}

/// <summary>
/// Class implementing interface with partial implementation (for testing missing members).
/// </summary>
public abstract class PartialImplementation : IBasicService
{
   public string Name => "Partial";

   public abstract void Execute();

   public virtual Task ExecuteAsync(CancellationToken cancellationToken = default)
   {
      return Task.CompletedTask;
   }

   // Calculate intentionally not implemented to test GetMissingMembers
   public abstract int Calculate(int x, int y);
}

/// <summary>
/// Concrete class with missing interface members for testing GetMissingMembersTool.
/// Implements only Name and Execute, leaving ExecuteAsync and Calculate missing.
/// </summary>
#pragma warning disable CS0535 // Does not implement interface member (intentional for testing)
public class IncompleteServiceImpl : IBasicService
{
   public string Name => "Incomplete";

   public void Execute()
   {
      Console.WriteLine("Executing...");
   }

   // Intentionally missing: ExecuteAsync and Calculate
}
#pragma warning restore CS0535
