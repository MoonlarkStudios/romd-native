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

    /// <summary>The observed NETFX include path is audited but cannot affect the C search paths or recipe.</summary>
    [Fact]
    public void ObservedNetFxIncludeIsExcludedFromCBuildIdentity()
    {
        using var tools = new WindowsToolset();
        WindowsToolchainInfo before = tools.Measure().Value;
        string netfx = Path.Combine(Path.GetDirectoryName(tools.Environment["WindowsSdkDir"])!, "NETFXSDK", "4.8", "include", "um");
        Directory.CreateDirectory(netfx);
        tools.Environment["INCLUDE"] += ";" + netfx;
        Result<WindowsToolchainInfo> result = tools.Measure();
        Assert.True(result.Succeeded, result.Succeeded ? "" : result.Failure.Message);
        Assert.Equal("libchdr-c-v1", result.Value.Recipe["windowsSearchPathPolicy"]?.GetValue<string>());
        Assert.True(JsonFields.SameCanonical(before.Recipe, result.Value.Recipe));
        Assert.Equal(tools.Environment["INCLUDE"], result.Value.Locations["originalINCLUDE"]?.GetValue<string>());
        Assert.DoesNotContain(netfx, result.Value.Locations["windowsINCLUDE"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.DoesNotContain("NETFX", JsonFields.Compact(result.Value.Recipe), StringComparison.Ordinal);
    }

    /// <summary>The observed x64 NETFX library directory is preserved as bootstrap evidence but excluded from the fixed C paths.</summary>
    [Fact]
    public void ObservedNetFxLibraryIsExcludedFromCBuildIdentity()
    {
        using var tools = new WindowsToolset();
        WindowsToolchainInfo before = tools.Measure().Value;
        string netfx = Path.Combine(Path.GetDirectoryName(tools.Environment["WindowsSdkDir"])!, "NETFXSDK", "4.8", "lib", "um", "x64");
        Directory.CreateDirectory(netfx);
        tools.Environment["LIB"] += ";" + netfx;
        Result<WindowsToolchainInfo> result = tools.Measure();
        Assert.True(result.Succeeded, result.Succeeded ? "" : result.Failure.Message);
        Assert.True(JsonFields.SameCanonical(before.Recipe, result.Value.Recipe));
        Assert.Equal("libchdr-c-v1", result.Value.Recipe["windowsSearchPathPolicy"]!.GetValue<string>());
        Assert.Equal("$VC/lib/x64;$SDK/Lib/10.0.26100.0/ucrt/x64;$SDK/Lib/10.0.26100.0/um/x64",
            result.Value.Recipe["windowsLIB"]!.GetValue<string>());
        Assert.Equal(tools.Environment["LIB"], result.Value.Locations["originalLIB"]!.GetValue<string>());
        Assert.Equal(before.SelectedSearchPaths["LIB"], result.Value.SelectedSearchPaths["LIB"]);
        Assert.DoesNotContain(netfx, result.Value.Locations["windowsLIB"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    /// <summary>No unobserved architecture/version/root, absent directory, link, or LIBPATH use gains an exclusion.</summary>
    [Theory]
    [InlineData("version")]
    [InlineData("architecture")]
    [InlineData("missing")]
    [InlineData("ancestor-link")]
    [InlineData("leaf-link")]
    [InlineData("unknown-root")]
    [InlineData("libpath")]
    public void NetFxLibraryExclusionRemainsNarrow(string scenario)
    {
        using var tools = new WindowsToolset();
        string parent = scenario == "unknown-root" ? Path.Combine(tools.Root, "other Windows Kits")
            : Path.GetDirectoryName(tools.Environment["WindowsSdkDir"])!;
        string version = Path.Combine(parent, "NETFXSDK", scenario == "version" ? "4.8.1" : "4.8");
        string extra = Path.Combine(version, "lib", "um", scenario == "architecture" ? "x86" : "x64");
        if (scenario != "missing") Directory.CreateDirectory(extra);
        if (scenario is "ancestor-link" or "leaf-link")
        {
            string link = scenario == "ancestor-link" ? version : extra;
            Directory.Move(link, link + "-actual");
            Directory.CreateSymbolicLink(link, link + "-actual");
        }
        tools.Environment[scenario == "libpath" ? "LIBPATH" : "LIB"] += ";" + extra;
        Assert.False(tools.Measure().Succeeded);
    }

    /// <summary>Unobserved NETFX versions, library roots and linked ancestors remain rejected.</summary>
    [Theory]
    [InlineData("version")]
    [InlineData("unobserved-library-layout")]
    [InlineData("ancestor-link")]
    public void NetFxExclusionIsExactAndDirectoryBound(string scenario)
    {
        using var tools = new WindowsToolset();
        string parent = Path.GetDirectoryName(tools.Environment["WindowsSdkDir"])!;
        string version = Path.Combine(parent, "NETFXSDK", scenario == "version" ? "4.8.1" : "4.8");
        string extra = Path.Combine(version, scenario == "unobserved-library-layout" ? "lib" : "include", scenario == "unobserved-library-layout" ? "x64" : "um");
        Directory.CreateDirectory(extra);
        if (scenario == "ancestor-link")
        {
            Directory.Move(version, version + "-actual");
            Directory.CreateSymbolicLink(version, version + "-actual");
        }
        tools.Environment[scenario == "unobserved-library-layout" ? "LIB" : "INCLUDE"] += ";" + extra;
        Assert.False(tools.Measure().Succeeded);
    }

    /// <summary>Recipes must identify the closed C policy and cannot add or omit effective roots.</summary>
    [Theory]
    [InlineData("windowsSearchPathPolicy", null)]
    [InlineData("windowsSearchPathPolicy", "other")]
    [InlineData("windowsINCLUDE", "$VC/include")]
    [InlineData("windowsLIB", "$VC/lib/x64")]
    [InlineData("windowsLIBPATH", "$SYSTEMROOT/Microsoft.NET/Framework64/v4.0.30319")]
    public void CSearchRecipeRejectsMissingOrChangedPolicy(string name, string? value)
    {
        using var tools = new WindowsToolset();
        JsonObject recipe = tools.Measure().Value.Recipe;
        if (value is null) recipe.Remove(name);
        else recipe[name] = value;
        Assert.NotNull(WindowsToolchain.ValidateRecipe(recipe));
    }

    /// <summary>All original search paths are captured even when the first validation fails.</summary>
    [Fact]
    public void AllBootstrapSearchPathsAreLoggedBeforeValidationFailure()
    {
        using var tools = new WindowsToolset();
        tools.Environment["INCLUDE"] = "unapproved\ninclude";
        tools.Environment["LIB"] = "unobserved lib";
        tools.Environment["LIBPATH"] = "unobserved libpath";
        using var log = new StringWriter();
        Assert.False(WindowsToolchain.Measure(tools.Environment, tools.Instance, tools.Tools, log).Succeeded);
        string line = Assert.Single(log.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        const string prefix = "windowsBootstrapSearchPaths=";
        Assert.StartsWith(prefix, line, StringComparison.Ordinal);
        JsonObject paths = JsonNode.Parse(line[prefix.Length..])!.AsObject();
        foreach (string name in (string[])["INCLUDE", "LIB", "LIBPATH"])
            Assert.Equal(tools.Environment[name], paths[name]!.GetValue<string>());
    }

    /// <summary>Existing allowed bootstrap extras and order cannot alter the fixed C environment.</summary>
    [Fact]
    public void AllowedBootstrapExtrasDoNotChangeEffectiveCSearchPaths()
    {
        using var tools = new WindowsToolset();
        WindowsToolchainInfo before = tools.Measure().Value;
        string extra = Path.Combine(tools.Environment["VCToolsInstallDir"], "atlmfc", "include");
        Directory.CreateDirectory(extra);
        tools.Environment["INCLUDE"] = extra + ";" + string.Join(';', tools.Environment["INCLUDE"].Split(';').Reverse());
        WindowsToolchainInfo after = tools.Measure().Value;
        Assert.True(JsonFields.SameCanonical(before.Recipe, after.Recipe));
        Assert.Equal(before.SelectedSearchPaths.OrderBy(pair => pair.Key), after.SelectedSearchPaths.OrderBy(pair => pair.Key));
        Assert.NotEqual(before.Locations["originalINCLUDE"]!.GetValue<string>(), after.Locations["originalINCLUDE"]!.GetValue<string>());
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

    /// <summary>Failure evidence identifies the rejected entry without accepting or dropping it.</summary>
    [Theory]
    [InlineData("leading-empty", "empty")]
    [InlineData("trailing-empty", "empty")]
    [InlineData("relative", "not-absolute")]
    [InlineData("unknown", "not-allowed")]
    [InlineData("nonexistent", "missing-directory")]
    [InlineData("link", "link")]
    public void SearchPathFailureIdentifiesRejectedEntry(string scenario, string reason)
    {
        using var tools = new WindowsToolset();
        string original = tools.Environment["INCLUDE"];
        string[] required = original.Split(';');
        string rejected = scenario switch
        {
            "leading-empty" or "trailing-empty" => "",
            "relative" => "relative\"quoted\nentry",
            "unknown" => Path.Combine(tools.Root, "unreviewed"),
            _ => required[0],
        };
        if (scenario == "unknown") Directory.CreateDirectory(rejected);
        if (scenario is "nonexistent" or "link") Directory.Delete(rejected);
        if (scenario == "link")
        {
            string target = Path.Combine(tools.Root, "linked-include");
            Directory.CreateDirectory(target);
            Directory.CreateSymbolicLink(rejected, target);
        }
        tools.Environment["INCLUDE"] = scenario switch
        {
            "leading-empty" => ";" + original,
            "nonexistent" or "link" => original,
            _ => original + ";" + rejected,
        };
        Result<WindowsToolchainInfo> result = tools.Measure();
        Assert.False(result.Succeeded);
        string message = result.Failure.Message;
        Assert.DoesNotContain('\n', message);
        JsonObject diagnostic = SearchPathDiagnostic(message, "INCLUDE");
        Assert.Equal(tools.Environment["INCLUDE"], diagnostic["rawValue"]!.GetValue<string>());
        int index = scenario is "leading-empty" or "nonexistent" or "link" ? 0 : required.Length;
        JsonObject entry = diagnostic["entries"]!.AsArray()[index]!.AsObject();
        Assert.Equal(index, entry["index"]!.GetValue<int>());
        Assert.Equal(rejected, entry["value"]!.GetValue<string>());
        Assert.Equal(reason, entry["reason"]!.GetValue<string>());
        Assert.Equal<string>(required, diagnostic["requiredRoots"]!.AsArray().Select(node => node!.GetValue<string>()));
        Assert.Empty(diagnostic["missingRequiredRoots"]!.AsArray());
        Assert.All(required, path => Assert.Contains(path, diagnostic["allowedRoots"]!.AsArray().Select(node => node!.GetValue<string>())));
    }

    /// <summary>Present entries can all be allowed while a selected required directory is absent.</summary>
    [Fact]
    public void SearchPathFailureIdentifiesMissingRequiredRoot()
    {
        using var tools = new WindowsToolset();
        string[] required = tools.Environment["LIB"].Split(';');
        tools.Environment["LIB"] = string.Join(';', required.Skip(1));
        Result<WindowsToolchainInfo> result = tools.Measure();
        Assert.False(result.Succeeded);
        JsonObject diagnostic = SearchPathDiagnostic(result.Failure.Message, "LIB");
        Assert.Equal(required[0], Assert.Single(diagnostic["missingRequiredRoots"]!.AsArray())!.GetValue<string>());
        Assert.All(diagnostic["entries"]!.AsArray(), entry => Assert.Null(entry!["reason"]));
    }

    /// <summary>Diagnostics do not change the established rejection for a wholly empty environment value.</summary>
    [Fact]
    public void EmptySearchPathRemainsMissingEnvironment()
    {
        using var tools = new WindowsToolset();
        tools.Environment["INCLUDE"] = "";
        Assert.Equal("Windows toolchain missing INCLUDE", tools.Measure().Failure.Message);
    }

    /// <summary>Valid search paths retain their recipe tokens and exact diagnostic-only locations.</summary>
    [Fact]
    public void SuccessfulSearchPathIdentityRemainsUnchanged()
    {
        using var tools = new WindowsToolset();
        WindowsToolchainInfo measured = tools.Measure().Value;
        Assert.Equal("$VC/include;$SDK/Include/10.0.26100.0/ucrt;$SDK/Include/10.0.26100.0/shared;$SDK/Include/10.0.26100.0/um",
            measured.Recipe["windowsINCLUDE"]!.GetValue<string>());
        Assert.Equal("$VC/lib/x64;$SDK/Lib/10.0.26100.0/ucrt/x64;$SDK/Lib/10.0.26100.0/um/x64", measured.Recipe["windowsLIB"]!.GetValue<string>());
        Assert.Equal("$VC/lib/x64", measured.Recipe["windowsLIBPATH"]!.GetValue<string>());
        foreach (string name in (string[])["INCLUDE", "LIB", "LIBPATH"])
            Assert.Equal(tools.Environment[name], measured.Locations["windows" + name]!.GetValue<string>());
    }

    private static JsonObject SearchPathDiagnostic(string message, string variable)
    {
        string prefix = "Windows " + variable + " differs from selected header/library roots; diagnostics=";
        Assert.StartsWith(prefix, message, StringComparison.Ordinal);
        JsonObject diagnostic = JsonNode.Parse(message[prefix.Length..])!.AsObject();
        Assert.Equal(variable, diagnostic["variable"]!.GetValue<string>());
        return diagnostic;
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
        string vs = Path.Combine(Root, "VS"), vc = Path.Combine(vs, "VC", "Tools", "MSVC", "14.44.35207"), sdk = Path.Combine(Root, "Windows Kits", "10");
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
