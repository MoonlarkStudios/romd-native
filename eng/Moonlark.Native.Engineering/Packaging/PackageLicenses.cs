using System.Text.Json;
using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Core;

namespace Moonlark.Native.Engineering.Packaging;

/// <summary>Checks the reviewed full-text grant inventory against current pinned source bytes.</summary>
internal static class PackageLicenses
{
    internal const string DirectoryPath = "src/Moonlark.Libchdr.Native/licenses";
    private const string ZstdCommit = "f8745da6ff1ad1e7bab384bd1f9d742439278e99";
    private const string ZstdLicenseHash = "7055266497633c9025b777c78eb7235af13922117480ed5c674677adc381c9d8";
    private sealed record SpanSource(string Component, string Grant, string Path, int First, int Last, bool Repository = false, bool Official = false);
    private static readonly SpanSource[] Sources =
    [
        new("Moonlark wrapper/build-info", "MIT", "LICENSE", 1, 21, Repository: true),
        new("libchdr root notice", "BSD-3-Clause", "LICENSE.txt", 1, 24),
        new("libchdr Aaron Giles notice", "BSD-3-Clause", "src/libchdr_chd.c", 9, 36),
        new("miniz common notice", "MIT", "deps/miniz-3.1.2/miniz.c", 2, 26),
        new("miniz additional copyright notice", "MIT", "deps/miniz-3.1.2/miniz.c", 3028, 3053),
        new("miniz retained public-domain statement", "Public-domain statement retained; MIT grant selected above", "deps/miniz-3.1.2/miniz.c", 622, 647),
        new("LZMA SDK", "Public domain as stated upstream", "deps/lzma-26.02/LICENSE", 1, 3),
        new("LZMA decoder attribution", "Public domain as stated upstream", "deps/lzma-26.02/src/real/LzmaDec.c", 1, 2),
        new("zstd full BSD grant", "BSD-3-Clause choice", "LICENSE", 1, 30, Official: true),
        new("zstd original choice notice", "BSD-3-Clause choice", "deps/zstd-1.5.7/zstddeclib.c", 10, 18),
        new("zstd embedded xxHash notice", "BSD-style choice stated in source", "deps/zstd-1.5.7/zstddeclib.c", 7437, 7446),
        new("dr_flac complete alternative grants", "MIT-0 selected; both original alternatives retained", "include/dr_libs/dr_flac.h", 12712, 12760),
    ];

    internal static Failure? Verify(string root)
    {
        try
        {
            string licensePath = Path.Combine(root, DirectoryPath, "LICENSE.txt");
            string inventoryPath = Path.Combine(root, DirectoryPath, "source-inventory.json");
            Require(PackagePaths.RegularFile(licensePath, root) is null && PackagePaths.RegularFile(inventoryPath, root) is null, "Missing regular license/inventory");
            byte[] license = File.ReadAllBytes(licensePath);
            JsonObject inventory = JsonNode.Parse(File.ReadAllText(inventoryPath))!.AsObject();
            string pin = Authorities.ReadLibchdr(root).Value.Pin.Commit;
            Require((int?)inventory["schemaVersion"] == 1 && (string?)inventory["libchdrCommit"] == pin, "License source pin mismatch");
            Require((string?)inventory["licenseFile"]?["path"] == "LICENSE.txt" && (long?)inventory["licenseFile"]?["bytes"] == license.LongLength
                && (string?)inventory["licenseFile"]?["sha256"] == Digest.Sha256(license), "Complete license file changed");
            Require((string?)inventory["zstdProvenance"]?["commit"] == ZstdCommit
                && (string?)inventory["zstdProvenance"]?["officialLicenseSha256"] == ZstdLicenseHash, "Official zstd grant provenance changed");
            JsonArray spans = inventory["sourceSpans"]!.AsArray();
            Require(spans.Count == Sources.Length, "Incomplete license span inventory");
            for (int index = 0; index < Sources.Length; index++)
            {
                SpanSource source = Sources[index];
                JsonObject span = spans[index]!.AsObject();
                Require((string?)span["component"] == source.Component && (string?)span["selectedGrant"] == source.Grant
                    && (string?)span["sourcePath"] == source.Path && (int?)span["startLine"] == source.First && (int?)span["endLine"] == source.Last
                    && (string?)span["packagePath"] == "LICENSE.txt", "Unknown or changed license selection/span");
                int offset = (int)span["packageStartByte"]!;
                int length = (int)span["byteLength"]!;
                Require(offset >= 0 && length > 0 && offset <= license.Length - length, "License span is outside the bundled text");
                ReadOnlySpan<byte> text = license.AsSpan(offset, length);
                Require(Digest.Sha256(text) == (string?)span["textSha256"], "License span digest mismatch");
                if (source.Official)
                {
                    Require((string?)span["sourceCommit"] == ZstdCommit && (string?)span["sourceUrl"] == "https://github.com/facebook/zstd/blob/" + ZstdCommit + "/LICENSE"
                        && (int?)span["startByte"] == 0 && (long?)span["sourceBytes"] == 1549 && (string?)span["sourceSha256"] == ZstdLicenseHash
                        && length == 1549 && Digest.Sha256(text) == ZstdLicenseHash, "Official full zstd grant changed");
                    continue;
                }
                string path = Path.Combine(root, source.Repository ? "" : "native/libchdr/upstream", source.Path);
                Require(PackagePaths.RegularFile(path, root) is null, "License source is missing or redirected");
                byte[] bytes = File.ReadAllBytes(path);
                Require(bytes.LongLength == (long?)span["sourceBytes"] && Digest.Sha256(bytes) == (string?)span["sourceSha256"], "License source bytes changed");
                if (!source.Repository) Require((string?)span["sourceCommit"] == pin, "License source commit differs from pin");
                (int first, int count) = LineSpan(bytes, source.First, source.Last);
                Require(first == (int?)span["startByte"] && count == length && bytes.AsSpan(first, count).SequenceEqual(text), "Bundled license differs from its full original span");
            }
            return null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or NullReferenceException)
        {
            return new Failure("License inventory failed: " + error.Message);
        }
    }

    private static (int Offset, int Length) LineSpan(byte[] bytes, int first, int last)
    {
        int line = 1, start = first == 1 ? 0 : -1, end = bytes.Length;
        for (int index = 0; index < bytes.Length; index++)
            if (bytes[index] == (byte)'\n')
            {
                if (line == last) { end = index + 1; break; }
                line++;
                if (line == first) start = index + 1;
            }
        Require(start >= 0, "Missing source license line");
        return (start, end - start);
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
