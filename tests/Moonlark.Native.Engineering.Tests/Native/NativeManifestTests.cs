using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Native;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Native;

/// <summary>Manifest-only verification fails closed on tampering, false claims and recipe drift.</summary>
public sealed class NativeManifestTests
{
    /// <summary>A manifest written by the builder for its binary verifies.</summary>
    [Fact]
    public void ValidLocalManifestPasses()
    {
        using var root = new NativeRoot();
        (string binary, JsonObject manifest) = root.Manifest();
        Assert.Null(NativeVerify.VerifyManifest(manifest, binary, root.Path));
        Assert.Equal(NativePolicy.ManifestSchemaVersion, (int)manifest["schemaVersion"]!);
        Assert.Equal(NativePolicy.RecipeSchemaVersion, (int)manifest["recipe"]!["schemaVersion"]!);
    }

    /// <summary>Tampered digest, size, filename and RID are rejected.</summary>
    [Theory]
    [InlineData("sha256", "\"0000000000000000000000000000000000000000000000000000000000000000\"", "digest")]
    [InlineData("size", "1", "size")]
    [InlineData("size", "true", "size")]
    [InlineData("file", "\"../libmoonlark_chdr.so\"", "filename")]
    [InlineData("rid", "\"linux-musl-x64\"", "RID")]
    public void TamperedDigestSizeFilenameAndRidFail(string field, string value, string message)
    {
        using var root = new NativeRoot();
        (string binary, JsonObject manifest) = root.Manifest();
        manifest[field] = JsonNode.Parse(value);
        Assert.Contains(message, NativeVerify.VerifyManifest(manifest, binary, root.Path)?.Message, StringComparison.Ordinal);
    }

    /// <summary>A symlink substituted for the binary is not a regular file.</summary>
    [Fact]
    public void SymlinkedBinaryFails()
    {
        using var root = new NativeRoot();
        (string binary, JsonObject manifest) = root.Manifest();
        string real = Path.Combine(Path.GetDirectoryName(binary)!, "real.so");
        File.Move(binary, real);
        File.CreateSymbolicLink(binary, real);
        Assert.Contains("regular file", NativeVerify.VerifyManifest(manifest, binary, root.Path)?.Message, StringComparison.Ordinal);
    }

    /// <summary>A local manifest can never claim release qualification or an attestation.</summary>
    [Theory]
    [InlineData("qualification", "\"release-qualified\"")]
    [InlineData("attestation", "\"https://example.invalid/provenance\"")]
    public void ManifestCannotClaimQualificationOrAttestation(string field, string value)
    {
        using var root = new NativeRoot();
        (string binary, JsonObject manifest) = root.Manifest();
        manifest[field] = JsonNode.Parse(value);
        Assert.Contains("qualification or attestation", NativeVerify.VerifyManifest(manifest, binary, root.Path)?.Message, StringComparison.Ordinal);
    }

    /// <summary>Changed pin, disabled features, a different preset, compiler or input digest, or any changed setting fail.</summary>
    [Theory]
    [InlineData("pin", "source/pin")]
    [InlineData("feature", "flags")]
    [InlineData("missing-setting", "flags")]
    [InlineData("preset", "RID")]
    [InlineData("compiler", "Unsupported C compiler")]
    [InlineData("input", "recipe digest")]
    [InlineData("setting", "Build-info/recipe")]
    public void RecipeChangesFail(string change, string message)
    {
        using var root = new NativeRoot();
        (string binary, JsonObject manifest) = root.Manifest();
        JsonObject recipe = manifest["recipe"]!.AsObject();
        JsonObject configuration = recipe["configuration"]!.AsObject();
        switch (change)
        {
            case "pin": recipe["source"]!["commit"] = new string('0', 40); break;
            case "feature": configuration["CHDR_VERIFY_BLOCK_CRC"] = "OFF"; break;
            case "missing-setting": configuration.Remove("CMAKE_GENERATOR"); break;
            case "preset": configuration["MOONLARK_RID"] = "linux-arm64"; break;
            case "compiler": recipe["toolchain"]!["compilerId"] = "MSVC"; break;
            case "input": recipe["inputSha256"]![BuildRecipe.NativeInputs[0]] = new string('0', 64); break;
            default: configuration["CMAKE_C_FLAGS_RELEASE"] = "-O0"; break;
        }
        Assert.Contains(message, NativeVerify.VerifyManifest(manifest, binary, root.Path)?.Message, StringComparison.Ordinal);
    }

    /// <summary>A changed recipe input file on disk no longer matches the recorded digest.</summary>
    [Fact]
    public void ChangedRecipeInputFileFails()
    {
        using var root = new NativeRoot();
        (string binary, JsonObject manifest) = root.Manifest();
        File.AppendAllText(Path.Combine(root.Path, "native", "libchdr", "CMakePresets.json"), "\n");
        Assert.Contains("recipe digest", NativeVerify.VerifyManifest(manifest, binary, root.Path)?.Message, StringComparison.Ordinal);
    }

    /// <summary>The binary digest and the recipe buildId are distinct identities.</summary>
    [Fact]
    public void BinaryHashAndRecipeBuildIdAreDistinctIdentities()
    {
        using var root = new NativeRoot();
        (string binary, JsonObject manifest) = root.Manifest();
        string buildId = (string)manifest["buildInfo"]!["buildId"]!;
        File.WriteAllText(binary, "different synthetic binary");
        manifest["size"] = new FileInfo(binary).Length;
        manifest["sha256"] = Digest.Sha256File(binary);
        Assert.Null(NativeVerify.VerifyManifest(manifest, binary, root.Path));
        Assert.Equal(buildId, (string)manifest["buildInfo"]!["buildId"]!);
        Assert.NotEqual(buildId, (string)manifest["sha256"]!);
        JsonObject changed = manifest["recipe"]!.DeepClone().AsObject();
        changed["toolchain"]!["compilerVersion"] = "other compiler";
        Assert.NotEqual(buildId, BuildRecipe.BuildId(changed));
    }

    /// <summary>Wrong build-info and missing fields are failures, not crashes.</summary>
    [Fact]
    public void WrongBuildInfoAndMissingFieldsFail()
    {
        using var root = new NativeRoot();
        (string binary, JsonObject manifest) = root.Manifest();
        manifest["buildInfo"]!["abiVersion"] = 2;
        Assert.Contains("Build-info/recipe", NativeVerify.VerifyManifest(manifest, binary, root.Path)?.Message, StringComparison.Ordinal);
        manifest.Remove("sha256");
        Assert.Contains("sha256", NativeVerify.VerifyManifest(manifest, binary, root.Path)?.Message, StringComparison.Ordinal);
    }

    /// <summary>Recorded symbols and dependencies are re-checked against the allowlists.</summary>
    [Fact]
    public void RecordedSymbolsAndDependenciesMustBeAllowlisted()
    {
        using var root = new NativeRoot();
        (string binary, JsonObject manifest) = root.Manifest();
        manifest["symbols"]!.AsArray().Add("ZSTD_decompress");
        Assert.Contains("Export mismatch", NativeVerify.VerifyManifest(manifest, binary, root.Path)?.Message, StringComparison.Ordinal);
        (binary, manifest) = root.Manifest();
        manifest["dependencies"] = JsonFields.Array(["libzstd.so.1"]);
        Assert.Contains("dependencies", NativeVerify.VerifyManifest(manifest, binary, root.Path)?.Message, StringComparison.Ordinal);
    }
}
