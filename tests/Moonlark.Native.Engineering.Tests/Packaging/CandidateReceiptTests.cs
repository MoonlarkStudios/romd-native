using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Packaging;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Packaging;

/// <summary>Candidate declarations must agree with the actual native package plan, even if someone rewrites receipt hashes.</summary>
public sealed class CandidateReceiptTests
{
    /// <summary>Exact partial and complete inventories preserve their explicit coverage classification.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactInventoryPasses(bool complete) => Assert.Null(CandidateConsumer.VerifyInventory(Receipt(complete)));

    /// <summary>Duplicate, invented or misbound declarations do not establish packaged RID/license coverage.</summary>
    [Theory]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("declared-mismatch")]
    [InlineData("coverage-claim")]
    [InlineData("missing-native-plan")]
    [InlineData("wrong-native-id")]
    [InlineData("missing-packaged-rid")]
    [InlineData("binary-source")]
    [InlineData("binary-hash")]
    [InlineData("manifest-hash")]
    [InlineData("license-hash")]
    [InlineData("inventory-hash")]
    [InlineData("missing-source-identity")]
    public void IncorrectInventoryFails(string change)
    {
        CandidateReceipt receipt = Receipt(complete: false);
        NativeCandidate native = receipt.Native.Single();
        CandidatePackage package = receipt.Packages.Single(item => item.Plan.Native);
        string prefix = "runtimes/" + native.Rid + "/native/";
        switch (change)
        {
            case "duplicate": receipt = receipt with { Native = [native, native], DeclaredRids = [native.Rid, native.Rid] }; break;
            case "unknown": receipt = receipt with { Native = [native with { Rid = "linux-musl-x64" }], DeclaredRids = ["linux-musl-x64"] }; break;
            case "declared-mismatch": receipt = receipt with { DeclaredRids = ["linux-arm64"] }; break;
            case "coverage-claim": receipt = receipt with { CompleteRidInventory = true }; break;
            case "missing-native-plan": receipt = receipt with { Packages = receipt.Packages.Where(item => !item.Plan.Native).ToArray() }; break;
            case "wrong-native-id": ChangePlan(plan => plan with { Id = "Another.Native" }); break;
            case "missing-packaged-rid": ChangePlan(plan => plan with { Files = plan.Files.Where(file => !file.PackagePath.StartsWith("runtimes/", StringComparison.Ordinal)).ToArray() }); break;
            case "binary-source": ChangeFile(prefix + NativeRids.LibraryFileName(native.Rid), file => file with { SourcePath = file.SourcePath + ".other" }); break;
            case "binary-hash": ChangeFile(prefix + NativeRids.LibraryFileName(native.Rid), file => file with { Sha256 = new string('0', 64) }); break;
            case "manifest-hash": ChangeFile(prefix + "moonlark_chdr." + native.Rid + ".build-manifest.json", file => file with { Sha256 = new string('0', 64) }); break;
            case "license-hash": ChangeFile("LICENSE.txt", file => file with { Sha256 = new string('0', 64) }); break;
            case "inventory-hash": ChangeFile("licenses/source-inventory.json", file => file with { Sha256 = new string('0', 64) }); break;
            case "missing-source-identity": receipt = receipt with { SourceCommit = null! }; break;
            default: throw new InvalidOperationException(change);
        }
        Assert.NotNull(CandidateConsumer.VerifyInventory(receipt));

        void ChangePlan(Func<PackagePlan, PackagePlan> changePlan) => receipt = receipt with
        { Packages = receipt.Packages.Select(item => item == package ? item with { Plan = changePlan(item.Plan) } : item).ToArray() };
        void ChangeFile(string path, Func<PackageFile, PackageFile> changeFile) => ChangePlan(plan => plan with
        { Files = plan.Files.Select(file => file.PackagePath == path ? changeFile(file) : file).ToArray() });
    }

    private static CandidateReceipt Receipt(bool complete)
    {
        string[] rids = complete ? [.. NativeRids.Supported] : ["linux-x64"];
        const string version = "1.0.0-preview.1";
        string root = Path.Combine(Path.GetTempPath(), "candidate-receipt-only");
        string licenseHash = new('c', 64), inventoryHash = new('d', 64);
        NativeCandidate[] native = rids.Select(rid => new NativeCandidate(rid, Path.Combine(root, rid, "build-manifest.json"),
            Path.Combine(root, rid, "native", NativeRids.LibraryFileName(rid)), new string('f', 64), new string('e', 64), new string('b', 64))).ToArray();
        List<PackageFile> files = [new(Path.Combine(root, "LICENSE.txt"), "LICENSE.txt", 1, licenseHash),
            new(Path.Combine(root, "source-inventory.json"), "licenses/source-inventory.json", 1, inventoryHash)];
        foreach (NativeCandidate item in native)
        {
            string prefix = "runtimes/" + item.Rid + "/native/";
            files.Add(new(item.BinaryPath, prefix + NativeRids.LibraryFileName(item.Rid), 1, item.BinarySha256));
            files.Add(new(item.ManifestPath, prefix + "moonlark_chdr." + item.Rid + ".build-manifest.json", 1, item.ManifestSha256));
        }
        return new(1, "local-unqualified", new string('a', 40), version, root, rids, complete, native,
            [new(Path.Combine(root, "managed.nupkg"), new string('1', 64), new("Moonlark.Libchdr", version, false, [])),
                new(Path.Combine(root, "native.nupkg"), new string('2', 64), new("Moonlark.Libchdr.Native", version, true, files))],
            licenseHash, inventoryHash, new JsonArray());
    }
}
