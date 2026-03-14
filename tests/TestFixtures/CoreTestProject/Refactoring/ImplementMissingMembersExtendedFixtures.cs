namespace CoreTestProject.Refactoring;

// NOTE: This file intentionally has compilation errors (CS0535, CS0534) for testing purposes.
// It is used by ImplementMissingMembersTool tests for extended scenarios.
// DO NOT MODIFY - line numbers are referenced in tests.

#region Abstract Class Fixtures

/// <summary>
/// Abstract base class for testing abstract member implementation.
/// </summary>
public abstract class AbstractTestBase
{
   public string Name { get; } = "Base";

   public abstract void ProcessData();
   public abstract int Calculate(int value);
   public abstract string Status { get; }
}

/// <summary>
/// Class with missing abstract members for testing.
/// </summary>
public class IncompleteAbstractClass : AbstractTestBase
{
   // Missing: ProcessData(), Calculate(int), Status property
}

#endregion

#region Struct Fixtures

/// <summary>
/// Interface for struct implementation testing.
/// </summary>
public interface IStructService
{
   void Initialize();
   bool IsReady { get; }
}

/// <summary>
/// Struct with missing interface members for testing.
/// </summary>
public struct IncompleteStruct : IStructService
{
   // Missing: Initialize(), IsReady property
}

#endregion

#region Property/Event Fixtures

/// <summary>
/// Interface with various member types for comprehensive testing.
/// </summary>
public interface IComplexService
{
   // Properties
   string Id { get; }
   string Description { get; set; }

   // Events
   event EventHandler? StatusChanged;
   event EventHandler<string>? MessageReceived;

   // Indexer
   string this[int index] { get; }

   // Methods
   void Start();
}

/// <summary>
/// Class with missing property, event, and indexer members.
/// </summary>
public class IncompleteComplexClass : IComplexService
{
   // Only implements Start() - missing all properties, events, and indexer
   public void Start() { }
}

#endregion

#region Multiple Interface Fixtures

/// <summary>
/// First interface for multiple interface testing.
/// </summary>
public interface IFirstInterface
{
   void FirstMethod();
   string FirstProperty { get; }
}

/// <summary>
/// Second interface for multiple interface testing.
/// </summary>
public interface ISecondInterface
{
   void SecondMethod();
   int SecondProperty { get; }
}

/// <summary>
/// Class implementing multiple interfaces with missing members from both.
/// </summary>
public class MultiInterfaceClass : IFirstInterface, ISecondInterface
{
   // Missing: FirstMethod(), FirstProperty, SecondMethod(), SecondProperty
}

#endregion
