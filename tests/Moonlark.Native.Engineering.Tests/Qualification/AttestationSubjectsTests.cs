using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Qualification;
using Moonlark.Native.Engineering.Tests.TestSupport;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Qualification;

/// <summary>Read-only subject-set checks with synthetic bytes; no signed artifacts or downloaded code are executed.</summary>
public sealed class AttestationSubjectsTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private static readonly string[] Rids = ["linux-x64", "linux-arm64"];
    private static readonly string[] Names = ["libmoonlark_chdr.so", "build-manifest.json", "evidence.json", "builder.json"];

    /// <summary>Every required subject participates exactly once, using computed hashes and unambiguous names.</summary>
    [Fact]
    public void ValidSetProducesExactlyEightUniqueChecksums()
    {
        using var fixture = new SubjectFixture();
        File.WriteAllText(Path.Combine(fixture.Root, "artifacts", "signing", "unreviewed-extra.so"), "must never join the subject set");
        Result<string> result = AttestationSubjects.Create(fixture.Root, Commit);
        Assert.True(result.Succeeded, result.Succeeded ? null : result.Failure.Message);
        string[] lines = result.Value.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(8, lines.Length);
        string[] expected = Rids.SelectMany(rid => Names.Select(name => Digest.Sha256File(fixture.PathOf(rid, name)) + "  " + rid + "/" + name)).ToArray();
        Assert.Equal(expected, lines);
        Assert.Equal(8, lines.Select(line => line[66..]).Distinct(StringComparer.Ordinal).Count());
        Assert.EndsWith("\n", result.Value, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(fixture.Root, "artifacts", "signing", "subjects.sha256")));
    }

    /// <summary>A partial download must never produce a partial list to sign.</summary>
    [Theory]
    [InlineData("linux-x64", "libmoonlark_chdr.so")]
    [InlineData("linux-x64", "build-manifest.json")]
    [InlineData("linux-x64", "evidence.json")]
    [InlineData("linux-x64", "builder.json")]
    [InlineData("linux-arm64", "libmoonlark_chdr.so")]
    [InlineData("linux-arm64", "build-manifest.json")]
    [InlineData("linux-arm64", "evidence.json")]
    [InlineData("linux-arm64", "builder.json")]
    public void EachMissingSubjectRejectsTheWholeSet(string rid, string name)
    {
        using var fixture = new SubjectFixture();
        File.Delete(fixture.PathOf(rid, name));
        Assert.False(AttestationSubjects.Create(fixture.Root, Commit).Succeeded);
    }

    /// <summary>Subjects must be regular files, never redirected leaves.</summary>
    [Theory]
    [InlineData("libmoonlark_chdr.so")]
    [InlineData("build-manifest.json")]
    [InlineData("evidence.json")]
    [InlineData("builder.json")]
    public void LinkedSubjectIsRejected(string name)
    {
        using var fixture = new SubjectFixture();
        string original = fixture.PathOf("linux-x64", name);
        string target = Path.Combine(fixture.Root, "outside-" + name);
        File.Move(original, target);
        File.CreateSymbolicLink(original, target);
        Assert.False(AttestationSubjects.Create(fixture.Root, Commit).Succeeded);
    }

    /// <summary>Directories in place of any file fail before reads or partial output.</summary>
    [Theory]
    [InlineData("libmoonlark_chdr.so")]
    [InlineData("build-manifest.json")]
    [InlineData("evidence.json")]
    [InlineData("builder.json")]
    public void DirectorySubjectIsRejected(string name)
    {
        using var fixture = new SubjectFixture();
        string path = fixture.PathOf("linux-arm64", name);
        File.Delete(path);
        Directory.CreateDirectory(path);
        Assert.False(AttestationSubjects.Create(fixture.Root, Commit).Succeeded);
    }

    /// <summary>An empty binary is invalid even when both claim documents agree with its hash and size.</summary>
    [Fact]
    public void EmptyBinaryIsRejectedDespiteMatchingClaims()
    {
        using var fixture = new SubjectFixture();
        File.WriteAllBytes(fixture.PathOf("linux-x64", "libmoonlark_chdr.so"), []);
        fixture.Change("linux-x64", "build-manifest.json", "size", JsonValue.Create(0));
        fixture.Change("linux-x64", "build-manifest.json", "sha256", JsonValue.Create(Digest.Sha256([])));
        fixture.Change("linux-x64", "evidence.json", "binaryBytes", JsonValue.Create(0));
        fixture.Change("linux-x64", "evidence.json", "binarySha256", JsonValue.Create(Digest.Sha256([])));
        Assert.False(AttestationSubjects.Create(fixture.Root, Commit).Succeeded);
    }

    /// <summary>Each directory component is checked before any artifact bytes are read.</summary>
    [Theory]
    [InlineData("artifacts")]
    [InlineData("artifacts/signing")]
    [InlineData("artifacts/signing/linux-arm64")]
    [InlineData("artifacts/signing/linux-arm64/checkout")]
    [InlineData("artifacts/signing/linux-arm64/checkout/artifacts")]
    [InlineData("artifacts/signing/linux-arm64/checkout/artifacts/native")]
    [InlineData("artifacts/signing/linux-arm64/checkout/artifacts/native/libchdr")]
    [InlineData("artifacts/signing/linux-arm64/checkout/artifacts/native/libchdr/linux-arm64")]
    [InlineData("artifacts/signing/linux-arm64/checkout/artifacts/native/libchdr/linux-arm64/native")]
    [InlineData("artifacts/signing/linux-arm64/checkout/artifacts/qualification")]
    [InlineData("artifacts/signing/linux-arm64/checkout/artifacts/qualification/linux-arm64")]
    public void LinkedParentIsRejected(string relative)
    {
        using var fixture = new SubjectFixture();
        string original = Path.Combine(fixture.Root, relative);
        string target = Path.Combine(fixture.Root, "redirected");
        Directory.Move(original, target);
        Directory.CreateSymbolicLink(original, target);
        Assert.False(AttestationSubjects.Create(fixture.Root, Commit).Succeeded);
    }

    /// <summary>Changing a binary invalidates both recorded claims even when the size stays equal.</summary>
    [Theory]
    [InlineData("linux-x64")]
    [InlineData("linux-arm64")]
    public void AlteredBinaryIsRejected(string rid)
    {
        using var fixture = new SubjectFixture();
        string path = fixture.PathOf(rid, "libmoonlark_chdr.so");
        byte[] data = File.ReadAllBytes(path);
        data[0] ^= 1;
        File.WriteAllBytes(path, data);
        Assert.False(AttestationSubjects.Create(fixture.Root, Commit).Succeeded);
    }

    /// <summary>The binary claims from each document are checked independently.</summary>
    [Theory]
    [InlineData("build-manifest.json", "sha256", "wrong")]
    [InlineData("evidence.json", "binarySha256", "wrong")]
    [InlineData("build-manifest.json", "size", "number")]
    [InlineData("evidence.json", "binaryBytes", "number")]
    [InlineData("build-manifest.json", "rid", "linux-arm64")]
    [InlineData("evidence.json", "rid", "linux-arm64")]
    [InlineData("builder.json", "rid", "linux-arm64")]
    [InlineData("evidence.json", "sourceCommit", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("builder.json", "sourceCommit", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("build-manifest.json", "qualification", "qualified")]
    [InlineData("evidence.json", "qualification", "qualified")]
    [InlineData("builder.json", "qualification", "qualified")]
    [InlineData("build-manifest.json", "attestation", "forged")]
    [InlineData("evidence.json", "attestation", "forged")]
    [InlineData("builder.json", "attestation", "forged")]
    public void InconsistentClaimsAreRejected(string name, string field, string value)
    {
        using var fixture = new SubjectFixture();
        fixture.Change("linux-x64", name, field, value == "number" ? JsonValue.Create(99999) : JsonValue.Create(value));
        Assert.False(AttestationSubjects.Create(fixture.Root, Commit).Succeeded);
    }

    /// <summary>Omitted fields cannot satisfy required identity, byte or qualification checks.</summary>
    [Theory]
    [InlineData("build-manifest.json", "sha256")]
    [InlineData("evidence.json", "binarySha256")]
    [InlineData("build-manifest.json", "size")]
    [InlineData("evidence.json", "binaryBytes")]
    [InlineData("build-manifest.json", "rid")]
    [InlineData("evidence.json", "rid")]
    [InlineData("builder.json", "rid")]
    [InlineData("evidence.json", "sourceCommit")]
    [InlineData("builder.json", "sourceCommit")]
    [InlineData("build-manifest.json", "qualification")]
    [InlineData("evidence.json", "qualification")]
    [InlineData("builder.json", "qualification")]
    [InlineData("build-manifest.json", "attestation")]
    [InlineData("evidence.json", "attestation")]
    [InlineData("builder.json", "attestation")]
    public void MissingClaimsAreRejected(string name, string field)
    {
        using var fixture = new SubjectFixture();
        string path = fixture.PathOf("linux-arm64", name);
        JsonObject document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        document.Remove(field);
        File.WriteAllText(path, document.ToJsonString());
        Assert.False(AttestationSubjects.Create(fixture.Root, Commit).Succeeded);
    }

    /// <summary>The expected source identity is an explicit full commit, not an arbitrary ref or prefix.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("main")]
    [InlineData("0123456789abcdef0123456789abcdef0123456")]
    [InlineData("0123456789abcdef0123456789abcdef012345678")]
    [InlineData("g123456789abcdef0123456789abcdef01234567")]
    public void InvalidExpectedCommitIsRejected(string expected)
    {
        using var fixture = new SubjectFixture();
        Assert.False(AttestationSubjects.Create(fixture.Root, expected).Succeeded);
    }

    /// <summary>Malformed and duplicate-key JSON cannot be interpreted as valid claims.</summary>
    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"rid\":\"linux-x64\",\"rid\":\"linux-arm64\"}")]
    public void InvalidJsonIsRejected(string content)
    {
        using var fixture = new SubjectFixture();
        File.WriteAllText(fixture.PathOf("linux-x64", "builder.json"), content);
        Assert.False(AttestationSubjects.Create(fixture.Root, Commit).Succeeded);
    }

    private sealed class SubjectFixture : IDisposable
    {
        private readonly TemporaryDirectory _directory = new();

        internal SubjectFixture()
        {
            Root = ArtifactsPath.Resolve(_directory.Path);
            foreach (string rid in Rids)
            {
                byte[] data = System.Text.Encoding.ASCII.GetBytes("synthetic " + rid + " binary");
                string binary = PathOf(rid, "libmoonlark_chdr.so");
                Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
                File.WriteAllBytes(binary, data);
                JsonObject manifest = Identity(rid);
                manifest["sha256"] = Digest.Sha256(data);
                manifest["size"] = data.Length;
                JsonObject evidence = Identity(rid);
                evidence["sourceCommit"] = Commit;
                evidence["binarySha256"] = Digest.Sha256(data);
                evidence["binaryBytes"] = data.Length;
                JsonObject builder = Identity(rid);
                builder["sourceCommit"] = Commit;
                Write(rid, "build-manifest.json", manifest);
                Write(rid, "evidence.json", evidence);
                Write(rid, "builder.json", builder);
            }
        }

        internal string Root { get; }

        internal string PathOf(string rid, string name)
        {
            string root = Path.Combine(Root, "artifacts", "signing", rid);
            return name switch
            {
                "builder.json" => Path.Combine(root, name),
                "evidence.json" => Path.Combine(root, "checkout", "artifacts", "qualification", rid, name),
                "build-manifest.json" => Path.Combine(root, "checkout", "artifacts", "native", "libchdr", rid, name),
                _ => Path.Combine(root, "checkout", "artifacts", "native", "libchdr", rid, "native", name),
            };
        }

        internal void Change(string rid, string name, string field, JsonNode? value)
        {
            string path = PathOf(rid, name);
            JsonObject document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            document[field] = value;
            Write(rid, name, document);
        }

        private void Write(string rid, string name, JsonObject document)
        {
            string path = PathOf(rid, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, document.ToJsonString());
        }

        private static JsonObject Identity(string rid) => new() { ["rid"] = rid, ["qualification"] = "local-unqualified", ["attestation"] = null };

        public void Dispose() => _directory.Dispose();
    }
}
