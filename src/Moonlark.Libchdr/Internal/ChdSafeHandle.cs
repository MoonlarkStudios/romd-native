using System.Runtime.InteropServices;
using Moonlark.Libchdr.Interop;

namespace Moonlark.Libchdr.Internal;

/// <summary>Adopts a successful native open and releases its callback context after native close returns.</summary>
internal sealed unsafe class ChdSafeHandle : SafeHandle
{
    internal ChdSafeHandle(chd_file* file, ChdSourceContext context) : base(nint.Zero, ownsHandle: true)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (file == null) throw new ArgumentException("A CHD handle must own a successful native open.", nameof(file));
        Context = context;
        SetHandle((nint)file);
    }
    internal ChdSourceContext Context { get; }
    public override bool IsInvalid => handle == nint.Zero;

    protected override bool ReleaseHandle()
    {
        try { NativeMethods.chd_close((chd_file*)handle); return true; }
        catch (Exception exception) { Context.CaptureFault(exception); return false; }
        finally { Context.Dispose(); }
    }
}
