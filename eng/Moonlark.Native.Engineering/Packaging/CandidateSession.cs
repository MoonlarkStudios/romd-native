using System.Globalization;
using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Native;
using Moonlark.Native.Engineering.Qualification;

namespace Moonlark.Native.Engineering.Packaging;

/// <summary>Scoped package/consumer commands and their raw receipts, using the existing sanitized process environment.</summary>
internal sealed class CandidateSession(string root, string directory, IReadOnlyDictionary<string, string> environment, TextWriter? log)
{
    internal JsonArray Steps { get; } = [];
    internal string Root { get; } = root;
    internal IReadOnlyDictionary<string, string> Environment { get; } = SelectEnvironment(environment);

    private static Dictionary<string, string> SelectEnvironment(IReadOnlyDictionary<string, string> environment)
    {
        var selected = new Dictionary<string, string>(NativeEvidence.TestEnvironment(environment, OperatingSystem.IsWindows()).Value,
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (environment.TryGetValue("NUGET_HTTP_CACHE_PATH", out string? cache)) selected["NUGET_HTTP_CACHE_PATH"] = cache;
        return selected;
    }

    internal ProcessOutput Execute(string name, string workingDirectory, params string[] command)
    {
        string path = Path.Combine(directory, "logs", name + ".log");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (Path.Exists(path) || ArtifactsPath.IsLink(path)) throw new InvalidDataException("Command log already exists");
        log?.WriteLine("Running " + name + "; log: " + path);
        ProcessOutput output;
        using (var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)))
        {
            writer.WriteLine("environment=" + JsonFields.Compact(new JsonObject(Environment.Select(pair => KeyValuePair.Create(pair.Key, (JsonNode?)pair.Value)))));
            output = ProcessRunner.Execute(command, Environment, writer, workingDirectory, TimeSpan.FromMinutes(15));
        }
        Steps.Add(new JsonObject { ["name"] = name, ["command"] = JsonFields.Array(command), ["workingDirectory"] = workingDirectory,
            ["exitCode"] = output.ExitCode, ["timedOut"] = output.TimedOut, ["log"] = path, ["sha256"] = Digest.Sha256File(path) });
        return output;
    }

    internal string Run(string name, string workingDirectory, params string[] command)
    {
        ProcessOutput output = Execute(name, workingDirectory, command);
        if (output.TimedOut || output.ExitCode != 0) throw new InvalidDataException("Candidate command failed: " + name + " (" + output.ExitCode.ToString(CultureInfo.InvariantCulture) + ")");
        return output.Stdout.Trim();
    }

    internal string Source(string name, string? expected = null)
    {
        string status = Run(name + "-status", Root, "git", "status", "--porcelain=v1", "--untracked-files=all");
        string commit = Run(name + "-head", Root, "git", "rev-parse", "HEAD");
        if (status.Length != 0 || !Authorities.Sha1().IsMatch(commit) || (expected is not null && expected != commit))
            throw new InvalidDataException("Candidate operation requires the original clean source commit");
        return commit;
    }
}
