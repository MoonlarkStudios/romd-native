using System.Runtime.InteropServices;

namespace Moonlark.Libchdr.Internal;

internal sealed class NativeLibraryHandle : SafeHandle
{
    internal NativeLibraryHandle(nint value) : base(0, true) => SetHandle(value);
    public override bool IsInvalid => handle == 0;
    protected override bool ReleaseHandle()
    {
        NativeLibrary.Free(handle);
        return true;
    }
}
