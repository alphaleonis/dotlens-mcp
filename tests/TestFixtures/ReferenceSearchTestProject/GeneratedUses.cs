using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace ReferenceSearchTestProject;

public readonly record struct Handle(nint Value);

[CustomMarshaller(typeof(Handle), MarshalMode.Default, typeof(HandleMarshaller))]
public static class HandleMarshaller
{
   public static nint ConvertToUnmanaged(Handle managed) => managed.Value;

   public static Handle ConvertToManaged(nint unmanaged) => new(unmanaged);
}

public static partial class NativeMethods
{
   [LibraryImport("native")]
   internal static partial void Close([MarshalUsing(typeof(HandleMarshaller))] Handle handle);

   public static nint Raw(Handle handle) => HandleMarshaller.ConvertToUnmanaged(handle);
}
