using System.Reflection;
using System.Runtime.InteropServices;
using Moonlark.Libchdr.Internal;
using Moonlark.Libchdr.Interop;
using Xunit;

namespace Moonlark.Libchdr.Tests;

/// <summary>
/// The raw ClangSharp output is committed verbatim, so its structural guarantees are checked on the compiled
/// assembly. These tests never load the native library.
/// </summary>
public sealed class InteropBoundaryTests
{
    private const string InteropNamespace = "Moonlark.Libchdr.Interop";
    private const string NativeLibraryName = "moonlark_chdr";
    private const BindingFlags Declared = BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Assembly Library = typeof(NativeMethods).Assembly;

    /// <summary>Every raw type, including nested fixed buffers and generated helpers, is invisible outside the assembly.</summary>
    [Fact]
    public void EveryInteropTypeIsNonPublic()
    {
        Type[] types = Library.GetTypes().Where(type => type.Namespace == InteropNamespace).ToArray();
        Assert.Contains(typeof(NativeMethods), types);
        Assert.All(types, type => Assert.False(type.IsVisible, type.FullName));
        Assert.All(types.Where(type => !type.IsNested), type => Assert.True(type.IsNotPublic, type.FullName));
    }

    /// <summary>NativeMethods imports exactly the paired export inventory, each a raw Cdecl exact-spelling import of the native library.</summary>
    [Fact]
    public void NativeMethodsImportExactlyThePairedExports()
    {
        DllImportAttribute[] imports = PInvokes(typeof(NativeMethods)).Select(Import).ToArray();
        string[] exports = NativeBuildContract.Exports.ToArray();
        Assert.Equal(exports.Order(StringComparer.Ordinal), imports.Select(import => import.EntryPoint ?? "").Order(StringComparer.Ordinal));
        Assert.All(imports, import =>
        {
            Assert.Equal(NativeLibraryName, import.Value);
            Assert.Equal(CallingConvention.Cdecl, import.CallingConvention);
            Assert.True(import.ExactSpelling, import.EntryPoint);
        });
    }

    /// <summary>No P/Invoke anywhere in the assembly, including compiler-generated stubs, targets another library.</summary>
    [Fact]
    public void NoImportTargetsAnotherLibrary()
    {
        MethodInfo[] methods = Library.GetTypes().SelectMany(PInvokes).ToArray();
        Assert.NotEmpty(methods);
        Assert.All(methods, method => Assert.Equal(NativeLibraryName, Import(method).Value));
    }

    private static IEnumerable<MethodInfo> PInvokes(Type type) =>
        type.GetMethods(Declared).Where(method => (method.Attributes & MethodAttributes.PinvokeImpl) != 0);

    private static DllImportAttribute Import(MethodInfo method)
    {
        DllImportAttribute? import = method.GetCustomAttribute<DllImportAttribute>();
        Assert.NotNull(import);
        return import;
    }
}
