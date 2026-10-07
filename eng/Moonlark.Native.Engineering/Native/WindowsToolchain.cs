using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Core;

namespace Moonlark.Native.Engineering.Native;

internal sealed record WindowsToolchainInfo(JsonObject Recipe, JsonObject Locations);

/// <summary>Measures the selected x64 MSVC/Windows SDK tools, keeping host locations out of build identity.</summary>
internal static class WindowsToolchain
{
    private static readonly string[] VersionNames = ["visualStudioVersion", "vcToolsVersion", "windowsSdkVersion", "ucrtVersion"];
    private static readonly string[] ToolNames = ["cl", "link", "dumpbin", "rc", "mt", "cmake", "ninja", "vswhere", "c1", "c2"];

    internal static Result<WindowsToolchainInfo> Collect(IReadOnlyDictionary<string, string> environment, string compiler, TextWriter? log)
    {
        Result<IReadOnlyDictionary<string, string>> normalized = BuildEnvironment.WindowsNames(environment);
        if (!normalized.Succeeded) return normalized.Failure;
        IReadOnlyDictionary<string, string> env = normalized.Value;
        string vswhere = Path.Combine(env.GetValueOrDefault("ProgramFiles(x86)") ?? "", "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!Path.IsPathFullyQualified(vswhere) || !File.Exists(vswhere)) return new Failure("Windows toolchain requires installed vswhere");
        Result<string> found = ProcessRunner.Run([vswhere, "-latest", "-products", "*", "-version", "[17.0,18.0)", "-requires",
            "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-format", "json", "-utf8"], env, log);
        if (!found.Succeeded) return found.Failure;
        JsonNode? instances;
        try { instances = JsonNode.Parse(found.Value); }
        catch (System.Text.Json.JsonException) { return new Failure("Invalid vswhere JSON"); }
        if (instances is not JsonArray { Count: 1 } array || array[0] is not JsonObject instance)
            return new Failure("Windows toolchain requires exactly one selected Visual Studio instance");
        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in ToolNames.Where(name => name is not ("vswhere" or "c1" or "c2")))
        {
            string requested = name == "cl" ? compiler : name;
            string? path = ProcessRunner.ResolveExecutable(requested, env);
            if (path is null) return new Failure("Windows toolchain cannot resolve " + name);
            resolved[name] = Path.GetFullPath(path);
        }
        resolved["vswhere"] = vswhere;
        string compilerDirectory = Path.GetDirectoryName(resolved["cl"])!;
        resolved["c1"] = Path.Combine(compilerDirectory, "c1.dll");
        resolved["c2"] = Path.Combine(compilerDirectory, "c2.dll");
        return Measure(env, instance, resolved);
    }

