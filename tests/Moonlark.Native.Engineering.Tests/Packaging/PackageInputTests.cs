using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Packaging;
using Moonlark.Native.Engineering.Tests.TestSupport;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Packaging;

/// <summary>Output freshness and full-license provenance are independent of package creation.</summary>
public sealed class PackageInputTests
{
    /// <summary>The original exact 12-span inventory passes.</summary>
    [Fact]
    public void ExactLicenseInventoryPasses()
    {
        using var fixture = new LicenseFixture();
        Assert.Null(PackageLicenses.Verify(fixture.Root));
    }

    /// <summary>A rehashed inventory cannot authorize changed grants or missing source provenance.</summary>
    [Theory]
    [InlineData("missing-file")]
    [InlineData("missing-span")]
    [InlineData("duplicate-span")]
    [InlineData("unknown-component")]
    [InlineData("selected-grant")]
    [InlineData("upstream-pin")]
    [InlineData("source-bytes")]
    [InlineData("license-bytes")]
    [InlineData("rehashed-grant")]
    [InlineData("official-commit")]
    [InlineData("official-hash")]
    [InlineData("escaped-source")]
    public void CorruptLicenseInventoryFails(string change)
    {
        using var fixture = new LicenseFixture();
        JsonObject inventory = JsonNode.Parse(File.ReadAllText(fixture.Inventory))!.AsObject();
        JsonArray spans = inventory["sourceSpans"]!.AsArray();
        switch (change)
        {
            case "missing-file": File.Delete(fixture.License); break;
            case "missing-span": spans.RemoveAt(0); break;
            case "duplicate-span": spans[1] = spans[0]!.DeepClone(); break;
            case "unknown-component": spans[0]!["component"] = "Other"; break;
            case "selected-grant": spans[^1]!["selectedGrant"] = "MIT"; break;
            case "upstream-pin": inventory["libchdrCommit"] = new string('0', 40); break;
            case "source-bytes": File.AppendAllText(Path.Combine(fixture.Root, "LICENSE"), "changed"); break;
            case "license-bytes": File.AppendAllText(fixture.License, "changed"); break;
            case "rehashed-grant":
                byte[] bytes = File.ReadAllBytes(fixture.License);
                int offset = (int)spans[^1]!["packageStartByte"]!; bytes[offset + 10] ^= 1;
                File.WriteAllBytes(fixture.License, bytes);
                inventory["licenseFile"]!["sha256"] = Digest.Sha256(bytes);
                spans[^1]!["textSha256"] = Digest.Sha256(bytes.AsSpan(offset, (int)spans[^1]!["byteLength"]!)); break;
            case "official-commit": inventory["zstdProvenance"]!["commit"] = new string('0', 40); break;
            case "official-hash": inventory["zstdProvenance"]!["officialLicenseSha256"] = new string('0', 64); break;
            case "escaped-source": spans[1]!["sourcePath"] = "../../outside"; break;
            default: throw new InvalidOperationException(change);
        }
        File.WriteAllText(fixture.Inventory, inventory.ToJsonString());
        Assert.NotNull(PackageLicenses.Verify(fixture.Root));
    }

    /// <summary>Candidate paths cannot escape artifacts or overwrite previous evidence.</summary>
    [Theory]
    [InlineData("existing")]
    [InlineData("outside")]
    [InlineData("symlink")]
    public void UnsafeCandidateOutputFails(string change)
    {
        using var temp = new TemporaryDirectory();
        string output = Path.Combine(temp.Path, "artifacts", "new");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (change == "existing") Directory.CreateDirectory(output);
        if (change == "outside") output = Path.Combine(temp.Path, "outside");
        if (change == "symlink") Directory.CreateSymbolicLink(output, temp.Path);
        Assert.False(PackagePaths.NewArtifactsDirectory(temp.Path, output).Succeeded);
    }

    private sealed class LicenseFixture : IDisposable
    {
        private readonly TemporaryDirectory _directory = new();
        internal string Root => _directory.Path;
        internal string Inventory => Path.Combine(Root, PackageLicenses.DirectoryPath, "source-inventory.json");
        internal string License => Path.Combine(Root, PackageLicenses.DirectoryPath, "LICENSE.txt");
        internal LicenseFixture()
        {
            TestRepository.CopyTo(Root, Authorities.PinPath, Authorities.PropsPath, "LICENSE", PackageLicenses.DirectoryPath);
            JsonObject inventory = JsonNode.Parse(File.ReadAllText(Inventory))!.AsObject();
            foreach (string source in inventory["sourceSpans"]!.AsArray()
                .Where(span => (string?)span!["sourceCommit"] == (string?)inventory["libchdrCommit"])
                .Select(span => (string)span!["sourcePath"]!).Distinct(StringComparer.Ordinal))
                TestRepository.CopyTo(Root, "native/libchdr/upstream/" + source);
        }
        public void Dispose() => _directory.Dispose();
    }
}
