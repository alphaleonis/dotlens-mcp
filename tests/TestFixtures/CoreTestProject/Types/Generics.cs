namespace CoreTestProject.Types;

// ===========================================
// Generic classes with various constraints
// ===========================================

/// <summary>
/// Generic class with no constraints.
/// </summary>
public class Container<T>
{
   public T? Value { get; set; }

   public Container() { }

   public Container(T value)
   {
      Value = value;
   }

   public bool HasValue => Value is not null;
}

/// <summary>
/// Generic class with class constraint.
/// </summary>
public class ReferenceContainer<T> where T : class
{
   public T? Value { get; set; }

   public T GetValueOrThrow()
   {
      return Value ?? throw new InvalidOperationException("No value");
   }
}

/// <summary>
/// Generic class with struct constraint.
/// </summary>
public class ValueContainer<T> where T : struct
{
   public T Value { get; set; }

   public T? NullableValue { get; set; }
}

/// <summary>
/// Generic class with new() constraint.
/// </summary>
public class Factory<T> where T : new()
{
   public T Create()
   {
      return new T();
   }

   public T CreateAndConfigure(Action<T> configure)
   {
      var instance = new T();
      configure(instance);
      return instance;
   }
}

/// <summary>
/// Generic class with interface constraint.
/// </summary>
public class ServiceWrapper<T> where T : IBasicService
{
   private readonly T _service;

   public ServiceWrapper(T service)
   {
      _service = service;
   }

   public void ExecuteService()
   {
      _service.Execute();
   }

   public async Task ExecuteServiceAsync(CancellationToken cancellationToken = default)
   {
      await _service.ExecuteAsync(cancellationToken);
   }
}

/// <summary>
/// Generic class with base class constraint.
/// </summary>
public class AbstractWrapper<T> where T : AbstractBase
{
   private readonly T _instance;

   public AbstractWrapper(T instance)
   {
      _instance = instance;
   }

   public void CallAbstractMethod()
   {
      _instance.AbstractMethod();
   }

   public string GetAbstractProperty()
   {
      return _instance.AbstractProperty;
   }
}

/// <summary>
/// Generic class with multiple constraints.
/// </summary>
public class ComplexGeneric<TKey, TValue>
   where TKey : notnull, IComparable<TKey>
   where TValue : class, new()
{
   private readonly Dictionary<TKey, TValue> _items = new();

   public void Add(TKey key, TValue value)
   {
      _items[key] = value;
   }

   public TValue GetOrCreate(TKey key)
   {
      if (!_items.TryGetValue(key, out var value))
      {
         value = new TValue();
         _items[key] = value;
      }
      return value;
   }

   public IEnumerable<TKey> GetSortedKeys()
   {
      return _items.Keys.Order();
   }
}

/// <summary>
/// Generic class with unmanaged constraint.
/// </summary>
public class UnmanagedContainer<T> where T : unmanaged
{
   private T _value;

   public ref T GetReference() => ref _value;

   public int Size => System.Runtime.InteropServices.Marshal.SizeOf<T>();
}

// ===========================================
// Generic methods
// ===========================================

/// <summary>
/// Class with generic methods.
/// </summary>
public class GenericMethods
{
   public T Identity<T>(T value)
   {
      return value;
   }

   public TOutput Convert<TInput, TOutput>(TInput input, Func<TInput, TOutput> converter)
   {
      return converter(input);
   }

   public T CreateInstance<T>() where T : new()
   {
      return new T();
   }

   public void ProcessItems<T>(IEnumerable<T> items, Action<T> processor)
   {
      foreach (var item in items)
      {
         processor(item);
      }
   }

   public IEnumerable<TResult> Map<TSource, TResult>(
      IEnumerable<TSource> source,
      Func<TSource, TResult> selector)
   {
      foreach (var item in source)
      {
         yield return selector(item);
      }
   }
}
