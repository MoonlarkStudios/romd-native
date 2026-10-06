using System.Reflection;
using System.Runtime.InteropServices;
using Moonlark.Libchdr.Internal;

namespace Moonlark.Libchdr;

/// <summary>Loads and validates one process-resident native libchdr build.</summary>
/// <remarks>Native libraries are trusted executable code. Verify external binaries before loading.
/// The library remains resident for the lifetime of this assembly and cannot be replaced or unloaded.</remarks>
public static class LibchdrLibrary
{
    private static readonly object Gate = new();
    private static ResidentLibrary? _resident;

    static LibchdrLibrary() => NativeLibrary.SetDllImportResolver(typeof(LibchdrLibrary).Assembly, ResolveImport);

    /// <summary>The validated identity, loading the packaged native asset on first use.</summary>
    public static LibchdrBuildInfo BuildInfo => EnsureLoaded().Info;

    /// <summary>Loads only the supplied absolute path and validates its exact family and ABI identity.</summary>
    /// <param name="absolutePath">A trusted native library file already verified by the host.</param>
    /// <exception cref="ArgumentException">The path is empty or not fully qualified.</exception>
    /// <exception cref="InvalidOperationException">A different path is already loaded.</exception>
    /// <exception cref="PlatformNotSupportedException">The process platform is unsupported.</exception>
    /// <exception cref="DllNotFoundException">The supplied file cannot be loaded.</exception>
    /// <exception cref="BadImageFormatException">The native build is incompatible.</exception>
    public static void Load(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        if (!Path.IsPathFullyQualified(absolutePath))
            throw new ArgumentException("The native library path must be fully qualified.", nameof(absolutePath));
        _ = NativePlatform.Current();
        string path = Path.GetFullPath(absolutePath);
        lock (Gate)
        {
            if (_resident is { } loaded)
            {
                if (!string.Equals(loaded.Path, path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new InvalidOperationException($"libchdr is already loaded from '{loaded.Path}'; cannot load '{path}'.");
                return;
            }
            Volatile.Write(ref _resident, OpenLibrary(path));
        }
    }

    internal static void EnsureInitialized() => _ = EnsureLoaded();

    private static ResidentLibrary EnsureLoaded()
    {
        if (Volatile.Read(ref _resident) is { } loaded) return loaded;
        lock (Gate)
        {
            if (_resident is { } current) return current;
            (string rid, string filename) = NativePlatform.Current();
            foreach (string path in NativePlatform.AssetPaths(rid, filename))
            {
                if (!File.Exists(path)) continue;
                // An incompatible first asset fails; it never triggers a fallback search.
                ResidentLibrary resident = OpenLibrary(path);
                Volatile.Write(ref _resident, resident);
                return resident;
            }
            throw new DllNotFoundException($"Packaged libchdr asset '{filename}' for {rid} was not found in .NET native asset directories or '{AppContext.BaseDirectory}'.");
        }
    }

    private static ResidentLibrary OpenLibrary(string path)
    {
        var handle = new NativeLibraryHandle(NativeLibrary.Load(path));
        try { return new(handle, NativeBuildInfoReader.Read(handle), path); }
        catch { handle.Dispose(); throw; }
    }

    private static nint ResolveImport(string name, Assembly assembly, DllImportSearchPath? searchPath) =>
        name == "moonlark_chdr" ? EnsureLoaded().Handle.DangerousGetHandle() : 0;

    private sealed record ResidentLibrary(NativeLibraryHandle Handle, LibchdrBuildInfo Info, string Path);
}
