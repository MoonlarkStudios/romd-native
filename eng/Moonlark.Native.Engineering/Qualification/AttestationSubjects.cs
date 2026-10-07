using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Native;

namespace Moonlark.Native.Engineering.Qualification;

/// <summary>Checks a complete downloaded Linux subject set without executing it, writing files or signing anything.</summary>
internal static class AttestationSubjects
{
    private static readonly string[] Rids = ["linux-x64", "linux-arm64"];
    private static readonly string[] Names = ["libmoonlark_chdr.so", "build-manifest.json", "evidence.json", "builder.json"];

    /// <summary>The caller must obtain these artifacts from the intended workflow run; metadata alone cannot prove run identity.</summary>
    internal static Result<string> Create(string root, string expectedCommit)
    {
        if (Check.That(expectedCommit.Length == 40 && expectedCommit.All(char.IsAsciiHexDigit), "Expected wrapper commit must contain exactly 40 hexadecimal digits") is { } commit)
            return commit;
        try
        {
            string resolvedRoot = ArtifactsPath.Resolve(root);
            var lines = new List<string>(8);
            foreach (string rid in Rids)
            {
                Result<IReadOnlyDictionary<string, byte[]>> subjects = ReadSubjects(resolvedRoot, rid);
                if (!subjects.Succeeded) return subjects.Failure;
                if (Validate(subjects.Value, rid, expectedCommit) is { } failure) return failure;
                lines.AddRange(Names.Select(name => Digest.Sha256(subjects.Value[name]) + "  " + rid + "/" + name));
            }
            return string.Join('\n', lines) + "\n";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new Failure("Cannot read attestation subjects: " + exception.Message);
        }
    }

    private static Result<IReadOnlyDictionary<string, byte[]>> ReadSubjects(string root, string rid)
    {
        var subjects = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string name in Names)
        {
            string path = SubjectPath(root, rid, name);
            Result<string> directory = ArtifactsPath.ValidateDirectory(Path.GetDirectoryName(path)!, root);
            if (!directory.Succeeded) return directory.Failure;
            if (Check.That(!ArtifactsPath.IsLink(path) && ArtifactsPath.IsRegularFile(path), "Missing or nonregular attestation subject: " + rid + "/" + name) is { } file)
                return file;
            subjects.Add(name, File.ReadAllBytes(path));
        }
        return subjects;
    }

    private static string SubjectPath(string root, string rid, string name)
    {
        string directory = Path.Combine(root, "artifacts", "signing", rid);
        return name switch
        {
            "builder.json" => Path.Combine(directory, name),
            "evidence.json" => Path.Combine(directory, "checkout", "artifacts", "qualification", rid, name),
            "build-manifest.json" => Path.Combine(directory, "checkout", "artifacts", "native", "libchdr", rid, name),
            _ => Path.Combine(directory, "checkout", "artifacts", "native", "libchdr", rid, "native", name),
        };
    }

    private static Failure? Validate(IReadOnlyDictionary<string, byte[]> subjects, string rid, string expectedCommit)
    {
        byte[] binary = subjects["libmoonlark_chdr.so"];
        string digest = Digest.Sha256(binary);
        foreach (string name in Names.Skip(1))
        {
            Result<JsonObject> parsed = JsonFields.ParseObject(subjects[name], rid + "/" + name);
            if (!parsed.Succeeded) return parsed.Failure;
            JsonObject document = parsed.Value;
            if (Identity(document, rid, name) is { } identity) return identity;
            if (name is "builder.json" or "evidence.json")
            {
                Result<string> source = JsonFields.RequireString(document, "sourceCommit");
                if (Check.That(source.Succeeded && string.Equals(source.Value, expectedCommit, StringComparison.OrdinalIgnoreCase),
                    "Wrapper source commit mismatch: " + rid + "/" + name) is { } commit) return commit;
            }
            if (name != "builder.json" && BinaryClaim(document, name, digest, binary.LongLength) is { } claim) return claim;
        }
        return null;
    }

    private static Failure? Identity(JsonObject document, string rid, string name)
    {
        Result<string> recordedRid = JsonFields.RequireString(document, "rid");
        if (Check.That(recordedRid.Succeeded && recordedRid.Value == rid, "Subject RID mismatch: " + rid + "/" + name) is { } identity) return identity;
        Result<string> qualification = JsonFields.RequireString(document, "qualification");
        return Check.That(qualification.Succeeded && qualification.Value == "local-unqualified"
            && document.TryGetPropertyValue("attestation", out JsonNode? attestation) && attestation is null,
            "Subject must remain locally unqualified without an attestation: " + rid + "/" + name);
    }

    private static Failure? BinaryClaim(JsonObject document, string name, string digest, long size)
    {
        bool evidence = name == "evidence.json";
        Result<string> sha256 = JsonFields.RequireString(document, evidence ? "binarySha256" : "sha256");
        Result<long> bytes = JsonFields.RequireInteger(document, evidence ? "binaryBytes" : "size");
        return Check.That(size > 0 && sha256.Succeeded && sha256.Value == digest && bytes.Succeeded && bytes.Value == size,
            "Native binary digest/size mismatch: " + name);
    }
}
