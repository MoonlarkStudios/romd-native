using System.Security.Cryptography;
using System.Text.Json;
using Moonlark.Libchdr.Internal;

namespace Moonlark.Libchdr.Tests;

internal static class NativeTestEnvironment
{
    internal static readonly string Root = FindRoot();

    static NativeTestEnvironment()
    {
        (string rid, string filename) = NativePlatform.Current();
        string directory = Path.Combine(Root, "artifacts", "native", "libchdr", rid);
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "build-manifest.json")));
        string binary = Path.Combine(directory, "native", filename);
        byte[] bytes = File.ReadAllBytes(binary);
        if (manifest.RootElement.GetProperty("size").GetInt64() != bytes.LongLength ||
            manifest.RootElement.GetProperty("sha256").GetString() != Convert.ToHexStringLower(SHA256.HashData(bytes)))
            throw new InvalidDataException("Native test asset digest differs from the local manifest.");
        LibchdrLibrary.Load(binary);
    }

    internal static string Fixture(string name) => Path.Combine(Root, "artifacts", "fixtures", "libchdr-final", name);
    internal static ChdFile OpenFixture(string name = "dvd-zstd.chd") => ChdFile.Open(Fixture(name));

    private static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "eng", "pins", "libchdr.json"))) return directory.FullName;
        throw new DirectoryNotFoundException("Native tests require the source checkout and verified synthetic fixtures; none are silently skipped.");
    }
}
