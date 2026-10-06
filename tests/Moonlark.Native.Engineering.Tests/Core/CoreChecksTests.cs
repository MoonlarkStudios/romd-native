using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Moonlark.Native.Engineering.Cli;
using Moonlark.Native.Engineering.Core;
using Moonlark.Native.Engineering.Tests.TestSupport;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Core;

/// <summary>Environment, host, authority, path, JSON and argument checks shared by every command.</summary>
public sealed class CoreChecksTests
{
    private static readonly string[] ImplicitInputNames =
    [
        "CC", "CXX", "AS", "AR", "LD", "NM", "RANLIB", "RC", "CFLAGS", "CPPFLAGS", "CXXFLAGS", "LDFLAGS", "CL", "_CL_",
        "LINK", "_LINK_", "CPATH", "C_INCLUDE_PATH", "CPLUS_INCLUDE_PATH", "OBJC_INCLUDE_PATH", "LIBRARY_PATH",
        "COMPILER_PATH", "GCC_EXEC_PREFIX", "SDKROOT", "MACOSX_DEPLOYMENT_TARGET", "LD_PRELOAD", "LD_LIBRARY_PATH",
        "DYLD_INSERT_LIBRARIES", "DYLD_LIBRARY_PATH", "DYLD_FRAMEWORK_PATH", "CCC_OVERRIDE_OPTIONS",
        "CMAKE_TOOLCHAIN_FILE", "GIT_INDEX_FILE", "GIT_WORK_TREE", "GIT_CONFIG_COUNT", "GIT_EDITOR", "cl",
    ];

    /// <summary>Every implicit compiler, linker, search-path and Git input that must be rejected.</summary>
    public static TheoryData<string> ImplicitInputs { get; } = new(ImplicitInputNames);

    /// <summary>Unrecorded tool inputs are rejected instead of silently influencing a build.</summary>
    [Theory]
    [MemberData(nameof(ImplicitInputs))]
    public void ImplicitCompilerAndGitOverridesAreRejected(string name)
    {
        Result<IReadOnlyDictionary<string, string>> result = BuildEnvironment.Create(Environment(("PATH", "existing tools"), (name, "unrecorded")), 12345);
        Assert.Contains("environment overrides", result.Failure.Message, StringComparison.Ordinal);
    }

    /// <summary>The recorded epoch is added and the display pager removed.</summary>
    [Fact]
    public void RecordedEpochIsAddedAndPagerDropped()
    {
        Assert.Equal(Environment(("PATH", "existing tools"), ("SOURCE_DATE_EPOCH", "12345")),
            BuildEnvironment.Create(Environment(("PATH", "existing tools")), 12345).Value);
        Assert.Equal(Environment(("PATH", "existing tools")),
            BuildEnvironment.Create(Environment(("PATH", "existing tools"), ("GIT_PAGER", "agent pager"))).Value);
    }

    /// <summary>Each supported host maps to exactly one RID.</summary>
    [Theory]
    [InlineData("Darwin", Architecture.Arm64, "osx-arm64")]
    [InlineData("Linux", Architecture.X64, "linux-x64")]
    [InlineData("Linux", Architecture.Arm64, "linux-arm64")]
    [InlineData("Windows", Architecture.X64, "win-x64")]
    public void SupportedNativeHostsAreExplicit(string system, Architecture architecture, string rid) =>
        Assert.Equal(rid, NativeRids.Resolve(system, architecture).Value);

    /// <summary>Unsupported hosts, including Intel macOS, fail explicitly.</summary>
    [Fact]
    public void UnsupportedNativeHostFails() =>
        Assert.Contains("Unsupported native host", NativeRids.Resolve("Darwin", Architecture.X64).Failure.Message, StringComparison.Ordinal);

    /// <summary>Noncanonical SemVer forms are rejected.</summary>
    [Theory]
    [InlineData("01.0.0")]
    [InlineData("1.02.0")]
    [InlineData("1.0.03")]
    [InlineData("1.0")]
    [InlineData("1.0.0-preview.01")]
    [InlineData("1.0.0-")]
    public void SemVerRejectsNoncanonicalNumericComponents(string version) => Assert.NotNull(Authorities.ValidateSemVer(version));

    /// <summary>Canonical stable, prerelease and metadata forms are accepted.</summary>
    [Theory]
    [InlineData("1.0.0-preview.1")]
    [InlineData("1.2.3")]
    [InlineData("2.0.0-rc.0")]
    [InlineData("1.0.0+commit")]
    public void SemVerAcceptsPrereleaseAndStableFamilies(string version) => Assert.Null(Authorities.ValidateSemVer(version));

