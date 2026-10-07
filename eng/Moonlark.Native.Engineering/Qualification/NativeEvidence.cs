using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Fixtures;
using Moonlark.Native.Engineering.Native;

namespace Moonlark.Native.Engineering.Qualification;

/// <summary>Collects local native build and test evidence, without granting release qualification or attestation.</summary>
internal static class NativeEvidence
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(30);

    private static readonly string[] TestEnvironmentNames =
    [
        "PATH", "HOME", "TMPDIR", "TMP", "TEMP", "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64",
        "DOTNET_CLI_HOME", "NUGET_PACKAGES", "CI", "DOTNET_CLI_TELEMETRY_OPTOUT", "DOTNET_NOLOGO",
    ];

    private static readonly string[] WindowsEnvironmentNames =
    [
        "SYSTEMROOT", "WINDIR", "COMSPEC", "PATHEXT", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "HOMEDRIVE", "HOMEPATH",
        "PROGRAMFILES", "PROGRAMFILES(X86)", "PROGRAMW6432",
    ];

    private static readonly string[] ManagedAssemblyPaths =
    [
        "tests/Moonlark.Libchdr.Tests/bin/Release/net10.0/Moonlark.Libchdr.dll",
        "tests/Moonlark.Libchdr.Tests/bin/Release/net10.0/Moonlark.Libchdr.CorpusRunner.dll",
        "tests/Moonlark.Libchdr.Tests/bin/Release/net10.0/Moonlark.Libchdr.Tests.dll",
        "tests/Moonlark.Native.Engineering.Tests/bin/Release/net10.0/Moonlark.Native.Engineering.dll",
        "tests/Moonlark.Native.Engineering.Tests/bin/Release/net10.0/Moonlark.Native.Engineering.Tests.dll",
        "eng/Moonlark.Native.Engineering/bin/Release/net10.0/Moonlark.Native.Engineering.dll",
    ];

    /// <summary>Only reviewed process essentials reach MSBuild and VSTest; arbitrary ambient properties are excluded.</summary>
    internal static Result<IReadOnlyDictionary<string, string>> TestEnvironment(IReadOnlyDictionary<string, string> environment, bool windows = false)
    {
        Result<IReadOnlyDictionary<string, string>> selected = windows ? BuildEnvironment.WindowsNames(environment) : new Dictionary<string, string>(environment, StringComparer.Ordinal);
        if (!selected.Succeeded) return selected.Failure;
        string[] names = windows ? [.. TestEnvironmentNames, .. WindowsEnvironmentNames] : TestEnvironmentNames;
        return names.Where(selected.Value.ContainsKey).ToDictionary(name => name, name => selected.Value[name],
            windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    /// <summary>Fingerprints the actual output copies used by the test hosts, corpus child and engineering CLI.</summary>
    internal static Result<JsonObject> ManagedAssemblies(string root)
    {
        var result = new JsonObject();
        foreach (string relative in ManagedAssemblyPaths)
        {
            string path = Path.Combine(root, relative);
            if (ArtifactsPath.IsLink(path) || !ArtifactsPath.IsRegularFile(path)) return new Failure("Missing or nonregular managed test assembly: " + relative);
            result[relative] = Digest.Sha256File(path);
        }
        return result;
    }

    internal static Failure? CheckManagedAssemblies(string root, JsonObject expected)
    {
        Result<JsonObject> actual = ManagedAssemblies(root);
        return !actual.Succeeded ? actual.Failure
            : Check.That(JsonFields.SameCanonical(actual.Value, expected), "Managed test assemblies changed during evidence collection");
    }

    private sealed record BuiltOutput(string ManifestPath, string BinaryPath, JsonObject Manifest, byte[] Bytes);

    internal sealed record Session(string Root, string Directory, IReadOnlyDictionary<string, string> Environment, TextWriter? Log)
    {
        internal JsonArray Steps { get; } = [];

        internal Result<T> NativeStep<T>(string name, Func<TextWriter, Result<T>> run)
        {
            string path = Path.Combine(Directory, name + ".log");
            if (ArtifactsPath.ValidateLogFile(path) is { } invalid) return invalid;
            Log?.WriteLine("Running " + name + "; log: " + path);
            Result<T> result;
            using (var writer = new StreamWriter(path, append: true))
            {
                result = run(writer);
                writer.WriteLine(result.Succeeded ? "step succeeded" : "step failed: " + result.Failure.Message);
            }
            Steps.Add(new JsonObject
            {
                ["name"] = name, ["succeeded"] = result.Succeeded,
                ["log"] = Path.GetRelativePath(Root, path), ["logSha256"] = Digest.Sha256File(path),
            });
            return result;
        }

        internal Result<string> Command(string name, params string[] command)
        {
            Result<IReadOnlyDictionary<string, string>> selected = TestEnvironment(Environment, OperatingSystem.IsWindows());
            if (!selected.Succeeded) return selected.Failure;
            IReadOnlyDictionary<string, string> environment = selected.Value;
            string path = Path.Combine(Directory, name + ".log");
            if (ArtifactsPath.ValidateLogFile(path) is { } invalid) return invalid;
            Log?.WriteLine("Running " + name + "; log: " + path);
            ProcessOutput output;
            var recordedEnvironment = new JsonObject(environment.Select(pair => KeyValuePair.Create(pair.Key, (JsonNode?)pair.Value)));
            using (var writer = new StreamWriter(path, append: true))
            {
                writer.WriteLine("environment=" + JsonFields.Compact(recordedEnvironment));
                output = ProcessRunner.Execute(command, environment, writer, Root, CommandTimeout);
            }
            var step = new JsonObject
            {
                ["name"] = name, ["command"] = JsonFields.Array(command), ["exitCode"] = output.ExitCode,
                ["timedOut"] = output.TimedOut, ["log"] = Path.GetRelativePath(Root, path), ["logSha256"] = Digest.Sha256File(path),
                ["environment"] = recordedEnvironment,
            };
            Steps.Add(step);
            return CommandFailure(name, output) is { } failure ? failure : output.Stdout.Trim();
        }
    }

    /// <summary>Runs inside the caller's approved builder, with the solution already restored and built.</summary>
    internal static Result<JsonObject> Run(string root, string rid, IReadOnlyDictionary<string, string> environment, TextWriter? log)
    {
        if (Check.That(rid is "linux-x64" or "linux-arm64" or "win-x64", "Native evidence requires linux-x64, linux-arm64 or win-x64") is { } unsupported) return unsupported;
        Result<string> directory = ArtifactsPath.ValidateDirectory(Path.Combine(root, "artifacts", "qualification", rid), root);
        if (!directory.Succeeded) return directory.Failure;
        string receipt = Path.Combine(directory.Value, "evidence.json");
        NativeOutput.DeleteFile(receipt);
        if (ValidateHost(rid, OperatingSystem.IsLinux() ? "Linux" : OperatingSystem.IsWindows() ? "Windows" : RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture, RuntimeInformation.ProcessArchitecture) is { } host) return host;
        Result<IReadOnlyDictionary<string, string>> cleanEnvironment = BuildEnvironment.Create(environment);
        if (!cleanEnvironment.Succeeded) return cleanEnvironment.Failure;
        Result<string> runDirectory = ArtifactsPath.ValidateDirectory(Path.Combine(directory.Value, "runs", Guid.NewGuid().ToString("N")), root);
        if (!runDirectory.Succeeded) return runDirectory.Failure;
        Directory.CreateDirectory(runDirectory.Value);
        var session = new Session(Path.GetFullPath(root), runDirectory.Value, cleanEnvironment.Value, log);
        Result<JsonObject> evidence = Collect(session, rid);
        if (!evidence.Succeeded)
        {
            log?.WriteLine("Native evidence failed: " + evidence.Failure.Message);
            return evidence.Failure;
        }
        File.WriteAllBytes(receipt, JsonFields.Serialize(evidence.Value, sortKeys: true));
        return evidence.Value;
    }

    private static Result<JsonObject> Collect(Session session, string rid)
    {
        Result<string> commit = SourceCommit(session, "source-before");
        if (!commit.Succeeded) return commit.Failure;
        Result<JsonObject> fixtures = VerifyFixtures(session, "fixtures-before");
        if (!fixtures.Succeeded) return fixtures.Failure;
        Result<JsonObject> managed = ManagedAssemblies(session.Root);
        if (!managed.Succeeded) return managed.Failure;
        Result<BuiltOutput> first = Build(session, rid, rid, "first");
        if (!first.Succeeded) return first.Failure;
        Result<BuiltOutput> second = Build(session, rid, "repro-" + rid, "second");
        if (!second.Succeeded) return second.Failure;
        if (Compare(rid, first.Value.Manifest, first.Value.Bytes, second.Value.Manifest, second.Value.Bytes) is { } comparison) return comparison;
        if (RunTests(session) is { } tests) return tests;
        Result<JsonObject> fixturesAfter = VerifyFixtures(session, "fixtures-after");
        if (!fixturesAfter.Succeeded) return fixturesAfter.Failure;
        if (Check.That(JsonFields.SameCanonical(fixtures.Value, fixturesAfter.Value), "Fixtures changed during evidence collection") is { } fixtureChange) return fixtureChange;
        Result<string> after = SourceCommit(session, "source-after");
        if (!after.Succeeded) return after.Failure;
        if (Check.That(commit.Value == after.Value, "Source commit changed during evidence collection") is { } moved) return moved;
        if (Unchanged(first.Value) is { } firstChanged) return firstChanged;
        if (Unchanged(second.Value) is { } secondChanged) return secondChanged;
        if (CheckManagedAssemblies(session.Root, managed.Value) is { } managedChanged) return managedChanged;
        return new JsonObject
        {
            ["schemaVersion"] = 1, ["rid"] = rid, ["sourceCommit"] = commit.Value,
            ["osDescription"] = RuntimeInformation.OSDescription, ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString(),
            ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(), ["emulation"] = "undetermined",
            ["qualification"] = "local-unqualified", ["attestation"] = null,
            ["binarySha256"] = Digest.Sha256(first.Value.Bytes), ["binaryBytes"] = first.Value.Bytes.Length,
            ["recipe"] = first.Value.Manifest["recipe"]!.DeepClone(), ["buildInfo"] = first.Value.Manifest["buildInfo"]!.DeepClone(),
            ["fixtures"] = fixtures.Value,
            ["managedAssemblySha256"] = managed.Value, ["managedBuildProvenance"] = "caller-required",
            ["manifests"] = JsonFields.Array([Path.GetRelativePath(session.Root, first.Value.ManifestPath), Path.GetRelativePath(session.Root, second.Value.ManifestPath)]),
            ["steps"] = session.Steps,
        };
    }

    private static Result<JsonObject> VerifyFixtures(Session session, string name) => session.NativeStep<JsonObject>(name, _ =>
    {
        string directory = Path.Combine(session.Root, "artifacts", "fixtures", "libchdr-final");
        Result<JsonObject> verified = FixtureVerification.Verify(session.Root, directory);
        return verified.Succeeded
            ? new JsonObject { ["manifestSha256"] = Digest.Sha256File(Path.Combine(directory, FixtureGenerator.ManifestName)), ["provenance"] = verified.Value }
            : verified.Failure;
    });

    private static Result<string> SourceCommit(Session session, string name)
    {
        Result<string> status = session.Command(name + "-status", "git", "status", "--porcelain=v1", "--untracked-files=all");
        if (!status.Succeeded) return status.Failure;
        if (Check.That(status.Value.Length == 0, "Evidence requires clean tracked and untracked source: " + status.Value) is { } dirty) return dirty;
        Result<string> commit = session.Command(name + "-commit", "git", "rev-parse", "--verify", "HEAD");
        if (!commit.Succeeded) return commit.Failure;
        return Check.That(commit.Value.Length == 40 && commit.Value.All(char.IsAsciiHexDigit), "Invalid source commit") is { } invalid ? invalid : commit.Value;
    }

    private static Result<BuiltOutput> Build(Session session, string rid, string outputName, string name)
    {
        string source = Path.Combine(session.Root, "native", "libchdr", "upstream");
        var request = new NativeBuildRequest(session.Root, rid, source,
            Path.Combine(session.Root, "artifacts", "native", "libchdr", outputName), rid == "win-x64" ? "cl" : "clang", session.Environment);
        Result<NativeBuildResult> build = session.NativeStep("build-" + name, writer => NativeBuild.Run(request, writer));
        if (!build.Succeeded) return build.Failure;
        Result<string> verify = session.NativeStep("verify-" + name,
            writer => NativeVerify.Run(session.Root, build.Value.Manifest, source, session.Environment, writer));
        if (!verify.Succeeded) return verify.Failure;
        Result<JsonObject> manifest = JsonFields.ReadObject(build.Value.Manifest, "Native manifest");
        if (!manifest.Succeeded) return manifest.Failure;
        Result<byte[]> bytes = ReadBinary(verify.Value);
        return bytes.Succeeded ? new BuiltOutput(build.Value.Manifest, verify.Value, manifest.Value, bytes.Value) : bytes.Failure;
    }

    private static Failure? RunTests(Session session)
    {
        foreach (string name in (string[])["libchdr-first", "libchdr-second", "engineering"])
        {
            string project = name == "engineering" ? "Moonlark.Native.Engineering.Tests" : "Moonlark.Libchdr.Tests";
            string results = Path.Combine(session.Directory, name + ".trx");
            NativeOutput.DeleteFile(results);
            Result<string> test = session.Command(name, "dotnet", "test", "tests/" + project + "/" + project + ".csproj",
                "-c", "Release", "--no-build", "--no-restore", "--logger", "trx;LogFileName=" + name + ".trx",
                "--results-directory", session.Directory);
            if (!test.Succeeded) return test.Failure;
            if (!File.Exists(results) || ArtifactsPath.IsLink(results)) return new Failure("Test run did not produce regular results: " + name);
            Result<JsonObject> counts = TestResults(XDocument.Load(results));
            if (!counts.Succeeded) return counts.Failure;
            session.Steps.Add(new JsonObject
            {
                ["name"] = name + "-results", ["counters"] = counts.Value,
                ["file"] = Path.GetRelativePath(session.Root, results), ["sha256"] = Digest.Sha256File(results),
            });
        }
        Result<string> check = session.Command("repo-check", "dotnet", "run", "--project", "eng/Moonlark.Native.Engineering",
            "-c", "Release", "--no-build", "--no-restore", "--", "repo", "check");
        return check.Succeeded ? null : check.Failure;
    }

    private static Failure? Unchanged(BuiltOutput output)
    {
        Result<byte[]> bytes = ReadBinary(output.BinaryPath);
        if (!bytes.Succeeded) return bytes.Failure;
        if (Check.That(bytes.Value.AsSpan().SequenceEqual(output.Bytes), "Native output changed during tests") is { } binary) return binary;
        Result<JsonObject> manifest = JsonFields.ReadObject(output.ManifestPath, "Native manifest");
        if (!manifest.Succeeded) return manifest.Failure;
        return Check.That(JsonFields.SameCanonical(manifest.Value, output.Manifest), "Native manifest changed during tests");
    }

    /// <summary>Compares measured bytes and their manifest claims, not just the claims from two builds.</summary>
    internal static Failure? Compare(string rid, JsonObject first, ReadOnlySpan<byte> firstBytes,
        JsonObject second, ReadOnlySpan<byte> secondBytes)
    {
        if (Check.That(rid is "linux-x64" or "linux-arm64" or "win-x64"
            && JsonFields.RequireString(first, "rid") is { Succeeded: true } firstRid && firstRid.Value == rid
            && JsonFields.RequireString(second, "rid") is { Succeeded: true } secondRid && secondRid.Value == rid,
            "Reproducibility output RID mismatch") is { } identity) return identity;
        if (Check.That(!firstBytes.IsEmpty && firstBytes.SequenceEqual(secondBytes), "Reproducibility binary bytes differ or are empty") is { } bytes) return bytes;
        string digest = Digest.Sha256(firstBytes);
        if (Check.That(JsonFields.RequireString(first, "sha256") is { Succeeded: true } firstDigest && firstDigest.Value == digest
            && JsonFields.RequireString(second, "sha256") is { Succeeded: true } secondDigest && secondDigest.Value == digest,
            "Reproducibility binary digest claim differs from actual bytes") is { } hashes) return hashes;
        foreach (string name in (string[])["recipe", "buildInfo"])
            if (Check.That(first[name] is JsonObject && second[name] is JsonObject && JsonFields.SameCanonical(first[name], second[name]),
                "Reproducibility " + name + " mismatch or missing") is { } inputs) return inputs;
        return null;
    }

    internal static Failure? ValidateHost(string rid, string system, Architecture os, Architecture process) =>
        Check.That((system, rid, os, process) is ("Linux", "linux-x64", Architecture.X64, Architecture.X64)
            or ("Linux", "linux-arm64", Architecture.Arm64, Architecture.Arm64)
            or ("Windows", "win-x64", Architecture.X64, Architecture.X64), "Native evidence requires matching OS and process architecture");

    internal static Failure? CommandFailure(string name, ProcessOutput output) =>
        output.TimedOut ? new Failure("Evidence command timed out: " + name)
            : output.ExitCode != 0 ? new Failure($"Evidence command failed ({output.ExitCode}): {name}\n{output.Combined}") : null;

    internal static Result<byte[]> ReadBinary(string path) =>
        !ArtifactsPath.IsLink(path) && ArtifactsPath.IsRegularFile(path) ? File.ReadAllBytes(path) : new Failure("Missing or nonregular native output: " + path);

    /// <summary>Requires actual executed passing tests; exit zero alone does not establish a suite pass.</summary>
    internal static Result<JsonObject> TestResults(XDocument document)
    {
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        XElement[] summaries = document.Root?.Elements(ns + "ResultSummary").ToArray() ?? [];
        XElement[] counters = summaries.Length == 1 ? summaries[0].Elements(ns + "Counters").ToArray() : [];
        if (Check.That(document.Root?.Name == ns + "TestRun" && summaries.Length == 1
            && summaries[0].Attribute("outcome")?.Value == "Completed" && counters.Length == 1,
            "Test results must contain one completed run and its counters") is { } format) return format;
        var values = new JsonObject();
        foreach (XAttribute attribute in counters[0].Attributes())
        {
            if (!long.TryParse(attribute.Value, NumberStyles.None, CultureInfo.InvariantCulture, out long value))
                return new Failure("Invalid test counter: " + attribute.Name);
            values[attribute.Name.LocalName] = value;
        }
        Result<long> total = JsonFields.RequireInteger(values, "total");
        Result<long> executed = JsonFields.RequireInteger(values, "executed");
        Result<long> passed = JsonFields.RequireInteger(values, "passed");
        Result<long> failed = JsonFields.RequireInteger(values, "failed");
        if (Check.That(total.Succeeded && total.Value > 0 && executed.Succeeded && executed.Value == total.Value
            && passed.Succeeded && passed.Value == total.Value && failed.Succeeded && failed.Value == 0
            && values.Where(pair => pair.Key is not ("total" or "executed" or "passed")).All(pair => pair.Value!.GetValue<long>() == 0),
            "Test results require a nonempty suite with every test executed and passed") is { } incomplete) return incomplete;
        return values;
    }
}
