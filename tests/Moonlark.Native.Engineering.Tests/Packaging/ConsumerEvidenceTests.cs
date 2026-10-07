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
    /// <summary>NuGet's RID group is a delta over the complete framework group, not a second full inventory.</summary>
    [Theory]
    [InlineData("native")]
    [InlineData("empty")]
    [InlineData("full")]
    public void ExactLockDeltaPasses(string shape)
    {
        JsonObject document = Lock();
        JsonObject groups = document["dependencies"]!.AsObject();
        if (shape == "empty") groups["net10.0/osx-arm64"] = new JsonObject();
        if (shape == "full") groups["net10.0/osx-arm64"] = groups["net10.0"]!.DeepClone();
        Assert.Null(ConsumerEvidence.VerifyLock(document, Version, "osx-arm64", Hashes()));
    }

    /// <summary>Only the exact framework and requested RID can preserve the two family's verified identities.</summary>
    [Theory]
    [InlineData("missing-base")]
    [InlineData("missing-rid")]
    [InlineData("unknown-group")]
    [InlineData("wrong-rid")]
    [InlineData("base-extra")]
    [InlineData("base-missing")]
    [InlineData("base-hash")]
    [InlineData("base-version")]
    [InlineData("base-type")]
    [InlineData("rid-extra")]
    [InlineData("rid-hash")]
    [InlineData("rid-version")]
    [InlineData("rid-type")]
    public void InvalidLockDeltaFails(string change)
    {
        JsonObject document = Lock();
        JsonObject groups = document["dependencies"]!.AsObject();
        JsonObject basis = groups["net10.0"]!.AsObject(), delta = groups["net10.0/osx-arm64"]!.AsObject();
        switch (change)
        {
            case "missing-base": groups.Remove("net10.0"); break;
            case "missing-rid": groups.Remove("net10.0/osx-arm64"); break;
            case "unknown-group": groups["net9.0"] = basis.DeepClone(); break;
            case "wrong-rid": groups.Remove("net10.0/osx-arm64"); groups["net10.0/linux-x64"] = basis.DeepClone(); break;
            case "base-extra": basis["Other"] = basis[Ids[0]]!.DeepClone(); break;
            case "base-missing": basis.Remove(Ids[0]); break;
            case "base-hash": basis[Ids[0]]!["contentHash"] = "wrong"; break;
            case "base-version": basis[Ids[0]]!["resolved"] = "9.0.0"; break;
            case "base-type": basis[Ids[0]]!["type"] = "Project"; break;
            case "rid-extra": delta["Other"] = delta[Ids[1]]!.DeepClone(); break;
            case "rid-hash": delta[Ids[1]]!["contentHash"] = "wrong"; break;
            case "rid-version": delta[Ids[1]]!["resolved"] = "9.0.0"; break;
            case "rid-type": delta[Ids[1]]!["type"] = "Project"; break;
            default: throw new InvalidOperationException(change);
        }
        Assert.NotNull(ConsumerEvidence.VerifyLock(document, Version, "osx-arm64", Hashes()));
    }

    private static JsonObject Lock()
    {
        var basis = new JsonObject(Ids.Select(id => KeyValuePair.Create(id, (JsonNode?)new JsonObject
        { ["type"] = id == Ids[0] ? "Direct" : "Transitive", ["resolved"] = Version, ["contentHash"] = Hashes()[id] })));
        basis[Ids[0]]!["requested"] = "[" + Version + ", " + Version + "]";
        basis[Ids[0]]!["dependencies"] = new JsonObject { [Ids[1]] = "[" + Version + "]" };
        return new JsonObject { ["version"] = 1, ["dependencies"] = new JsonObject
        { ["net10.0"] = basis, ["net10.0/osx-arm64"] = new JsonObject { [Ids[1]] = basis[Ids[1]]!.DeepClone() } } };
    }

}
