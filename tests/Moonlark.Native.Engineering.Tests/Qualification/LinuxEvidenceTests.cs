using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Qualification;
using Moonlark.Native.Engineering.Tests.TestSupport;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Qualification;

/// <summary>Rejection rules for local Linux evidence; synthetic bytes are never native qualification.</summary>
public sealed class LinuxEvidenceTests
{
    /// <summary>Matching claimed hashes cannot hide different bytes from the two builds.</summary>
    [Fact]
    public void DifferentActualBytesRejectMatchingClaims()
    {
        JsonObject first = Manifest("first"u8);
        Assert.NotNull(LinuxEvidence.Compare("linux-x64", first, "first"u8, first.DeepClone().AsObject(), "other"u8));
    }

    /// <summary>Identical bytes still need matching valid digest claims.</summary>
    [Fact]
    public void EqualBytesRejectFalseDigestClaims()
    {
        JsonObject first = Manifest("binary"u8);
        first["sha256"] = new string('0', 64);
        Assert.NotNull(LinuxEvidence.Compare("linux-x64", first, "binary"u8, first.DeepClone().AsObject(), "binary"u8));
    }

    /// <summary>Reproducibility includes effective recipe and compiled build identity.</summary>
    [Theory]
    [InlineData("recipe")]
    [InlineData("buildInfo")]
    public void DifferentBuildInputsRejectEqualBytes(string field)
    {
        JsonObject first = Manifest("binary"u8);
        JsonObject second = first.DeepClone().AsObject();
        second[field]!["identity"] = "different";
        Assert.NotNull(LinuxEvidence.Compare("linux-x64", first, "binary"u8, second, "binary"u8));
    }

    /// <summary>Missing build identities are not two equal valid identities.</summary>
    [Theory]
    [InlineData("recipe")]
    [InlineData("buildInfo")]
    public void MissingBuildInputsAreRejected(string field)
    {
        JsonObject first = Manifest("binary"u8);
        first.Remove(field);
        Assert.NotNull(LinuxEvidence.Compare("linux-x64", first, "binary"u8, first.DeepClone().AsObject(), "binary"u8));
    }

    /// <summary>Both outputs must belong to the requested Linux RID.</summary>
    [Theory]
    [InlineData("linux-arm64")]
    [InlineData("osx-arm64")]
    [InlineData("")]
    public void IncorrectManifestRidIsRejected(string rid)
    {
        JsonObject first = Manifest("binary"u8);
        JsonObject second = first.DeepClone().AsObject();
        second["rid"] = rid;
        Assert.NotNull(LinuxEvidence.Compare("linux-x64", first, "binary"u8, second, "binary"u8));
    }

    /// <summary>A missing output fails before evidence can consume it.</summary>
    [Fact]
    public void MissingOutputIsFailure()
    {
        using var directory = new TemporaryDirectory();
        Assert.False(LinuxEvidence.ReadBinary(Path.Combine(directory.Path, "missing.so")).Succeeded);
    }

    /// <summary>Host and process architecture must both match the requested RID.</summary>
    [Theory]
    [InlineData("linux-x64", "Darwin", Architecture.X64, Architecture.X64)]
    [InlineData("linux-x64", "Linux", Architecture.Arm64, Architecture.X64)]
    [InlineData("linux-arm64", "Linux", Architecture.Arm64, Architecture.X64)]
    [InlineData("osx-arm64", "Darwin", Architecture.Arm64, Architecture.Arm64)]
    public void IncorrectHostIsRejected(string rid, string system, Architecture os, Architecture process) =>
        Assert.NotNull(LinuxEvidence.ValidateHost(rid, system, os, process));

    /// <summary>A timeout is failure even if a process supplied zero as its exit code.</summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    public void CommandFailuresCannotBecomeSuccess(int exitCode, bool timedOut) =>
        Assert.NotNull(LinuxEvidence.CommandFailure("test", new ProcessOutput(exitCode, "", "failure detail", timedOut)));

    /// <summary>The pure comparison accepts equal measurements without claiming they are a native build.</summary>
    [Fact]
    public void EqualMeasurementsPassOnlyTheComparisonRule()
    {
        JsonObject first = Manifest("binary"u8);
        Assert.Null(LinuxEvidence.Compare("linux-x64", first, "binary"u8, first.DeepClone().AsObject(), "binary"u8));
        Assert.Null(LinuxEvidence.CommandFailure("test", new ProcessOutput(0, "", "", false)));
        Assert.Null(LinuxEvidence.ValidateHost("linux-arm64", "Linux", Architecture.Arm64, Architecture.Arm64));
    }

    /// <summary>An early failure cannot leave evidence from a previous successful run.</summary>
    [Fact]
    public void EarlyFailureInvalidatesOldEvidence()
    {
        using var directory = new TemporaryDirectory();
        string output = Path.Combine(directory.Path, "artifacts", "qualification", "linux-x64");
        Directory.CreateDirectory(output);
        string receipt = Path.Combine(output, "evidence.json");
        File.WriteAllText(receipt, "old successful evidence");
        var environment = new Dictionary<string, string>(TestRepository.CleanEnvironment, StringComparer.Ordinal) { ["GIT_EDITOR"] = "unapproved override" };
        Assert.False(LinuxEvidence.Run(directory.Path, "linux-x64", environment, null).Succeeded);
        Assert.False(File.Exists(receipt));
    }

