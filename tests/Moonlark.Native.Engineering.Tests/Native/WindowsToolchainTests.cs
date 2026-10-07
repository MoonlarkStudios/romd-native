using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Native;
using Moonlark.Native.Engineering.Tests.TestSupport;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Native;

/// <summary>Synthetic toolsets exercise selection and provenance rules without claiming Windows execution.</summary>
public sealed class WindowsToolchainTests
{
    /// <summary>Recipes contain selected versions and actual tool digests, while host locations are separate.</summary>
    [Fact]
    public void SelectedToolsProduceLocationIndependentIdentity()
    {
        using var first = new WindowsToolset();
        using var second = new WindowsToolset();
        WindowsToolchainInfo measured = first.Measure().Value;
        Assert.True(JsonFields.SameCanonical(measured.Recipe, second.Measure().Value.Recipe));
        Assert.Null(WindowsToolchain.ValidateRecipe(measured.Recipe));
        Assert.Null(BuildRecipe.RejectLocations(measured.Recipe, [first.Root, second.Root]));
        Assert.Contains(first.Root, measured.Locations["cl"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal(Digest.Sha256File(first.Tools["cl"]), measured.Recipe["clSha256"]!.GetValue<string>());
        File.AppendAllText(first.Tools["c1"], "changed compiler front end");
        Assert.False(JsonFields.SameCanonical(measured.Recipe, first.Measure().Value.Recipe));
    }

    /// <summary>Wrong selectors, versions, installation or header/library roots are rejected.</summary>
    [Theory]
    [InlineData("VSCMD_ARG_HOST_ARCH", "x86")]
    [InlineData("VSCMD_ARG_TGT_ARCH", "arm64")]
    [InlineData("VCToolsVersion", "14.99.0")]
    [InlineData("WindowsSDKVersion", "10.0.0.0")]
    [InlineData("UCRTVersion", "10.0.0.0")]
    [InlineData("WindowsSDKVersion", "../10.0.26100.0")]
    [InlineData("VSINSTALLDIR", "relative")]
    [InlineData("INCLUDE", "relative")]
    [InlineData("LIB", "")]
    [InlineData("LIBPATH", "relative")]
    public void IncorrectSelectedEnvironmentIsRejected(string name, string value)
    {
        using var tools = new WindowsToolset();
        tools.Environment[name] = value;
        Assert.False(tools.Measure().Succeeded);
    }

    /// <summary>Resolving another existing executable cannot masquerade as the selected compiler or SDK tool.</summary>
    [Theory]
    [InlineData("cl")]
    [InlineData("link")]
    [InlineData("dumpbin")]
    [InlineData("rc")]
    [InlineData("mt")]
    [InlineData("c1")]
    [InlineData("c2")]
    public void WrongToolLocationIsRejected(string name)
    {
        using var tools = new WindowsToolset();
        tools.Tools[name] = tools.Tools["cmake"];
        Assert.False(tools.Measure().Succeeded);
    }

    /// <summary>Every recorded tool must exist as an ordinary file.</summary>
    [Theory]
    [InlineData("cl")]
    [InlineData("cmake")]
    [InlineData("ninja")]
    [InlineData("vswhere")]
    [InlineData("c1")]
    public void MissingToolIsRejected(string name)
    {
        using var tools = new WindowsToolset();
        File.Delete(tools.Tools[name]);
        Assert.False(tools.Measure().Succeeded);
    }

    /// <summary>The VS instance observed by vswhere must be complete and selected by VsDevCmd.</summary>
    [Theory]
    [InlineData("installationPath", "\"elsewhere\"")]
    [InlineData("installationVersion", "\"18.0.1\"")]
    [InlineData("isComplete", "false")]
    public void MismatchedVisualStudioInstanceIsRejected(string name, string value)
    {
        using var tools = new WindowsToolset();
        tools.Instance[name] = JsonNode.Parse(value);
        Assert.False(tools.Measure().Succeeded);
    }

    /// <summary>The effective CMake compiler and linker/SDK paths must equal the measured selection.</summary>
    [Theory]
    [InlineData("compiler")]
    [InlineData("CMAKE_LINKER")]
    [InlineData("CMAKE_RC_COMPILER")]
    [InlineData("CMAKE_MT")]
    [InlineData("CMAKE_MAKE_PROGRAM")]
    [InlineData("CMAKE_COMMAND")]
    public void CMakeCannotSelectDifferentTools(string name)
    {
        using var tools = new WindowsToolset();
        WindowsToolchainInfo measured = tools.Measure().Value;
        CMakeReply reply = tools.Reply();
        Assert.Null(WindowsToolchain.ValidateCMake(reply, measured));
        reply = name == "compiler" ? reply with { Compiler = reply.Compiler with { Path = tools.Tools["cmake"] } }
            : reply with { Cache = [.. reply.Cache.Select(entry => entry.Name == name ? entry with { Value = tools.Tools["cl"] } : entry)] };
        Assert.NotNull(WindowsToolchain.ValidateCMake(reply, measured));
    }

    /// <summary>Manifest verification rejects missing or malformed provenance even when build-info is recomputed to match.</summary>
    [Theory]
    [InlineData("windowsSdkVersion", null)]
    [InlineData("clSha256", "\"not a digest\"")]
    [InlineData("windowsINCLUDE", "\"$VC/../../unreviewed\"")]
    [InlineData("windowsTargetArchitecture", "\"arm64\"")]
    public void ForgedWindowsManifestProvenanceIsRejected(string name, string? value)
    {
        using var tools = new WindowsToolset();
        using var root = new NativeRoot();
        JsonObject toolchain = tools.Measure().Value.Recipe;
        toolchain["compilerId"] = "MSVC";
        toolchain["compilerVersion"] = "19.44.1";
        string binary = Path.Combine(root.Path, NativeRids.LibraryFileName("win-x64"));
        File.WriteAllBytes(binary, "synthetic; never loaded"u8.ToArray());
        JsonObject recipe = BuildRecipe.Create(root.Authority, "win-x64", NativeRoot.Configuration("win-x64"), toolchain, 12345, BuildRecipe.InputDigests(root.Path).Value);
        var inspection = new Inspection([.. root.Allowlist.Order(StringComparer.Ordinal)], ["KERNEL32.dll"], new JsonObject { ["architecture"] = "synthetic" });
        JsonObject manifest = NativeManifest.Create(root.Authority, "win-x64", binary, recipe, inspection, new JsonObject());
        Assert.Null(NativeVerify.VerifyManifest(manifest, binary, root.Path));
        if (value is null) recipe["toolchain"]!.AsObject().Remove(name);
        else recipe["toolchain"]![name] = JsonNode.Parse(value);
        manifest["recipe"] = recipe.DeepClone();
        manifest["buildInfo"] = BuildRecipe.BuildInfo(root.Authority.Pin, root.Authority.ManagedVersion, recipe);
        Assert.Contains("Windows toolchain provenance", NativeVerify.VerifyManifest(manifest, binary, root.Path)?.Message, StringComparison.Ordinal);
    }
}

internal sealed class WindowsToolset : IDisposable
{
    private readonly TemporaryDirectory _directory = new();
    internal string Root => _directory.Path;
    internal Dictionary<string, string> Environment { get; } = new(StringComparer.OrdinalIgnoreCase);
    internal Dictionary<string, string> Tools { get; } = new(StringComparer.Ordinal);
    internal JsonObject Instance { get; }

    internal WindowsToolset()
    {
        string vs = Path.Combine(Root, "VS"), vc = Path.Combine(vs, "VC", "Tools", "MSVC", "14.44.35207"), sdk = Path.Combine(Root, "SDK");
        const string sdkVersion = "10.0.26100.0";
        Environment["VSINSTALLDIR"] = vs;
        Environment["VCToolsInstallDir"] = vc;
        Environment["VCToolsVersion"] = "14.44.35207";
        Environment["WindowsSdkDir"] = sdk;
        Environment["UniversalCRTSdkDir"] = sdk;
        Environment["WindowsSDKVersion"] = sdkVersion + "\\";
        Environment["UCRTVersion"] = sdkVersion;
        Environment["VSCMD_ARG_HOST_ARCH"] = "x64";
        Environment["VSCMD_ARG_TGT_ARCH"] = "x64";
        Environment["SystemRoot"] = Path.Combine(Root, "Windows");
        Environment["INCLUDE"] = string.Join(';', new[] { Path.Combine(vc, "include"), Path.Combine(sdk, "Include", sdkVersion, "ucrt"), Path.Combine(sdk, "Include", sdkVersion, "shared"), Path.Combine(sdk, "Include", sdkVersion, "um") });
        Environment["LIB"] = string.Join(';', new[] { Path.Combine(vc, "lib", "x64"), Path.Combine(sdk, "Lib", sdkVersion, "ucrt", "x64"), Path.Combine(sdk, "Lib", sdkVersion, "um", "x64") });
        Environment["LIBPATH"] = Path.Combine(vc, "lib", "x64");
        foreach (string variable in (string[])["INCLUDE", "LIB", "LIBPATH"])
            foreach (string directory in Environment[variable].Split(';')) Directory.CreateDirectory(directory);
        foreach (string name in (string[])["cl", "link", "dumpbin", "c1", "c2", "rc", "mt", "cmake", "ninja", "vswhere"])
        {
            string directory = name is "cl" or "link" or "dumpbin" or "c1" or "c2" ? Path.Combine(vc, "bin", "Hostx64", "x64")
                : name is "rc" or "mt" ? Path.Combine(sdk, "bin", sdkVersion, "x64") : Path.Combine(Root, "tools");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, name + (name is "c1" or "c2" ? ".dll" : ".exe"));
            File.WriteAllText(path, "synthetic " + name);
            Tools[name] = path;
        }
        Instance = new JsonObject { ["installationPath"] = vs, ["installationVersion"] = "17.14.37710.0", ["isComplete"] = true };
    }

    internal Result<WindowsToolchainInfo> Measure() => WindowsToolchain.Measure(Environment, Instance, Tools);
    internal CMakeReply Reply() => new([new("CMAKE_LINKER", "FILEPATH", Tools["link"]), new("CMAKE_RC_COMPILER", "FILEPATH", Tools["rc"]), new("CMAKE_MT", "FILEPATH", Tools["mt"]), new("CMAKE_MAKE_PROGRAM", "FILEPATH", Tools["ninja"]), new("CMAKE_COMMAND", "INTERNAL", Tools["cmake"])], new("MSVC", "19.44.1", Tools["cl"]));
    public void Dispose() => _directory.Dispose();
}
