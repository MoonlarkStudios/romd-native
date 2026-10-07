using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Chdman;
using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Fixtures;
using Moonlark.Native.Engineering.Tests.TestSupport;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Fixtures;

/// <summary>Transferred fixture integrity checks require no native library or chdman executable.</summary>
public sealed class FixtureVerificationTests
{
    /// <summary>A complete receipt retains tool provenance without pretending to verify its unavailable executable.</summary>
    [Fact]
    public void CompleteInventoryPreservesReceiptWithoutToolExecutable()
    {
        using var fixture = new FixtureDirectory();
        Result<JsonObject> result = fixture.Verify();
        Assert.True(result.Succeeded, result.Succeeded ? "" : result.Failure.Message);
        Assert.True(JsonNode.DeepEquals(fixture.Manifest, result.Value));
        Assert.False(File.Exists(Path.Combine(fixture.Path, "unavailable-chdman")));
    }

    /// <summary>Corrupted claims cannot turn a partial, inconsistent, or unpinned transfer into verified evidence.</summary>
    [Theory]
    [InlineData("schema")]
    [InlineData("synthetic")]
    [InlineData("generator")]
    [InlineData("pin")]
    [InlineData("tool-schema")]
    [InlineData("source-missing")]
    [InlineData("source-duplicate")]
    [InlineData("fixture-missing")]
    [InlineData("fixture-duplicate")]
    [InlineData("negative-missing")]
    [InlineData("negative-duplicate")]
    [InlineData("codec")]
    [InlineData("header")]
    [InlineData("computed")]
    [InlineData("verified")]
    [InlineData("none-verified")]
    [InlineData("recovery")]
    [InlineData("negative-accepted")]
    [InlineData("negative-diagnostic")]
    [InlineData("negative-exit")]
    [InlineData("negative-missing-boolean")]
    [InlineData("wrong-boolean-type")]
    [InlineData("bytes")]
    [InlineData("sha1")]
    [InlineData("sha256")]
    [InlineData("traversal")]
    [InlineData("absolute")]
    public void RejectsCorruptedManifest(string corruption)
    {
        using var fixture = new FixtureDirectory();
        JsonObject manifest = fixture.Manifest;
        JsonArray sources = manifest["sources"]!.AsArray();
        JsonArray fixtures = manifest["fixtures"]!.AsArray();
        JsonArray negatives = manifest["negativeVerification"]!.AsArray();
        JsonNode first = fixtures[0]!;
        switch (corruption)
        {
            case "schema": manifest["schemaVersion"] = 2; break;
            case "synthetic": manifest["synthetic"] = false; break;
            case "generator": manifest["generatorVersion"] = "future"; break;
            case "pin": manifest["tool"]!["pin"]!["commit"] = new string('0', 40); break;
            case "tool-schema": manifest["tool"]!["schemaVersion"] = 2; break;
            case "source-missing": sources.RemoveAt(0); break;
            case "source-duplicate": sources[1] = sources[0]!.DeepClone(); break;
            case "fixture-missing": fixtures.RemoveAt(0); break;
            case "fixture-duplicate": fixtures[1] = first.DeepClone(); break;
            case "negative-missing": negatives.RemoveAt(0); break;
            case "negative-duplicate": negatives[1] = negatives[0]!.DeepClone(); break;
            case "codec": first["codec"] = "zstd"; break;
            case "header": first["header"]!["hunkBytes"] = 1; break;
            case "computed": first["computedOverallSha1"] = new string('0', 40); break;
            case "verified": first["chdmanVerifiedBothHashes"] = false; break;
            case "none-verified": fixtures[5]!["chdmanVerifiedBothHashes"] = true; break;
            case "recovery": first["exactSourceRecovery"] = false; break;
            case "negative-accepted": negatives[0]!["acceptedByVerificationParser"] = true; break;
            case "negative-diagnostic": negatives[0]!["diagnostic"] = "success"; break;
            case "negative-exit": negatives[0]!["exitCode"] = 1; break;
            case "negative-missing-boolean": negatives[0]!.AsObject().Remove("acceptedByVerificationParser"); break;
            case "wrong-boolean-type": first["exactSourceRecovery"] = "true"; break;
            case "bytes": first["logical"]!["bytes"] = 0; break;
            case "sha1": first["chd"]!["sha1"] = new string('0', 40); break;
            case "sha256": sources[0]!["sha256"] = new string('0', 64); break;
            case "traversal": first["logical"]!["path"] = "../dvd-lzma.logical"; break;
            case "absolute": first["logical"]!["path"] = Path.Combine(fixture.Path, "dvd-lzma.logical"); break;
            default: throw new InvalidOperationException(corruption);
        }
        Assert.False(fixture.Verify().Succeeded);
    }