    /// <summary>The pin must agree with props and keep the closed header and feature contracts.</summary>
    [Theory]
    [InlineData("commit", "\"0000000000000000000000000000000000000000\"", "commit mismatch")]
    [InlineData("headers", "{\"../../escape\": \"0000000000000000000000000000000000000000000000000000000000000000\"}", "two ABI headers")]
    [InlineData("features", "[\"raw-sectors\"]", "features")]
    public void PinPropsAndClosedHeaderListAreRequired(string property, string value, string message)
    {
        using var directory = new TemporaryDirectory();
        TestRepository.CopyTo(directory.Path, Authorities.PinPath, Authorities.PropsPath);
        string path = Path.Combine(directory.Path, Authorities.PinPath);
        JsonObject pin = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        pin[property] = JsonNode.Parse(value);
        File.WriteAllText(path, pin.ToJsonString());
        Assert.Contains(message, Authorities.ReadLibchdr(directory.Path).Failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Outputs cannot leave artifacts by parent paths, sibling prefixes, symlinks or files.</summary>
    [Fact]
    public void OutputCannotEscapeArtifactsThroughParentSiblingOrSymlink()
    {
        using var directory = new TemporaryDirectory();
        string artifacts = Path.Combine(directory.Path, "artifacts");
        Assert.Contains("artifacts", ArtifactsPath.ValidateDirectory(Path.Combine(directory.Path, "src"), directory.Path).Failure.Message, StringComparison.Ordinal);
        Assert.False(ArtifactsPath.ValidateDirectory(Path.Combine(directory.Path, "artifacts-sibling"), directory.Path).Succeeded);
        Assert.False(ArtifactsPath.ValidateDirectory(Path.Combine(artifacts, "..", "src"), directory.Path).Succeeded);
        Directory.CreateDirectory(artifacts);
        Directory.CreateSymbolicLink(Path.Combine(artifacts, "escape"), directory.Path);
        Assert.Contains("symlink", ArtifactsPath.ValidateDirectory(Path.Combine(artifacts, "escape", "out"), directory.Path).Failure.Message, StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(artifacts, "file"), "");
        Assert.Contains("directory", ArtifactsPath.ValidateDirectory(Path.Combine(artifacts, "file", "out"), directory.Path).Failure.Message, StringComparison.Ordinal);
        Assert.True(ArtifactsPath.ValidateDirectory(Path.Combine(artifacts, "new", "nested"), directory.Path).Succeeded);
    }

    /// <summary>A symlinked artifacts root cannot redirect outputs into source.</summary>
    [Fact]
    public void ArtifactsRootCannotRedirectOutputsIntoSource()
    {
        using var directory = new TemporaryDirectory();
        string source = Directory.CreateDirectory(Path.Combine(directory.Path, "src")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(directory.Path, "artifacts"), source);
        Assert.Contains("artifacts", ArtifactsPath.ValidateDirectory(Path.Combine(directory.Path, "artifacts", "libchdr"), directory.Path).Failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Symlinks above the repository root are resolved rather than rejected.</summary>
    [Fact]
    public void SymlinkedRootAboveArtifactsIsResolvedNotRejected()
    {
        using var directory = new TemporaryDirectory();
        string real = Directory.CreateDirectory(Path.Combine(directory.Path, "real")).FullName;
        string alias = Path.Combine(directory.Path, "alias");
        Directory.CreateSymbolicLink(alias, real);
        Result<string> result = ArtifactsPath.ValidateDirectory(Path.Combine(alias, "artifacts", "out"), alias);
        Assert.Equal(Path.Combine(ArtifactsPath.Resolve(real), "artifacts", "out"), result.Value);
    }

    /// <summary>A default log leaf must be absent or a regular file.</summary>
    [Fact]
    public void DefaultLogCannotBeASymlinkOrDirectory()
    {
        using var directory = new TemporaryDirectory();
        string target = Path.Combine(directory.Path, "source.cs");
        File.WriteAllText(target, "sentinel");
        string link = Path.Combine(directory.Path, "build.log");
        File.CreateSymbolicLink(link, target);
        Assert.NotNull(ArtifactsPath.ValidateLogFile(link));
        Assert.NotNull(ArtifactsPath.ValidateLogFile(directory.Path));
        Assert.Null(ArtifactsPath.ValidateLogFile(Path.Combine(directory.Path, "fresh.log")));
    }

    /// <summary>Recipe identity JSON is ordinally sorted, compact and ASCII.</summary>
    [Fact]
    public void CanonicalJsonSortsKeysOrdinallyAndEscapesNonAscii()
    {
        JsonNode node = JsonNode.Parse("{\"b\":[1,{\"z\":true,\"a\":null}],\"B\":\"é\",\"a\":\"x\"}")!;
        Assert.Equal("{\"B\":\"\\u00E9\",\"a\":\"x\",\"b\":[1,{\"a\":null,\"z\":true}]}", Encoding.ASCII.GetString(CanonicalJson.Serialize(node)));
    }

    /// <summary>Blob identity matches git hash-object.</summary>
    [Fact]
    public void GitBlobIdentityMatchesGit()
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "blob.txt");
        File.WriteAllText(path, "int checked_source = 1;\n");
        Assert.Equal(ProcessRunner.Run(["git", "hash-object", path], TestRepository.CleanEnvironment).Value,
            Digest.GitBlobSha1(File.ReadAllBytes(path)));
    }

    /// <summary>Unknown, repeated, valueless and positional arguments fail.</summary>
    [Theory]
    [InlineData("--unknown", "x")]
    [InlineData("--tag", "a", "--tag", "b")]
    [InlineData("--tag")]
    [InlineData("positional")]
    public void ArgumentGrammarIsClosed(params string[] arguments) =>
        Assert.False(Arguments.Parse(arguments, ["--tag"], ["--check"]).Succeeded);

    /// <summary>Required options are enforced and flags parse.</summary>
    [Fact]
    public void RequiredOptionsAndFlagsParse()
    {
        Assert.Contains("--rid", Arguments.Parse(["--check"], ["--rid"], ["--check"], ["--rid"]).Failure.Message, StringComparison.Ordinal);
        ParsedArguments parsed = Arguments.Parse(["--rid", "osx-arm64", "--check"], ["--rid"], ["--check"], ["--rid"]).Value;
        Assert.Equal("osx-arm64", parsed.Option("--rid"));
        Assert.True(parsed.Flag("--check"));
    }

    private static Dictionary<string, string> Environment(params (string Name, string Value)[] values) =>
        values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);
}
