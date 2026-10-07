using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Core;

namespace Moonlark.Native.Engineering.Packaging;

/// <summary>Checks actual restored package identities and the fresh consumer process's identity/integrity result.</summary>
internal static class ConsumerEvidence
{
    internal static Failure? VerifyAssets(JsonObject assets, string version, IReadOnlyDictionary<string, string> packageHashes)
    {
        if (assets["libraries"] is not JsonObject libraries || libraries.Count != 2 || packageHashes.Count != 2)
            return new Failure("Consumer restore must contain exactly two family packages");
        foreach (string id in (string[])["Moonlark.Libchdr", "Moonlark.Libchdr.Native"])
        {
            if (libraries[id + "/" + version] is not JsonObject item || (string?)item["type"] != "package"
                || !packageHashes.TryGetValue(id, out string? expected) || (string?)item["sha512"] != expected)
                return new Failure("Consumer restored a missing, project, wrong-family or different package: " + id);
        }
        return null;
    }

    internal static Failure? VerifyLock(JsonObject document, string version, string rid, IReadOnlyDictionary<string, string> hashes)
    {
        if ((int?)document["version"] != 1 || !NativeRids.IsSupported(rid)
            || hashes.Count != 2 || !hashes.ContainsKey("Moonlark.Libchdr") || !hashes.ContainsKey("Moonlark.Libchdr.Native")
            || document["dependencies"] is not JsonObject targets || targets.Count != 2
            || targets["net10.0"] is not JsonObject basis || basis.Count != 2
            || targets["net10.0/" + rid] is not JsonObject delta)
            return new Failure("Consumer lock must contain the exact framework and requested RID groups");
        // RID entries override the framework group. Every override must preserve the already verified family identity.
        foreach (JsonObject group in new[] { basis, delta })
            foreach ((string id, JsonNode? value) in group)
                if (!hashes.TryGetValue(id, out string? expected) || value is not JsonObject item
                    || (string?)item["resolved"] != version || (string?)item["contentHash"] != expected
                    || (string?)item["type"] != (id == "Moonlark.Libchdr" ? "Direct" : "Transitive"))
                    return new Failure("Consumer lock family/hash/type mismatch");
        return null;
    }

    internal static Failure? VerifyResult(JsonObject result, string version, string upstream, string buildId) =>
        Check.That((bool?)result["IntegrityVerified"] == true && result["BuildInfo"] is JsonObject info
            && (string?)info["FamilyVersion"] == version && (string?)info["UpstreamCommit"] == upstream && (string?)info["BuildId"] == buildId
            && (bool?)info["HasRawSectors"] == true && (bool?)info["HasSubcode"] == true && (bool?)info["VerifiesBlockCrc"] == true,
            "Consumer did not prove exact native identity and full CHD integrity");
}
