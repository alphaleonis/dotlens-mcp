namespace CoreTestProject.Patterns;

// ===========================================
// Unused code patterns for FindUnusedCode tests
// ===========================================
// This file contains INTENTIONALLY unused symbols for testing.
// Tests should verify EXACT matches - all unused symbols detected,
// no false positives (used symbols reported as unused).
//
// EXPECTED UNUSED SYMBOLS BY KIND:
// --------------------------------
// Classes: UnusedInternalClass, UnusedPrivateNestedClass
// Structs: UnusedStruct
// Records: UnusedRecord
// Interfaces: IUnusedInterface
// Enums: UnusedEnum
// Delegates: UnusedDelegate
// Methods: UnusedPrivateMethod, UnusedCalculation
// Properties: UnusedProperty
// Fields: _unusedField, _unusedReadonlyField
// Events: UnusedEvent
// ===========================================

#region Unused Types

/// <summary>
/// UNUSED: Class not referenced anywhere.
/// </summary>
internal class UnusedInternalClass
{
   public void DoSomething()
   {
      Console.WriteLine("This class is never instantiated");
   }
}

/// <summary>
/// UNUSED: Struct not referenced anywhere.
/// </summary>
public struct UnusedStruct
{
   public int Value { get; set; }
   public void Process() { }
}

/// <summary>
/// UNUSED: Record not referenced anywhere.
/// </summary>
public record UnusedRecord(string Name, int Value);

/// <summary>
/// UNUSED: Interface not implemented anywhere.
/// </summary>
public interface IUnusedInterface
{
   void UnusedInterfaceMethod();
   string UnusedInterfaceProperty { get; }
}

/// <summary>
/// UNUSED: Enum not referenced anywhere.
/// </summary>
public enum UnusedEnum
{
   Value1,
   Value2,
   Value3
}

/// <summary>
/// UNUSED: Delegate not referenced anywhere.
/// </summary>
public delegate void UnusedDelegate(string message);

#endregion

#region Used Types (for comparison - should NOT appear in unused results)

/// <summary>
/// USED: This class is instantiated by UsedTypesConsumer.
/// </summary>
public class UsedClass
{
   public void DoWork() => Console.WriteLine("Working");
}

/// <summary>
/// USED: This struct is used by UsedTypesConsumer.
/// </summary>
public struct UsedStruct
{
   public int Value { get; set; }
}

/// <summary>
/// USED: This record is used by UsedTypesConsumer.
/// </summary>
public record UsedRecord(string Name);

/// <summary>
/// USED: This interface is implemented by UsedTypesConsumer.
/// </summary>
public interface IUsedInterface
{
   void UsedMethod();
}

/// <summary>
/// USED: This enum is referenced by UsedTypesConsumer.
/// </summary>
public enum UsedEnum
{
   Active,
   Inactive
}

/// <summary>
/// USED: This delegate is referenced by UsedTypesConsumer.
/// </summary>
public delegate void UsedDelegate(int value);

/// <summary>
/// Class that uses the "Used" types to ensure they're not reported as unused.
/// </summary>
public class UsedTypesConsumer : IUsedInterface
{
   private readonly UsedClass _usedClass = new();
   private UsedStruct _usedStruct;
   private UsedRecord? _usedRecord;
   private UsedEnum _status;
   private UsedDelegate? _callback;

   public void UsedMethod()
   {
      _usedClass.DoWork();
      _usedStruct.Value = 42;
      _usedRecord = new UsedRecord("test");
      _status = UsedEnum.Active;
      _callback?.Invoke(1);
      Console.WriteLine($"{_usedRecord} {_status}");
   }
}

/// <summary>
/// Class that ensures UsedTypesConsumer and MemberConsumer are actually used (not reported as unused).
/// </summary>
public class TypeConsumerUser
{
   public void UseConsumers()
   {
      var typeConsumer = new UsedTypesConsumer();
      typeConsumer.UsedMethod();

      var memberConsumer = new MemberConsumer();
      memberConsumer.Consume();
   }
}

#endregion

#region Class with Mixed Used/Unused Members

/// <summary>
/// Class with intentionally unused members for testing.
/// </summary>
public class UnusedCodeExamples
{
   // UNUSED: private field never read or written
   private int _unusedField;

   // UNUSED: private readonly field never read
   private readonly string _unusedReadonlyField = "never used";

   // USED: field is accessed in UsedMethod
   private int _usedField;

   // UNUSED: private property never accessed
   private string UnusedProperty { get; set; } = "";

   // USED: property is accessed in UsedMethod
   public string UsedProperty { get; set; } = "";

   // UNUSED: private method never called
   private void UnusedPrivateMethod()
   {
      Console.WriteLine("This method is never called");
   }

   // UNUSED: private method never called
   private int UnusedCalculation(int a, int b)
   {
      return a + b;
   }

   // USED: method is called externally
   public void UsedMethod()
   {
      _usedField = 42;
      Console.WriteLine(UsedProperty);
   }

   // UNUSED: event never subscribed to or raised
   public event EventHandler? UnusedEvent;

   // USED: event is subscribed to by EventConsumer
   public event EventHandler? UsedEvent;

   public void RaiseUsedEvent() => UsedEvent?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Class that uses members from UnusedCodeExamples.
/// </summary>
public class MemberConsumer
{
   public void Consume()
   {
      var examples = new UnusedCodeExamples();
      examples.UsedMethod();
      examples.UsedProperty = "test";
      examples.UsedEvent += (s, e) => Console.WriteLine("Event fired");
      examples.RaiseUsedEvent();
   }
}

#endregion

#region Nested Class (Unused)

/// <summary>
/// Class containing an unused nested class.
/// </summary>
public class ContainerWithNestedUnused
{
   // UNUSED: nested class never instantiated
   private class UnusedPrivateNestedClass
   {
      public void NestedMethod() { }
   }

