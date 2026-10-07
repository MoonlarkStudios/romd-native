using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Packaging;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Packaging;

/// <summary>Consumer acceptance requires the exact two restored packages and actual verified native identity/integrity.</summary>
public sealed class ConsumerEvidenceTests
{
    private const string Version = "1.0.0-preview.1";
    private static readonly string[] Ids = ["Moonlark.Libchdr", "Moonlark.Libchdr.Native"];
    private static Dictionary<string, string> Hashes() => Ids.ToDictionary(id => id, id => "sha512-" + id, StringComparer.Ordinal);
    private static JsonObject Assets() => new() { ["libraries"] = new JsonObject(Ids.Select(id => KeyValuePair.Create(id + "/" + Version,
        (JsonNode?)new JsonObject { ["type"] = "package", ["sha512"] = "sha512-" + id }))) };
    private static JsonObject Result() => new()
    {
        ["IntegrityVerified"] = true,
        ["BuildInfo"] = new JsonObject { ["FamilyVersion"] = Version, ["UpstreamCommit"] = new string('a', 40), ["BuildId"] = new string('b', 64),
            ["HasRawSectors"] = true, ["HasSubcode"] = true, ["VerifiesBlockCrc"] = true },
    };

    /// <summary>Complete exact evidence is accepted.</summary>
    [Fact]
    public void ExactConsumerEvidencePasses()
    {
        Assert.Null(ConsumerEvidence.VerifyAssets(Assets(), Version, Hashes()));
        Assert.Null(ConsumerEvidence.VerifyResult(Result(), Version, new string('a', 40), new string('b', 64)));
    }

    /// <summary>Missing, additional, project or wrong-version/hash packages do not prove a clean package-only consumer.</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("project")]
    [InlineData("version")]
    [InlineData("hash")]
    public void WrongRestoreEvidenceFails(string change)
    {
        JsonObject assets = Assets(); JsonObject libraries = assets["libraries"]!.AsObject();
        string key = "Moonlark.Libchdr.Native/" + Version;
        if (change == "missing") libraries.Remove(key);
        else if (change == "extra") libraries["Other/1.0.0"] = new JsonObject { ["type"] = "package" };
        else if (change == "project") libraries[key]!["type"] = "project";
        else if (change == "hash") libraries[key]!["sha512"] = "another-hash";
        else { JsonNode? old = libraries[key]; libraries.Remove(key); libraries["Moonlark.Libchdr.Native/9.0.0"] = old; }
        Assert.NotNull(ConsumerEvidence.VerifyAssets(assets, Version, Hashes()));
    }

    /// <summary>A successful process alone cannot replace full native identity and integrity evidence.</summary>
    [Theory]
    [InlineData("IntegrityVerified")]
    [InlineData("FamilyVersion")]
    [InlineData("UpstreamCommit")]
    [InlineData("BuildId")]
    [InlineData("HasRawSectors")]
    [InlineData("HasSubcode")]
    [InlineData("VerifiesBlockCrc")]
    public void MissingRuntimeEvidenceFails(string field)
    {
        JsonObject result = Result();
        if (field == "IntegrityVerified") result.Remove(field); else result["BuildInfo"]!.AsObject().Remove(field);
        Assert.NotNull(ConsumerEvidence.VerifyResult(result, Version, new string('a', 40), new string('b', 64)));
    }
}
