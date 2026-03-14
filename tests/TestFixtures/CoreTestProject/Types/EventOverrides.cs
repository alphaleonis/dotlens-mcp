namespace CoreTestProject.Types;

/// <summary>
/// Interface with an event for testing interface implementation detection.
/// </summary>
public interface IEventSource
{
   event EventHandler? EventRaised;
}

/// <summary>
/// Base class with virtual events for testing override detection.
/// </summary>
public abstract class EventSourceBase
{
   public virtual event EventHandler? StateChanged;
   public abstract event EventHandler? ActionRequired;

   protected virtual void OnStateChanged()
   {
      StateChanged?.Invoke(this, EventArgs.Empty);
   }
}

/// <summary>
/// Derived class that overrides events and implements interface.
/// </summary>
public class ConcreteEventSource : EventSourceBase, IEventSource
{
   // Override of virtual event
   public override event EventHandler? StateChanged;

   // Override of abstract event
   public override event EventHandler? ActionRequired;

   // Implicit interface implementation
   public event EventHandler? EventRaised;

   protected override void OnStateChanged()
   {
      StateChanged?.Invoke(this, EventArgs.Empty);
   }
}