    /// <summary>All inputs are actual environment, vswhere and resolved-file observations; synthetic tests exercise rejection rules.</summary>
    internal static Result<WindowsToolchainInfo> Measure(IReadOnlyDictionary<string, string> environment, JsonObject instance,
        IReadOnlyDictionary<string, string> tools)
    {
        Result<IReadOnlyDictionary<string, string>> normalized = BuildEnvironment.WindowsNames(environment);
        if (!normalized.Succeeded) return normalized.Failure;
        IReadOnlyDictionary<string, string> env = normalized.Value;
        string[] required = ["VSINSTALLDIR", "VCToolsInstallDir", "VCToolsVersion", "WindowsSdkDir", "WindowsSDKVersion", "UniversalCRTSdkDir",
            "UCRTVersion", "VSCMD_ARG_HOST_ARCH", "VSCMD_ARG_TGT_ARCH", "INCLUDE", "LIB", "LIBPATH", "SystemRoot"];
        foreach (string name in required)
            if (string.IsNullOrWhiteSpace(env.GetValueOrDefault(name))) return new Failure("Windows toolchain missing " + name);
        if (env["VSCMD_ARG_HOST_ARCH"] != "x64" || env["VSCMD_ARG_TGT_ARCH"] != "x64")
            return new Failure("Windows toolchain requires x64 host and target selectors");
        string vs = env["VSINSTALLDIR"], vc = env["VCToolsInstallDir"], sdk = env["WindowsSdkDir"];
        string vcVersion = VersionText(env["VCToolsVersion"]), sdkVersion = VersionText(env["WindowsSDKVersion"]), ucrtVersion = VersionText(env["UCRTVersion"]);
        string? vsVersion = JsonFields.RequireString(instance, "installationVersion") is { Succeeded: true } version ? version.Value : null;
        if (!ValidVersion(vcVersion) || !ValidVersion(sdkVersion) || !ValidVersion(ucrtVersion) || vsVersion is null || !ValidVersion(vsVersion)
            || !vsVersion.StartsWith("17.", StringComparison.Ordinal) || sdkVersion != ucrtVersion)
            return new Failure("Windows toolchain has inconsistent or invalid selected versions");
        if (JsonFields.RequireString(instance, "installationPath") is not { Succeeded: true } installation || !SamePath(installation.Value, vs)
            || instance["isComplete"] is not JsonValue complete || !complete.TryGetValue<bool>(out bool isComplete) || !isComplete)
            return new Failure("VsDevCmd environment differs from selected complete Visual Studio instance");
        if (!SamePath(vc, Path.Combine(vs, "VC", "Tools", "MSVC", vcVersion)) || !SamePath(env["UniversalCRTSdkDir"], sdk))
            return new Failure("Windows toolchain paths differ from selected toolset/SDK");
        var recipe = new JsonObject
        {
            ["visualStudioVersion"] = vsVersion, ["vcToolsVersion"] = vcVersion, ["windowsSdkVersion"] = sdkVersion, ["ucrtVersion"] = ucrtVersion,
            ["windowsHostArchitecture"] = "x64", ["windowsTargetArchitecture"] = "x64",
        };
        var locations = new JsonObject { ["visualStudio"] = FullPath(vs), ["vcTools"] = FullPath(vc), ["windowsSdk"] = FullPath(sdk) };
        string vcBin = Path.Combine(vc, "bin", "Hostx64", "x64"), sdkBin = Path.Combine(sdk, "bin", sdkVersion, "x64");
        foreach (string name in ToolNames)
        {
            if (!tools.TryGetValue(name, out string? path) || !Path.IsPathFullyQualified(path) || ArtifactsPath.IsLink(path) || !ArtifactsPath.IsRegularFile(path))
                return new Failure("Windows tool must be a regular absolute file: " + name);
            string? expected = name switch
            {
                "cl" or "link" or "dumpbin" => Path.Combine(vcBin, name + ".exe"),
                "c1" or "c2" => Path.Combine(vcBin, name + ".dll"),
                "rc" or "mt" => Path.Combine(sdkBin, name + ".exe"),
                _ => null,
            };
            if (expected is not null && !SamePath(path, expected)) return new Failure("Windows tool differs from selected toolset/SDK: " + name);
            recipe[name + "Sha256"] = Digest.Sha256File(path);
            locations[name] = FullPath(path);
        }
        foreach (string variable in (string[])["INCLUDE", "LIB", "LIBPATH"])
        {
            Result<string> selected = SearchPath(variable, env[variable], vs, vc, sdk, sdkVersion, env["SystemRoot"]);
            if (!selected.Succeeded) return selected.Failure;
            recipe["windows" + variable] = selected.Value;
            locations["windows" + variable] = env[variable];
        }
        return new WindowsToolchainInfo(recipe, locations);
    }

    private static Result<string> SearchPath(string variable, string value, string vs, string vc, string sdk, string sdkVersion, string systemRoot)
    {
        string include = Path.Combine(sdk, "Include", sdkVersion), lib = Path.Combine(sdk, "Lib", sdkVersion);
        string[] required = variable switch
        {
            "INCLUDE" => [Path.Combine(vc, "include"), Path.Combine(include, "ucrt"), Path.Combine(include, "shared"), Path.Combine(include, "um")],
            "LIB" => [Path.Combine(vc, "lib", "x64"), Path.Combine(lib, "ucrt", "x64"), Path.Combine(lib, "um", "x64")],
            _ => [],
        };
        string[] allowed = variable switch
        {
            "INCLUDE" => [.. required, Path.Combine(vc, "atlmfc", "include"), Path.Combine(vs, "VC", "Auxiliary", "VS", "include"), Path.Combine(include, "winrt"), Path.Combine(include, "cppwinrt")],
            "LIB" => [.. required, Path.Combine(vc, "atlmfc", "lib", "x64")],
            _ => [Path.Combine(vc, "lib", "x64"), Path.Combine(vc, "atlmfc", "lib", "x64"), Path.Combine(vc, "lib", "x86", "store", "references"),
                Path.Combine(sdk, "UnionMetadata", sdkVersion), Path.Combine(sdk, "References", sdkVersion), Path.Combine(systemRoot, "Microsoft.NET", "Framework64", "v4.0.30319")],
        };
        string[] paths = value.Split(';', StringSplitOptions.TrimEntries);
        if (paths.Length == 0 || paths.Any(path => !Path.IsPathFullyQualified(path) || !Directory.Exists(path) || ArtifactsPath.IsLink(path)
            || !allowed.Any(candidate => SamePath(candidate, path))) || required.Any(path => !paths.Any(candidate => SamePath(candidate, path))))
            return new Failure("Windows " + variable + " differs from selected header/library roots");
        return string.Join(';', paths.Select(path => Token(path, vc, "$VC", vs, "$VS", sdk, "$SDK", systemRoot, "$SYSTEMROOT")));
    }