    /// <summary>A redirected output cannot lead evidence processing outside artifacts.</summary>
    [Fact]
    public void RedirectedOutputIsRejected()
    {
        using var directory = new TemporaryDirectory();
        string target = Path.Combine(directory.Path, "target.so");
        File.WriteAllText(target, "synthetic");
        string link = Path.Combine(directory.Path, "link.so");
        File.CreateSymbolicLink(link, target);
        Assert.False(LinuxEvidence.ReadBinary(link).Succeeded);
    }

    /// <summary>Successful process exit cannot hide empty, skipped, failed or unfinished runs.</summary>
    [Theory]
    [InlineData("Completed", 0, 0, 0, 0)]
    [InlineData("Completed", 2, 1, 1, 0)]
    [InlineData("Completed", 2, 2, 1, 1)]
    [InlineData("Completed", 2, 2, 1, 0)]
    [InlineData("Completed", 2, 2, 3, 0)]
    [InlineData("Aborted", 2, 2, 2, 0)]
    public void IncompleteTestResultsAreRejected(string outcome, int total, int executed, int passed, int failed) =>
        Assert.False(LinuxEvidence.TestResults(Results(outcome, total, executed, passed, failed)).Succeeded);

    /// <summary>Malformed results fail closed instead of treating absent counters as zero.</summary>
    [Fact]
    public void MissingCountersAreRejected() => Assert.False(LinuxEvidence.TestResults(new XDocument(new XElement("TestRun"))).Succeeded);