    /// <summary>Each referenced file category is checked against the bytes on disk.</summary>
    [Theory]
    [InlineData("dvd-source.iso")]
    [InlineData("dvd-lzma.chd")]
    [InlineData("dvd-lzma.logical")]
    [InlineData("dvd-lzma.extracted.iso")]
    [InlineData("negative-raw-mismatch.chd")]
    public void RejectsChangedAndMissingFiles(string name)
    {
        using var fixture = new FixtureDirectory();
        string path = Path.Combine(fixture.Path, name);
        byte[] bytes = File.ReadAllBytes(path);
        bytes[^1] ^= 1;
        File.WriteAllBytes(path, bytes);
        Assert.False(fixture.Verify().Succeeded);
        File.Delete(path);
        Assert.False(fixture.Verify().Succeeded);
    }

    /// <summary>Even digest-matching links must not redirect a transferred fixture or manifest.</summary>
    [Theory]
    [InlineData("dvd-source.iso")]
    [InlineData(FixtureGenerator.ManifestName)]
    public void RejectsSymbolicLinks(string name)
    {
        using var fixture = new FixtureDirectory();
        fixture.Save();
        string path = Path.Combine(fixture.Path, name);
        string target = path + ".target";
        File.Move(path, target);
        File.CreateSymbolicLink(path, target);
        Assert.False(FixtureVerification.Verify(TestRepository.Root, fixture.Path).Succeeded);
    }

    /// <summary>Manifest decoding and directory validation fail closed without reading redirected fixture data.</summary>
    [Fact]
    public void RejectsMalformedManifestAndRedirectedDirectory()
    {
        using var fixture = new FixtureDirectory();
        string manifest = Path.Combine(fixture.Path, FixtureGenerator.ManifestName);
        foreach (string text in new[] { "not json", "[]", "null", "{}" })
        {
            File.WriteAllText(manifest, text);
            Assert.False(FixtureVerification.Verify(TestRepository.Root, fixture.Path).Succeeded);
        }
        fixture.Save();
        using var links = new TemporaryDirectory();
        string link = Path.Combine(links.Path, "fixtures");
        Directory.CreateSymbolicLink(link, fixture.Path);
        Assert.False(FixtureVerification.Verify(TestRepository.Root, link).Succeeded);
        File.Delete(Path.Combine(fixture.Path, "dvd-source.iso"));
        Directory.CreateDirectory(Path.Combine(fixture.Path, "dvd-source.iso"));
        Assert.False(fixture.Verify().Succeeded);
    }

    /// <summary>Inventory membership, rather than incidental array order, determines completeness.</summary>
    [Fact]
    public void AcceptsReorderedInventories()
    {
        using var fixture = new FixtureDirectory();
        foreach (string name in new[] { "sources", "fixtures", "negativeVerification" })
            fixture.Manifest[name] = new JsonArray([.. fixture.Manifest[name]!.AsArray().Reverse().Select(item => item!.DeepClone())]);
        Result<JsonObject> result = fixture.Verify();
        Assert.True(result.Succeeded, result.Succeeded ? "" : result.Failure.Message);
    }