    private static string Token(string path, params string[] roots)
    {
        for (int index = 0; index < roots.Length; index += 2)
            if (ArtifactsPath.IsWithin(path, roots[index])) return roots[index + 1] + "/" + Path.GetRelativePath(roots[index], path).Replace('\\', '/');
        throw new InvalidOperationException("Validated Windows path has no identity root");
    }

    internal static Failure? ValidateCMake(CMakeReply reply, WindowsToolchainInfo measured)
    {
        if (reply.Compiler.Id != "MSVC" || !SamePath(reply.Compiler.Path, measured.Locations["cl"]!.GetValue<string>()))
            return new Failure("CMake selected a different MSVC compiler");
        foreach ((string name, string tool) in (ValueTuple<string, string>[])[("CMAKE_LINKER", "link"), ("CMAKE_RC_COMPILER", "rc"), ("CMAKE_MT", "mt"), ("CMAKE_MAKE_PROGRAM", "ninja"), ("CMAKE_COMMAND", "cmake")])
        {
            CacheEntry[] values = reply.Cache.Where(entry => entry.Name == name).ToArray();
            if (values is not [{ } value] || !SamePath(value.Value, measured.Locations[tool]!.GetValue<string>()))
                return new Failure("CMake selected a different Windows tool: " + tool);
        }
        return null;
    }

    internal static Failure? ValidateRecipe(JsonObject tools)
    {
        foreach (string name in VersionNames)
            if (JsonFields.RequireString(tools, name) is not { Succeeded: true } value || !ValidVersion(value.Value))
                return new Failure("Windows toolchain provenance missing or invalid: " + name);
        foreach (string name in ToolNames)
            if (JsonFields.RequireString(tools, name + "Sha256") is not { Succeeded: true } digest || !Authorities.Sha256().IsMatch(digest.Value))
                return new Failure("Windows toolchain provenance missing tool digest: " + name);
        if (tools["windowsSdkVersion"]!.GetValue<string>() != tools["ucrtVersion"]!.GetValue<string>())
            return new Failure("Windows toolchain provenance has inconsistent SDK versions");
        foreach (string name in (string[])["windowsINCLUDE", "windowsLIB", "windowsLIBPATH"])
        {
            if (JsonFields.RequireString(tools, name) is not { Succeeded: true } value || value.Value.Length == 0
                || value.Value.Split(';').Any(path => !ValidToken(path)))
                return new Failure("Windows toolchain provenance missing or invalid search path: " + name);
        }
        return Check.That(JsonFields.RequireString(tools, "windowsHostArchitecture") is { Succeeded: true, Value: "x64" }
            && JsonFields.RequireString(tools, "windowsTargetArchitecture") is { Succeeded: true, Value: "x64" },
            "Windows toolchain provenance requires x64 host and target");
    }

    private static bool ValidToken(string path) => path.Split('/') is ["$VC" or "$VS" or "$SDK" or "$SYSTEMROOT", .. { Length: > 0 } parts]
        && parts.All(part => part.Length > 0 && part is not ("." or "..") && part.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'));

    private static string VersionText(string value) => value.TrimEnd('\\', '/');
    private static bool ValidVersion(string value) => value.Length > 0 && value.All(character => char.IsAsciiDigit(character) || character == '.') && Version.TryParse(value, out _);
    private static string FullPath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static bool SamePath(string first, string second) => Path.IsPathFullyQualified(first) && Path.IsPathFullyQualified(second)
        && string.Equals(FullPath(first), FullPath(second), StringComparison.OrdinalIgnoreCase);
}