    /// <summary>Real result counters can be summarized without conferring qualification on synthetic test input.</summary>
    [Fact]
    public void CompletedResultCountersArePreserved()
    {
        Result<JsonObject> result = LinuxEvidence.TestResults(Results("Completed", 3, 3, 3, 0));
        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Value["passed"]!.GetValue<long>());
    }

    /// <summary>Ambient MSBuild and test settings cannot narrow or redirect qualification runs.</summary>
    [Theory]
    [InlineData("VSTestTestCaseFilter")]
    [InlineData("vstesttestcasefilter")]
    [InlineData("VSTEST_TESTCASEFILTER")]
    [InlineData("RunSettings")]
    [InlineData("RunSettingsFilePath")]
    [InlineData("VSTestSetting")]
    [InlineData("VSTestSettings")]
    [InlineData("DirectoryBuildPropsPath")]
    [InlineData("DirectoryBuildTargetsPath")]
    [InlineData("DirectoryPackagesPropsPath")]
    [InlineData("CustomBeforeMicrosoftCommonTargets")]
    [InlineData("CustomAfterMicrosoftCommonTargets")]
    [InlineData("MSBuildExtensionsPath")]
    [InlineData("MSBuildSDKsPath")]
    [InlineData("MSBuildToolsPath")]
    [InlineData("VSTestTestAdapterPath")]
    [InlineData("VSTestTestAssembly")]
    [InlineData("TestProject")]
    [InlineData("TargetPath")]
    [InlineData("OutputPath")]
    [InlineData("BaseOutputPath")]
    [InlineData("TargetFramework")]
    [InlineData("DOTNET_STARTUP_HOOKS")]
    [InlineData("DOTNET_ADDITIONAL_DEPS")]
    [InlineData("DOTNET_SHARED_STORE")]
    [InlineData("COMPlus_ReadyToRun")]
    [InlineData("UNREVIEWED_FUTURE_PROPERTY")]
    public void AmbientTestOverridesAreRemoved(string name)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["PATH"] = "/tools:/bin", [name] = "unreviewed" };
        IReadOnlyDictionary<string, string> selected = LinuxEvidence.TestEnvironment(environment);
        Assert.False(selected.ContainsKey(name));
        Assert.Equal("/tools:/bin", selected["PATH"]);
        Assert.Equal("unreviewed", environment[name]);
    }

    /// <summary>The reviewed runtime, package cache and user directories survive process isolation.</summary>
    [Fact]
    public void RequiredTestEnvironmentIsPreserved()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PATH"] = "/tools:/bin", ["HOME"] = "/home/builder", ["TMPDIR"] = "/tmp/build", ["TMP"] = "/tmp/build", ["TEMP"] = "/tmp/build",
            ["DOTNET_ROOT"] = "/opt/dotnet", ["DOTNET_ROOT_X64"] = "/opt/dotnet-x64", ["DOTNET_ROOT_ARM64"] = "/opt/dotnet-arm64",
            ["DOTNET_CLI_HOME"] = "/home/dotnet", ["NUGET_PACKAGES"] = "/cache/nuget", ["CI"] = "true",
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["DOTNET_NOLOGO"] = "1",
        };
        Assert.Equal(environment.OrderBy(pair => pair.Key, StringComparer.Ordinal), LinuxEvidence.TestEnvironment(environment).OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    /// <summary>The production command path passes the isolated environment to the actual child and records it.</summary>
    [Fact]
    public void EveryCommandUsesAndRecordsReviewedEnvironment()
    {
        using var directory = new TemporaryDirectory();
        var environment = new Dictionary<string, string>(TestRepository.CleanEnvironment, StringComparer.Ordinal)
        {
            ["VSTestTestCaseFilter"] = "ambient-filter-sentinel", ["DirectoryBuildTargetsPath"] = "ambient-redirect-sentinel",
        };
        var session = new LinuxEvidence.Session(directory.Path, directory.Path, environment, null);
        string[] command = OperatingSystem.IsWindows() ? ["cmd.exe", "/d", "/c", "set"] : ["env"];
        Result<string> result = session.Command("environment-proof", command);
        Assert.True(result.Succeeded, result.Succeeded ? null : result.Failure.Message);
        Assert.DoesNotContain("ambient-filter-sentinel", result.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("ambient-redirect-sentinel", result.Value, StringComparison.Ordinal);
        Assert.Contains("PATH=", result.Value, StringComparison.OrdinalIgnoreCase);
        JsonObject recorded = session.Steps[0]!["environment"]!.AsObject();
        Assert.Equal(environment["PATH"], recorded["PATH"]!.GetValue<string>());
        Assert.False(recorded.ContainsKey("VSTestTestCaseFilter"));
        string log = File.ReadAllText(Path.Combine(directory.Path, "environment-proof.log"));
        Assert.Contains("environment=", log, StringComparison.Ordinal);
        Assert.DoesNotContain("ambient-filter-sentinel", log, StringComparison.Ordinal);
    }

    /// <summary>The receipt identifies actual DLL copies consumed by the test hosts and corpus child.</summary>
    [Fact]
    public void ManagedAssemblyDigestsMeasureActualTestedCopies()
    {
        using var directory = new TemporaryDirectory();
        WriteManagedAssemblies(directory.Path);
        JsonObject captured = LinuxEvidence.ManagedAssemblies(directory.Path).Value;
        Assert.Equal(6, captured.Count);
        foreach (string relative in ManagedPaths)
            Assert.Equal(Digest.Sha256("synthetic assembly bytes"u8), captured[relative]!.GetValue<string>());
        Assert.Null(LinuxEvidence.CheckManagedAssemblies(directory.Path, captured));
        File.WriteAllText(Path.Combine(directory.Path, ManagedPaths[0]), "changed after tests");
        Assert.NotNull(LinuxEvidence.CheckManagedAssemblies(directory.Path, captured));
    }

    /// <summary>Missing tested DLLs cannot produce an incomplete success inventory.</summary>
    [Fact]
    public void MissingManagedAssemblyIsRejected()
    {
        using var directory = new TemporaryDirectory();
        WriteManagedAssemblies(directory.Path);
        File.Delete(Path.Combine(directory.Path, ManagedPaths[2]));
        Assert.False(LinuxEvidence.ManagedAssemblies(directory.Path).Succeeded);
    }

    private static readonly string[] ManagedPaths =
    [
        "tests/Moonlark.Libchdr.Tests/bin/Release/net10.0/Moonlark.Libchdr.dll",
        "tests/Moonlark.Libchdr.Tests/bin/Release/net10.0/Moonlark.Libchdr.CorpusRunner.dll",
        "tests/Moonlark.Libchdr.Tests/bin/Release/net10.0/Moonlark.Libchdr.Tests.dll",
        "tests/Moonlark.Native.Engineering.Tests/bin/Release/net10.0/Moonlark.Native.Engineering.dll",
        "tests/Moonlark.Native.Engineering.Tests/bin/Release/net10.0/Moonlark.Native.Engineering.Tests.dll",
        "eng/Moonlark.Native.Engineering/bin/Release/net10.0/Moonlark.Native.Engineering.dll",
    ];

    private static void WriteManagedAssemblies(string root)
    {
        foreach (string relative in ManagedPaths)
        {
            string path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "synthetic assembly bytes");
        }
    }

    private static XDocument Results(string outcome, int total, int executed, int passed, int failed)
    {
        XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        return new XDocument(new XElement(ns + "TestRun", new XElement(ns + "ResultSummary", new XAttribute("outcome", outcome),
            new XElement(ns + "Counters", new XAttribute("total", total), new XAttribute("executed", executed),
                new XAttribute("passed", passed), new XAttribute("failed", failed)))));
    }

    private static JsonObject Manifest(ReadOnlySpan<byte> bytes) => new()
    {
        ["rid"] = "linux-x64", ["sha256"] = Digest.Sha256(bytes),
        ["recipe"] = new JsonObject { ["identity"] = "recipe" },
        ["buildInfo"] = new JsonObject { ["identity"] = "build" },
    };
}