    /// <summary>A valid CHD encoded with another compressor cannot satisfy a rehashed inventory entry.</summary>
    [Fact]
    public void RejectsRehashedFixtureUsingAnotherCodec()
    {
        using var fixture = new FixtureDirectory();
        byte[] zlib = File.ReadAllBytes(Path.Combine(fixture.Path, "dvd-zlib.chd"));
        fixture.Manifest["fixtures"]![2]!["chd"] = fixture.Write("dvd-huff.chd", zlib);
        Assert.False(fixture.Verify().Succeeded);
    }

    /// <summary>The selected compressor and all three unused slots must match the fixture recipe, including none.</summary>
    [Theory]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 3)]
    [InlineData(5, 0)]
    [InlineData(5, 1)]
    [InlineData(5, 2)]
    [InlineData(5, 3)]
    public void RejectsRehashedCompressorSlotMismatch(int fixtureIndex, int slot)
    {
        using var fixture = new FixtureDirectory();
        JsonNode entry = fixture.Manifest["fixtures"]![fixtureIndex]!;
        string name = entry["chd"]!["path"]!.GetValue<string>();
        byte[] chd = File.ReadAllBytes(Path.Combine(fixture.Path, name));
        "zlib"u8.CopyTo(chd.AsSpan(16 + slot * 4, 4));
        entry["chd"] = fixture.Write(name, chd);
        Assert.False(fixture.Verify().Succeeded);
    }

    /// <summary>A trailing directory separator does not hide a redirected fixture root.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsLinkedDirectoryRegardlessOfTrailingSeparator(bool trailingSeparator)
    {
        using var fixture = new FixtureDirectory();
        fixture.Save();
        using var links = new TemporaryDirectory();
        string link = Path.Combine(links.Path, "fixtures");
        Directory.CreateSymbolicLink(link, fixture.Path);
        string candidate = trailingSeparator ? link + Path.DirectorySeparatorChar : link;
        Assert.False(FixtureVerification.Verify(TestRepository.Root, candidate).Succeeded);
    }

    /// <summary>Rehashing altered source, recovery, logical, or negative bytes does not bypass semantic checks.</summary>
    [Theory]
    [InlineData("source")]
    [InlineData("extracted")]
    [InlineData("logical")]
    [InlineData("negative")]
    public void RejectsSelfConsistentFileReceiptForWrongContent(string category)
    {
        using var fixture = new FixtureDirectory();
        JsonNode parent = category switch
        {
            "source" => fixture.Manifest["sources"]!,
            "negative" => fixture.Manifest["negativeVerification"]![0]!,
            _ => fixture.Manifest["fixtures"]![0]!,
        };
        JsonNode entry = category == "source" ? parent[0]! : parent[category == "negative" ? "chd" : category]!;
        string name = entry["path"]!.GetValue<string>();
        byte[] bytes = File.ReadAllBytes(Path.Combine(fixture.Path, name));
        bytes[^1] ^= 1;
        JsonObject changed = fixture.Write(name, bytes);
        if (category == "source") parent[0] = changed;
        else parent[category == "negative" ? "chd" : category] = changed;
        Assert.False(fixture.Verify().Succeeded);
    }

    private sealed class FixtureDirectory : IDisposable
    {
        private readonly TemporaryDirectory _directory = new();
        internal string Path => _directory.Path;
        internal JsonObject Manifest { get; }

        internal FixtureDirectory()
        {
            byte[] dvd = SyntheticSources.Dvd();
            byte[] cd = SyntheticSources.Cd();
            byte[] subcode = SyntheticSources.CdSubcode();
            var fixtures = new JsonArray();
            foreach (string codec in FixtureGenerator.DvdCodecs) fixtures.Add(Make("dvd", codec, "dvd-" + codec, dvd));
            foreach (string codec in FixtureGenerator.CdCodecs) fixtures.Add(Make("cd", codec, "cd-" + codec, cd));
            fixtures.Add(Make("cd", "cdlz", "cd-subcode", subcode));
            var negatives = new JsonArray();
            foreach ((string name, int offset, string diagnostic) in new[]
            {
                ("raw-mismatch", 64, "Error: Raw SHA1 in header"),
                ("overall-mismatch", 84, "Error: Overall SHA1 in header"),
                ("raw-missing", 64, "No verification to be done; CHD has no checksum"),
            })
            {
                byte[] bytes = File.ReadAllBytes(System.IO.Path.Combine(Path, "dvd-lzma.chd"));
                if (name == "raw-missing") bytes.AsSpan(64, 20).Clear();
                else bytes[offset] ^= 1;
                negatives.Add(new JsonObject
                {
                    ["case"] = name, ["chd"] = Write("negative-" + name + ".chd", bytes),
                    ["exitCode"] = 0, ["diagnostic"] = diagnostic, ["acceptedByVerificationParser"] = false,
                });
            }
            Manifest = new JsonObject
            {
                ["schemaVersion"] = 1, ["synthetic"] = true, ["generatorVersion"] = FixtureGenerator.GeneratorVersion,
                ["tool"] = new JsonObject
                {
                    ["schemaVersion"] = 1, ["pin"] = MamePin.ReadDocument(TestRepository.Root),
                    ["binary"] = new JsonObject { ["path"] = "unavailable-chdman", ["bytes"] = 1, ["sha256"] = new string('a', 64) },
                    ["recipeSha256"] = new string('b', 64), ["qualification"] = "local-unqualified",
                },
                ["sources"] = new JsonArray(Write("dvd-source.iso", dvd), Write("cd-source.bin", cd),
                    Write("cd-source.cue", Encoding.ASCII.GetBytes(SyntheticSources.CdCue)), Write("cd-subcode-source.bin", subcode),
                    Write("cd-subcode-source.toc", Encoding.ASCII.GetBytes(SyntheticSources.CdSubcodeToc))),
                ["fixtures"] = fixtures, ["negativeVerification"] = negatives,
            };
        }

        internal JsonObject Write(string name, byte[] bytes)
        {
            File.WriteAllBytes(System.IO.Path.Combine(Path, name), bytes);
            return FixtureFile.Read(Path, name).ToJson();
        }

        private JsonObject Make(string media, string codec, string stem, byte[] logical)
        {
            byte[] chd = new byte[124];
            "MComprHD"u8.CopyTo(chd);
            BinaryPrimitives.WriteUInt32BigEndian(chd.AsSpan(8), 124);
            BinaryPrimitives.WriteUInt32BigEndian(chd.AsSpan(12), 5);
            if (codec != "none") Encoding.ASCII.GetBytes(codec).CopyTo(chd, 16);
            BinaryPrimitives.WriteUInt64BigEndian(chd.AsSpan(32), (ulong)logical.Length);
            BinaryPrimitives.WriteUInt32BigEndian(chd.AsSpan(56), media == "dvd" ? 16384U : 19584U);
            BinaryPrimitives.WriteUInt32BigEndian(chd.AsSpan(60), media == "dvd" ? 2048U : 2448U);
            string raw = ChdHeader.Sha1(logical);
            string overall = ChdHeader.ComputeOverallSha1(raw, []);
            if (codec != "none")
            {
                Convert.FromHexString(raw).CopyTo(chd, 64);
                Convert.FromHexString(overall).CopyTo(chd, 84);
            }
            return new JsonObject
            {
                ["media"] = media, ["codec"] = codec, ["chd"] = Write(stem + ".chd", chd),
                ["header"] = ChdHeader.Parse(chd).Value.ToJson(), ["logical"] = Write(stem + ".logical", logical),
                ["computedOverallSha1"] = overall, ["chdmanVerifiedBothHashes"] = codec != "none", ["exactSourceRecovery"] = true,
                ["extracted"] = Write(stem + ".extracted." + (media == "dvd" ? "iso" : "bin"), logical),
            };
        }

        internal void Save() => File.WriteAllText(System.IO.Path.Combine(Path, FixtureGenerator.ManifestName), ReceiptJson.Serialize(Manifest));
        internal Result<JsonObject> Verify()
        {
            Save();
            return FixtureVerification.Verify(TestRepository.Root, Path);
        }

        public void Dispose() => _directory.Dispose();
    }
}
