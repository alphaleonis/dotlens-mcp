using System.Text.Json;
using Xunit;

namespace UnusedCodeTestProject;

internal sealed class QualifiedItem
{
   public QualifiedItem() => QualifiedInit();

   private static void QualifiedInit()
   {
   }
}

internal sealed class NullableItem
{
   public NullableItem() => NullableInit();

   private static void NullableInit()
   {
   }
}

internal sealed class ArrayItem
{
   public ArrayItem() => ArrayInit();

   private static void ArrayInit()
   {
   }
}

internal sealed class TupleItem
{
   public TupleItem() => TupleInit();

   private static void TupleInit()
   {
   }
}

internal sealed class RegisteredItem
{
   public RegisteredItem() => RegisteredInit();

   private static void RegisteredInit()
   {
   }
}

internal sealed class LazyItem
{
   public LazyItem() => LazyInit();

   private static void LazyInit()
   {
   }
}

public sealed class AttributedItem : TheoryData<int>
{
   internal AttributedItem() => AttributedInit();

   private static void AttributedInit()
   {
   }
}

internal sealed class DeclaredItem
{
   public DeclaredItem() => DeclaredInit();

   private static void DeclaredInit()
   {
   }
}

internal static class Registry<T>
   where T : new()
{
   public static T Instance { get; } = new();
}

internal static class HandoverShapeUses
{
   private static readonly Lazy<LazyItem> Cache = new();

   private static readonly List<DeclaredItem> Declared = [];

   public static void Run()
   {
      _ = JsonSerializer.Deserialize<System.Collections.Generic.List<QualifiedItem>>("[]");
      _ = JsonSerializer.Deserialize<List<NullableItem>?>("[]");
      _ = JsonSerializer.Deserialize<ArrayItem[]>("[]");
      _ = JsonSerializer.Deserialize<(TupleItem, int)>("[]");
      _ = Registry<RegisteredItem>.Instance;
      _ = Cache.Value;
      _ = Declared.Count;
   }
}

public class HandoverShapeTests
{
   [Theory]
   [ClassData<AttributedItem>]
   public void Reads(int value)
   {
   }
}
