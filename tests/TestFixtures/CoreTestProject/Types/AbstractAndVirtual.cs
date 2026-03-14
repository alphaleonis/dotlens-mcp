namespace CoreTestProject.Types;

// ===========================================
// Abstract classes and virtual members
// ===========================================

/// <summary>
/// Abstract base class with various member types.
/// </summary>
public abstract class AbstractBase
{
   // Abstract method - must be implemented
   public abstract void AbstractMethod();

   // Abstract property - must be implemented
   public abstract string AbstractProperty { get; }

   // Virtual method - can be overridden
   public virtual void VirtualMethod()
   {
      Console.WriteLine("Base implementation");
   }

   // Virtual property - can be overridden
   public virtual int VirtualProperty => 0;

   // Non-virtual method - cannot be overridden
   public void NonVirtualMethod()
   {
      Console.WriteLine("Cannot override");
   }

   // Protected virtual method
   protected virtual void ProtectedVirtualMethod() { }
}

/// <summary>
/// Concrete implementation of AbstractBase.
/// </summary>
public class ConcreteClass : AbstractBase
{
   private string _abstractProperty = "implemented";

   public override void AbstractMethod()
   {
      Console.WriteLine("Concrete implementation");
   }

   public override string AbstractProperty => _abstractProperty;

   public override void VirtualMethod()
   {
      base.VirtualMethod();
      Console.WriteLine("Extended implementation");
   }

   public override int VirtualProperty => 42;

   protected override void ProtectedVirtualMethod()
   {
      base.ProtectedVirtualMethod();
   }
}

/// <summary>
/// Derived class that further extends ConcreteClass.
/// </summary>
public class DerivedClass : ConcreteClass
{
   public override void VirtualMethod()
   {
      Console.WriteLine("Derived implementation");
   }

   public override int VirtualProperty => 100;

   // New method that hides base - for testing shadowing
   public new void NonVirtualMethod()
   {
      Console.WriteLine("Hidden method");
   }
}

/// <summary>
/// Class with sealed override - cannot be further overridden.
/// </summary>
public class SealedOverrideClass : ConcreteClass
{
   public sealed override void AbstractMethod()
   {
      Console.WriteLine("Sealed override");
   }

   public sealed override int VirtualProperty => 999;
}
