namespace CoreTestProject.Patterns;

// ===========================================
// Events and delegates for testing
// ===========================================

/// <summary>
/// Class with events for testing event-related tools.
/// </summary>
public class EventPublisher
{
   // Standard event pattern
   public event EventHandler? StatusChanged;

   // Generic event with custom args
   public event EventHandler<DataEventArgs>? DataReceived;

   // Custom delegate event
   public event Action<string>? MessageSent;

   // Event with multiple handlers pattern
   public event EventHandler<ProgressEventArgs>? ProgressUpdated;

   private string _status = "Idle";

   public string Status
   {
      get => _status;
      set
      {
         if (_status != value)
         {
            _status = value;
            OnStatusChanged();
         }
      }
   }

   protected virtual void OnStatusChanged()
   {
      StatusChanged?.Invoke(this, EventArgs.Empty);
   }

   protected virtual void OnDataReceived(string data)
   {
      DataReceived?.Invoke(this, new DataEventArgs(data));
   }

   protected virtual void OnMessageSent(string message)
   {
      MessageSent?.Invoke(message);
   }

   protected virtual void OnProgressUpdated(int current, int total)
   {
      ProgressUpdated?.Invoke(this, new ProgressEventArgs(current, total));
   }

   public void ProcessData(string data)
   {
      OnDataReceived(data);
      OnMessageSent($"Processed: {data}");
   }

   public async Task ProcessWithProgressAsync(IEnumerable<string> items, CancellationToken cancellationToken = default)
   {
      var list = items.ToList();
      for (int i = 0; i < list.Count; i++)
      {
         cancellationToken.ThrowIfCancellationRequested();
         await Task.Delay(10, cancellationToken);
         OnProgressUpdated(i + 1, list.Count);
         OnDataReceived(list[i]);
      }
   }
}

public class DataEventArgs : EventArgs
{
   public string Data { get; }
   public DateTime Timestamp { get; }

   public DataEventArgs(string data)
   {
      Data = data;
      Timestamp = DateTime.UtcNow;
   }
}

public class ProgressEventArgs : EventArgs
{
   public int Current { get; }
   public int Total { get; }
   public double Percentage => Total > 0 ? (double)Current / Total * 100 : 0;

   public ProgressEventArgs(int current, int total)
   {
      Current = current;
      Total = total;
   }
}

/// <summary>
/// Class that subscribes to events.
/// </summary>
public class EventSubscriber
{
   private readonly EventPublisher _publisher;
   private readonly List<string> _receivedData = new();

   public IReadOnlyList<string> ReceivedData => _receivedData;
   public int StatusChangeCount { get; private set; }

   public EventSubscriber(EventPublisher publisher)
   {
      _publisher = publisher;

      // Subscribe to events
      _publisher.StatusChanged += OnStatusChanged;
      _publisher.DataReceived += OnDataReceived;
      _publisher.MessageSent += OnMessageSent;
   }

   private void OnStatusChanged(object? sender, EventArgs e)
   {
      StatusChangeCount++;
   }

   private void OnDataReceived(object? sender, DataEventArgs e)
   {
      _receivedData.Add(e.Data);
   }

   private void OnMessageSent(string message)
   {
      Console.WriteLine(message);
   }

   public void Unsubscribe()
   {
      _publisher.StatusChanged -= OnStatusChanged;
      _publisher.DataReceived -= OnDataReceived;
      _publisher.MessageSent -= OnMessageSent;
   }
}

// ===========================================
// Delegate patterns
// ===========================================

/// <summary>
/// Class demonstrating delegate patterns.
/// </summary>
public class DelegateExamples
{
   // Using built-in delegates
   public void ProcessWithAction(Action action)
   {
      action();
   }

   public void ProcessWithAction<T>(T item, Action<T> action)
   {
      action(item);
   }

   public TResult ProcessWithFunc<TResult>(Func<TResult> func)
   {
      return func();
   }

   public TResult ProcessWithFunc<T, TResult>(T item, Func<T, TResult> func)
   {
      return func(item);
   }

   public bool Filter<T>(T item, Predicate<T> predicate)
   {
      return predicate(item);
   }

   // Method that takes a comparison delegate
   public void SortItems<T>(List<T> items, Comparison<T> comparison)
   {
      items.Sort(comparison);
   }

   // Lambda expressions
   public IEnumerable<int> FilterNumbers(IEnumerable<int> numbers)
   {
      return numbers.Where(n => n > 0 && n % 2 == 0);
   }

   // Local function (not exactly a delegate, but related)
   public int ProcessWithLocalFunction(int[] numbers)
   {
      int Sum(int[] arr)
      {
         int total = 0;
         foreach (var n in arr)
            total += n;
         return total;
      }

      return Sum(numbers);
   }
}
