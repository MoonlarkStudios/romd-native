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

    internal static Failure? VerifyResult(JsonObject result, string version, string upstream, string buildId) =>
        Check.That((bool?)result["IntegrityVerified"] == true && result["BuildInfo"] is JsonObject info
            && (string?)info["FamilyVersion"] == version && (string?)info["UpstreamCommit"] == upstream && (string?)info["BuildId"] == buildId
            && (bool?)info["HasRawSectors"] == true && (bool?)info["HasSubcode"] == true && (bool?)info["VerifiesBlockCrc"] == true,
            "Consumer did not prove exact native identity and full CHD integrity");
}
