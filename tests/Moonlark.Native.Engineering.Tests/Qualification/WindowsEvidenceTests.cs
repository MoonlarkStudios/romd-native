using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Native;
using Moonlark.Native.Engineering.Qualification;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Qualification;

/// <summary>Windows uses the same strict evidence checks with an explicit native x64 host and reviewed environment.</summary>
public sealed class WindowsEvidenceTests
{
    /// <summary>Only Windows x64 OS and process may collect the Windows asset.</summary>
    [Fact]
    public void WindowsNativeHostIsAccepted() => Assert.Null(NativeEvidence.ValidateHost("win-x64", "Windows", Architecture.X64, Architecture.X64));

    /// <summary>Matching Windows bytes and input identities can pass the same reproducibility check.</summary>
    [Fact]
    public void WindowsReproducibilityUsesActualBytes()
    {
        var manifest = new JsonObject
        {
            ["rid"] = "win-x64", ["sha256"] = Digest.Sha256("binary"u8),
            ["recipe"] = new JsonObject { ["identity"] = "recipe" }, ["buildInfo"] = new JsonObject { ["identity"] = "build" },
        };
        Assert.Null(NativeEvidence.Compare("win-x64", manifest, "binary"u8, manifest.DeepClone().AsObject(), "binary"u8));
        Assert.NotNull(NativeEvidence.Compare("win-x64", manifest, "binary"u8, manifest.DeepClone().AsObject(), "changed"u8));
    }

    /// <summary>Windows environment names are case-insensitive; process essentials survive while MSBuild overrides do not.</summary>
    [Fact]
    public void WindowsProcessEssentialsSurvive()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Path"] = "tools", ["SystemRoot"] = "windows", ["userprofile"] = "profile", ["AppData"] = "appdata",
            ["LocalAppData"] = "local", ["ComSpec"] = "cmd", ["PATHEXT"] = ".EXE", ["DirectoryBuildTargetsPath"] = "forbidden",
        };
        IReadOnlyDictionary<string, string> selected = WindowsEvidence.TestEnvironment(environment).Value;
        Assert.Equal("tools", selected["PATH"]);
        Assert.Equal("windows", selected["SYSTEMROOT"]);
        Assert.Equal("profile", selected["USERPROFILE"]);
        Assert.Equal("appdata", selected["APPDATA"]);
        Assert.Equal("local", selected["LOCALAPPDATA"]);
        Assert.Equal("cmd", selected["COMSPEC"]);
        Assert.Equal(".EXE", selected["PATHEXT"]);
        Assert.False(selected.ContainsKey("DirectoryBuildTargetsPath"));
    }

    /// <summary>Conflicting Windows aliases are rejected before child-process environment construction.</summary>
    [Theory]
    [InlineData("Path", "PATH")]
    [InlineData("SystemRoot", "SYSTEMROOT")]
    [InlineData("UserProfile", "USERPROFILE")]
    public void ConflictingAliasesAreRejected(string first, string second)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { [first] = "first", [second] = "second" };
        Assert.False(WindowsEvidence.TestEnvironment(environment).Succeeded);
        Assert.False(BuildEnvironment.WindowsNames(environment).Succeeded);
        environment[second] = "first";
        Assert.Equal("first", WindowsEvidence.TestEnvironment(environment).Value[first]);
    }

    /// <summary>A mismatched Windows OS/process pair and another RID cannot qualify this asset.</summary>
    [Theory]
    [InlineData("win-x64", "Linux", Architecture.X64, Architecture.X64)]
    [InlineData("win-x64", "Windows", Architecture.Arm64, Architecture.X64)]
    [InlineData("win-x64", "Windows", Architecture.X64, Architecture.Arm64)]
    [InlineData("linux-x64", "Windows", Architecture.X64, Architecture.X64)]
    public void MismatchedWindowsHostIsRejected(string rid, string system, Architecture os, Architecture process) =>
        Assert.NotNull(NativeEvidence.ValidateHost(rid, system, os, process));

    /// <summary>A failed Windows run removes previous successful evidence before host validation.</summary>
    [Fact]
    public void EarlyWindowsFailureInvalidatesPreviousEvidence()
    {
        using var directory = new Moonlark.Native.Engineering.Tests.TestSupport.TemporaryDirectory();
        string output = Path.Combine(directory.Path, "artifacts", "qualification", "win-x64");
        Directory.CreateDirectory(output);
        string receipt = Path.Combine(output, "evidence.json");
        File.WriteAllText(receipt, "stale success");
        Assert.False(WindowsEvidence.Run(directory.Path, new Dictionary<string, string> { ["GIT_EDITOR"] = "rejected" }, null).Succeeded);
        Assert.False(File.Exists(receipt));
    }

    /// <summary>A compiler banner alone cannot prove the selected Windows SDK/toolset.</summary>
    [Fact]
    public void WindowsCompilerRequiresToolsetProvenance() =>
        Assert.False(NativeToolchain.WithCompiler(new JsonObject(), new CompilerIdentity("MSVC", "19.44", "cl.exe"), "win-x64").Succeeded);
}