   // USED: nested class is instantiated
   private class UsedPrivateNestedClass
   {
      public void NestedMethod() { }
   }

   public void UseNested()
   {
      var nested = new UsedPrivateNestedClass();
      nested.NestedMethod();
   }
}

#endregion

#region Virtual/Override Members (excluded from unused detection)

/// <summary>
/// Base class with virtual members.
/// Virtual/override methods are excluded from unused detection by design.
/// </summary>
public class BaseWithVirtual
{
   protected virtual void VirtualMethod()
   {
      Console.WriteLine("Base virtual");
   }

   public virtual void UsedVirtualMethod()
   {
      Console.WriteLine("Base used");
   }
}

/// <summary>
/// Derived class demonstrating that override methods are excluded.
/// </summary>
public class DerivedWithOverride : BaseWithVirtual
{
   // Excluded from unused detection (is override)
   protected override void VirtualMethod()
   {
      Console.WriteLine("Override");
   }

   // Excluded from unused detection (is override)
   public override void UsedVirtualMethod()
   {
      Console.WriteLine("Override used");
   }
}

/// <summary>
/// Consumer that calls the virtual method.
/// </summary>
public class VirtualConsumer
{
   public void Consume()
   {
      BaseWithVirtual b = new DerivedWithOverride();
      b.UsedVirtualMethod();
   }
}

#endregion

#region Unused Constructor Tests

/// <summary>
/// Class with an unused constructor for testing SymbolKindFilter.Constructor.
/// </summary>
public class ClassWithUnusedConstructor
{
   // UNUSED: This parameterized constructor is never called
   public ClassWithUnusedConstructor(string unusedParam)
   {
      Console.WriteLine(unusedParam);
   }

   // USED: Default constructor is called by consumer
   public ClassWithUnusedConstructor()
   {
   }

   public void DoWork() => Console.WriteLine("Working");
}

/// <summary>
/// Class that uses ClassWithUnusedConstructor via default constructor only.
/// </summary>
public class ConstructorConsumer
{
   public void Consume()
   {
      var obj = new ClassWithUnusedConstructor(); // Uses default ctor, not the parameterized one
      obj.DoWork();
   }
}

#endregion

#region Static Constructor Tests

/// <summary>
/// Class with an unused static constructor for testing.
/// </summary>
public class ClassWithUnusedStaticConstructor
{
   // UNUSED: Static constructor is never invoked (class is never used)
   static ClassWithUnusedStaticConstructor()
   {
      Console.WriteLine("Static initialization");
   }

   public void DoWork() => Console.WriteLine("Working");
}

/// <summary>
/// Class with a used static constructor (the class is instantiated, triggering the static ctor).
/// </summary>
public class ClassWithUsedStaticConstructor
{
   // USED: Static constructor runs when class is first accessed
   static ClassWithUsedStaticConstructor()
   {
      Console.WriteLine("Static initialization");
   }

   public void DoWork() => Console.WriteLine("Working");
}

/// <summary>
/// Consumer that triggers ClassWithUsedStaticConstructor's static constructor.
/// </summary>
public class StaticConstructorConsumer
{
   public void Consume()
   {
      var obj = new ClassWithUsedStaticConstructor();
      obj.DoWork();
   }
}

#endregion

#region False Positive Regression Test (bug 5qzu)

/// <summary>
/// Helper class with a method that IS used (called once from another class).
/// This should NOT be reported as unused code.
/// Regression test for bug 5qzu where methods called exactly once were incorrectly reported.
/// </summary>
public static class HelperWithUsedMethod
{
   /// <summary>
   /// USED: Called exactly once by CallerOfHelperMethod.ValidateUser.
   /// Should NOT appear in find_unused_code results.
   /// </summary>
   public static bool IsValidName(string name)
   {
      return !string.IsNullOrWhiteSpace(name) && name.Length <= 100;
   }
}

/// <summary>
/// Class that calls HelperWithUsedMethod.IsValidName once.
/// </summary>
public class CallerOfHelperMethod
{
   public bool ValidateUser(string userName)
   {
      return HelperWithUsedMethod.IsValidName(userName);
   }
}

#endregion

#region Interface Implementation (partial use)

/// <summary>
/// Interface where some members are used and some are not.
/// Note: Interface members themselves are not directly detected as unused;
/// the implementations are what get detected.
/// </summary>
public interface IPartiallyUsed
{
   void UsedInterfaceMethod();
   void UnusedInterfaceMethod();
}

/// <summary>
/// Implementation where one method is called and one is not.
/// Note: Interface implementations are not reported as unused because they
/// satisfy a contract. Only truly private unreferenced members are reported.
/// </summary>
public class PartiallyUsedImplementation : IPartiallyUsed
{
   public void UsedInterfaceMethod()
   {
      Console.WriteLine("This is called");
   }

   public void UnusedInterfaceMethod()
   {
      Console.WriteLine("This is implemented but never called");
   }
}

/// <summary>
/// Consumer that uses only some interface methods.
/// </summary>
public class InterfaceConsumer
{
   public void Consume(IPartiallyUsed impl)
   {
      impl.UsedInterfaceMethod();
   }
}

#endregion
