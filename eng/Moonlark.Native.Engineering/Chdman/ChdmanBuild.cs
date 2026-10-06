using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Moonlark.Native.Engineering.Core;

namespace Moonlark.Native.Engineering.Chdman;

internal sealed record ChdmanBuildRequest(string Archive, string Output, int Jobs, string LogPath);

/// <summary>Builds only the pinned MAME chdman into ignored local artifacts. The recipe qualifies its macOS ARM64 execution only.</summary>
internal static partial class ChdmanBuild
{
    internal const string RecipeDirectory = "eng/Moonlark.Native.Engineering/Chdman";
    internal const string ManifestName = "build-manifest.json";
    private const string BinaryPath = "build/osx_clang/bin/x64/Release/chdman";
    private const string Banner = "chdman - MAME Compressed Hunks of Data (CHD) manager ";

    private static readonly FrozenSet<string> MakeOverrides =
        new[] { "MAKEFLAGS", "MFLAGS", "GENIE_FLAGS", "MAKEFILES", "MAKEOVERRIDES", "GNUMAKEFLAGS" }.ToFrozenSet(StringComparer.Ordinal);

    // Upstream make variables can also be inherited by name. Supply only process/runtime
    // essentials so unrelated shell variables cannot set them.
    private static readonly FrozenSet<string> Essentials =
        new[] { "PATH", "HOME", "TMPDIR", "TMP", "TEMP", "USER", "LOGNAME", "SHELL", "SOURCE_DATE_EPOCH" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> AllowedDependencies =
        new[] { "/usr/lib/libSystem.B.dylib", "/usr/lib/libc++.1.dylib" }.ToFrozenSet(StringComparer.Ordinal);

    private sealed record Toolchain(string Compiler, string Sdk, string MakeVersion, string ClangVersion);

    private sealed record Inspection(ImmutableArray<string> Dependencies, string Version);

    internal static Result<JsonObject> Run(string root, IReadOnlyDictionary<string, string> environment, ChdmanBuildRequest request)
    {
        if (Check.That(OperatingSystem.IsMacOS() && RuntimeInformation.OSArchitecture == Architecture.Arm64
            && RuntimeInformation.ProcessArchitecture == Architecture.Arm64,
            "This local recipe currently qualifies only its macOS ARM64 execution") is { } host) return host;
        if (Check.That(request.Jobs is >= 1 and <= 32, "Jobs must be between 1 and 32") is { } jobs) return jobs;
        Result<string> output = ArtifactsPath.ValidateDirectory(request.Output, root);
        if (!output.Succeeded) return output.Failure;
        Result<MamePin> pin = MamePin.Read(root);
        if (!pin.Succeeded) return pin.Failure;
        if (SourceArchive.Validate(request.Archive, pin.Value) is { } archive) return archive;
        // Taken before the long build, like Python hashing its own file; recipe sources must not change meanwhile.
        string recipe = RecipeSha256(root);
        if (PrepareOutput(output.Value) is { } prepared) return prepared;
        Result<ExtractedSource> source = SourceArchive.Extract(request.Archive, Path.Combine(output.Value, "source"), pin.Value);
        if (!source.Succeeded) return source.Failure;
        Result<IReadOnlyDictionary<string, string>> env = MakeEnvironment(environment, source.Value.Epoch);
        if (!env.Succeeded) return env.Failure;
        Result<JsonObject> receipt;
        using (var log = new StreamWriter(request.LogPath, append: false))
            receipt = Build(pin.Value, source.Value, output.Value, request.Jobs, recipe, env.Value, log);
        if (!receipt.Succeeded) return receipt.Failure;
        File.WriteAllText(Path.Combine(output.Value, ManifestName), ReceiptJson.Serialize(receipt.Value));
        return receipt.Value;
    }

    /// <summary>Allowlist-only make environment: implicit tool inputs and make/GENie overrides fail, everything else is dropped.</summary>
    internal static Result<IReadOnlyDictionary<string, string>> MakeEnvironment(IReadOnlyDictionary<string, string> environment, long epoch)
    {
        Result<IReadOnlyDictionary<string, string>> created = BuildEnvironment.Create(environment, epoch);
        if (!created.Succeeded) return created.Failure;
        if (Check.That(!created.Value.Keys.Any(MakeOverrides.Contains), "Unrecorded make/GENie environment override") is { } makeOverride) return makeOverride;
        Dictionary<string, string> result = created.Value.Where(pair => Essentials.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        result["LC_ALL"] = "C";
        result["TZ"] = "UTC";
        return result;
    }

    /// <summary>The bundled GENie invocation: tools only, fatal warnings and bundled codecs kept, prefix-mapped sources.</summary>
    internal static ImmutableArray<string> GenerateArguments(string source, string clangVersion) =>
    [
        Path.Combine(source, "3rdparty/genie/bin/darwin/genie"), "--with-tools",
        "--target=mame", "--subtarget=mame", "--osd=mac", "--targetos=macosx",
        "--PLATFORM=arm64", "--build-dir=build", "--gcc=osx-clang",
        "--gcc_version=" + clangVersion, "--OPTIMIZE=3", "--NOASM=1",
        "--SYMBOLS=0", "--STRIP_SYMBOLS=1", "--SEPARATE_BIN=1",
        "--ARCHOPTS=-ffile-prefix-map=" + source + "=/_/mame -mmacosx-version-min=14.0",
        "--LDOPTS=-mmacosx-version-min=14.0 -Wl,-dead_strip,-dead_strip_dylibs", "gmake",
    ];

    /// <summary>mac.lua adds emulator frameworks globally. A recorded, explicit tool link uses the same bundled archives, retaining only chdman's imports.</summary>
    internal static ImmutableArray<string> LinkArguments(int jobs) =>
    [
        "make", "-C", "build/projects/mac/mame/gmake-osx-clang", "config=release64",
        "-j" + jobs.ToString(CultureInfo.InvariantCulture), "LIBS=$(LDDEPS) -lpthread",
        "ALL_LDFLAGS=-m64 -arch arm64 -mmacosx-version-min=14.0 -Wl,-dead_strip,-dead_strip_dylibs,-fatal_warnings,-reproducible", "chdman",
    ];

    /// <summary>
    /// SHA-256 of the <c>shasum -a 256</c> listing (lowercase digest, two spaces, path, LF) of <see cref="RecipeSources"/>:
    /// <c>LC_ALL=C shasum -a 256 eng/Moonlark.Native.Engineering/Chdman/*.cs | shasum -a 256</c> from the repository root.
    /// </summary>
    internal static string RecipeSha256(string root) =>
        Digest.Sha256(Encoding.UTF8.GetBytes(string.Concat(RecipeSources(root)
            .Select(path => Digest.Sha256File(Path.Combine(root, path)) + "  " + path + "\n"))));

    /// <summary>Every C# source under the recipe directory, as repository-relative '/' paths in ordinal order.</summary>
    internal static ImmutableArray<string> RecipeSources(string root) =>
    [
        .. Directory.EnumerateFiles(Path.Combine(root, RecipeDirectory), "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal),
    ];

    private static Failure? PrepareOutput(string output)
    {
        Directory.CreateDirectory(output);
        File.Delete(Path.Combine(output, ManifestName));
        foreach (string name in (string[])["source", "native"])
        {
            string child = Path.Combine(output, name);
            if (Check.That(!ArtifactsPath.IsLink(child), "Output child cannot be a symlink: " + name) is { } link) return link;
            if (Directory.Exists(child)) Directory.Delete(child, recursive: true);
        }
        return null;
    }

    private static Result<JsonObject> Build(MamePin pin, ExtractedSource source, string output, int jobs, string recipe,
        IReadOnlyDictionary<string, string> environment, TextWriter log)
    {
        Result<Toolchain> toolchain = IdentifyToolchain(source.Source, environment, log);
        if (!toolchain.Succeeded) return toolchain.Failure;
        ImmutableArray<string> generate = GenerateArguments(source.Source, toolchain.Value.ClangVersion);
        ImmutableArray<string> link = LinkArguments(jobs);
        ImmutableArray<string>[] steps =
        [
            ["make", "-C", "3rdparty/genie/build/gmake.darwin", "-f", "genie.make", "-j" + jobs.ToString(CultureInfo.InvariantCulture)],
            ["make", "build/generated/version.cpp", "OSD=mac", "EMULATOR=0", "TOOLS=1", "PLATFORM=arm64",
                "OVERRIDE_CC=clang", "OVERRIDE_CXX=clang++", "NEW_GIT_VERSION=" + pin.Commit],
            generate,
            link,
        ];
        foreach (ImmutableArray<string> step in steps)
        {
            Result<string> result = ProcessRunner.Run(step, environment, log, source.Source);
            if (!result.Succeeded) return result.Failure;
        }
        Result<string> tool = Install(source.Source, output);
        if (!tool.Succeeded) return tool.Failure;
        Result<Inspection> inspection = Inspect(tool.Value, pin, source.Source, environment, log);
        if (!inspection.Succeeded) return inspection.Failure;
        return Receipt(pin, toolchain.Value, recipe, source.Epoch, generate, link, tool.Value, inspection.Value);
    }

    private static Result<Toolchain> IdentifyToolchain(string source, IReadOnlyDictionary<string, string> environment, TextWriter log)
    {
        Result<string> compiler = ProcessRunner.Run(["clang", "--version"], environment, log, source);
        if (!compiler.Succeeded) return compiler.Failure;
        Result<string> sdk = ProcessRunner.Run(["xcrun", "--show-sdk-version"], environment, log, source);
        if (!sdk.Succeeded) return sdk.Failure;
        Result<string> make = ProcessRunner.Run(["make", "--version"], environment, log, source);
        if (!make.Succeeded) return make.Failure;
        Match match = ClangVersion().Match(compiler.Value);
        if (Check.That(match.Success, "Cannot identify clang version") is { } unknown) return unknown;
        return new Toolchain(compiler.Value, sdk.Value, Lines(make.Value)[0], match.Groups[1].Value);
    }

    private static Result<string> Install(string source, string output)
    {
        string binary = Path.Combine(source, BinaryPath);
        if (Check.That(File.Exists(binary), "chdman output was not produced") is { } missing) return missing;
        string native = Path.Combine(output, "native");
        Directory.CreateDirectory(native);
        string tool = Path.Combine(native, "chdman");
        File.Copy(binary, tool);
        // Python's copy2 keeps the access and modification times.
        File.SetLastAccessTimeUtc(tool, File.GetLastAccessTimeUtc(binary));
        File.SetLastWriteTimeUtc(tool, File.GetLastWriteTimeUtc(binary));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        return tool;
    }

    private static Result<Inspection> Inspect(string tool, MamePin pin, string source, IReadOnlyDictionary<string, string> environment, TextWriter log)
    {
        Result<string> dependencies = ProcessRunner.Run(["otool", "-L", tool], environment, log, source);
        if (!dependencies.Succeeded) return dependencies.Failure;
        ImmutableArray<string> paths = [.. Lines(dependencies.Value).Skip(1).Select(line => line.Trim().Split(' ')[0])];
        if (Check.That(paths.All(AllowedDependencies.Contains), "Unexpected dynamic dependency: " + dependencies.Value) is { } dependency) return dependency;
        Result<string> architecture = ProcessRunner.Run(["lipo", "-archs", tool], environment, log, source);
        if (!architecture.Succeeded) return architecture.Failure;
        if (Check.That(architecture.Value == "arm64", "Unexpected binary architecture") is { } arm64) return arm64;
        Result<string> banner = ProcessRunner.Run([tool, "listtemplates"], environment, log, source);
        if (!banner.Succeeded) return banner.Failure;
        string version = Lines(banner.Value)[0];
        if (Check.That(version.StartsWith(Banner + pin.Version + " ", StringComparison.Ordinal), "chdman version mismatch") is { } mismatch) return mismatch;
        return new Inspection(paths, version);
    }

    private static JsonObject Receipt(MamePin pin, Toolchain toolchain, string recipe, long epoch, ImmutableArray<string> generate,
        ImmutableArray<string> link, string tool, Inspection inspection) => new()
    {
        ["schemaVersion"] = 1,
        ["qualification"] = "local-unqualified",
        ["attestation"] = null,
        ["rid"] = "osx-arm64",
        ["pin"] = pin.Document.DeepClone(),
        ["compiler"] = toolchain.Compiler,
        ["macosSdk"] = toolchain.Sdk,
        ["makeVersion"] = toolchain.MakeVersion,
        ["recipeSha256"] = recipe,
        ["sourceDateEpoch"] = epoch,
        ["arguments"] = Strings(generate.Skip(1)),
        ["linkArguments"] = Strings(link),
        ["binary"] = new JsonObject
        {
            ["path"] = "native/chdman",
            ["sha256"] = Digest.Sha256File(tool),
            ["bytes"] = new FileInfo(tool).Length,
            ["dependencies"] = Strings(inspection.Dependencies),
        },
        ["version"] = inspection.Version,
    };

    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

    /// <summary>Python <c>str.splitlines()</c> for the line endings tools emit.</summary>
    private static string[] Lines(string text) => text.ReplaceLineEndings("\n").Split('\n');

    [GeneratedRegex(@"clang version ([0-9]+\.[0-9]+\.[0-9]+)")]
    private static partial Regex ClangVersion();
}
