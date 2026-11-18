namespace ReferenceSearchTestProject;

[AttributeUsage(AttributeTargets.All)]
public sealed class TagAttribute : Attribute
{
   public int Level { get; set; }

   public string? Label;
}

public delegate ref int RefSource();

public sealed class Cells
{
   private readonly int[] _values = new int[8];
   private int _first;

   public ref int this[int index] => ref _values[index];

   public ref int First => ref _first;

   public ref readonly int FirstReadOnly => ref _first;

   public void Alias()
   {
      ref var first = ref _first;
      ref readonly var view = ref _first;
      first = view;
   }
}

[Tag(Level = 1, Label = "cells")]
public static class RefUsage
{
   public static ref int Pick(Cells cells, bool flag)
   {
      ref var writable = ref cells[0];
      ref readonly var readable = ref cells[1];
      writable = ref cells[2];
      readable = ref cells[3];
      ref var either = ref flag ? ref cells[4] : ref cells[5];
      either = readable;
      return ref cells[6];
   }

   public static ref readonly int Peek(Cells cells) => ref cells[7];

   public static RefSource Source(Cells cells) => () => ref cells[0];

   [Tag(Level = 2)]
   public static int Level(TagAttribute tag) => tag.Level;

   public static void Bump(Cells cells, bool flag) => Increment(ref flag ? ref cells[0] : ref cells[1]);

   private static void Increment(ref int value) => value++;
}

public sealed unsafe class Arguments
{
   private int _slot;
   private int _other;

   public delegate void Writer(ref int value);

   public delegate void Reader(ref readonly int value);

   public int this[ref readonly int index] => index;

   public void Pass(bool flag, Writer writer, Reader reader, delegate*<ref int, void> writePointer, delegate*<ref readonly int, void> readPointer)
   {
      Change(ref _slot);
      Look(ref _slot);
      Look(in _slot);
      Look(ref flag ? ref _slot : ref _other);
      Change(ref flag ? ref _slot : ref _other);
      Peek(ref _slot);
      writer(ref _slot);
      reader(ref _slot);
      writePointer(ref _slot);
      readPointer(ref _slot);
      _ = this[ref _slot];
      Fill(out _slot);
   }

   private static void Change(ref int value) => value++;

   private static void Look(ref readonly int value) => _ = value;

   private static void Peek(in int value) => _ = value;

   private static void Fill(out int value) => value = 0;
}
