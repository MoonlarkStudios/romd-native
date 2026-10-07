using Moonlark.Native.Engineering.Repository;
using Moonlark.Native.Engineering.Tests.TestSupport;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Repository;

/// <summary>The reviewed Linux pipeline is an exact evidence and provenance contract, independently of the older source grammar.</summary>
public sealed class LinuxWorkflowTests
{
    /// <summary>Both real architectures depend on freshly produced fixtures and run generation and qualification.</summary>
    [Fact]
    public void LinuxPipelineProvidesBothArchitecturesAndInputs()
    {
        string text = File.ReadAllText(Path.Combine(TestRepository.Root, ".github", "workflows", "native-libchdr.yml"));
        Assert.Contains("    runs-on: macos-15\n", text, StringComparison.Ordinal);
        Assert.Contains("    runs-on: ubuntu-24.04\n", text, StringComparison.Ordinal);
        Assert.Contains("    runs-on: ubuntu-24.04-arm\n", text, StringComparison.Ordinal);
        Assert.Equal(2, text.Split("    needs: fixtures\n", StringSplitOptions.None).Length - 1);
        Assert.Contains("qualification container --rid linux-x64", text, StringComparison.Ordinal);
        Assert.Contains("qualification container --rid linux-arm64", text, StringComparison.Ordinal);
        Assert.Contains("qualification fixtures", text, StringComparison.Ordinal);
        Assert.Null(RepositoryCheck.Run(TestRepository.Root, null));
    }

    /// <summary>Failed drift checks retain their actual output without changing the qualification artifact layout.</summary>
    [Theory]
    [InlineData("linux-x64")]
    [InlineData("linux-arm64")]
    public void LinuxGenerationEvidenceSurvivesFailure(string rid)
    {
        string text = File.ReadAllText(Path.Combine(TestRepository.Root, ".github", "workflows", "native-libchdr.yml"));
        string job = text.Split("  " + rid + ":\n", StringSplitOptions.None)[1].Split("\n  linux-", StringSplitOptions.None)[0];
        Assert.Contains("        if: always()\n        with:\n          name: libchdr-" + rid + "-generation\n          path: artifacts/generation/libchdr\n          retention-days: 7", job, StringComparison.Ordinal);
    }

    /// <summary>Changing scheduling, identities, permissions, inputs or execution requires another reviewed contract.</summary>
    [Theory]
    [InlineData("contents: read", "contents: write")]
    [InlineData("  pull_request:\n", "  pull_request_target:\n")]
    [InlineData("    branches: [main, refactor/csharp-engineering]", "    branches: [main]")]
    [InlineData("runs-on: ubuntu-24.04-arm", "runs-on: windows-2022")]
    [InlineData("runs-on: ubuntu-24.04-arm", "runs-on: ubuntu-24.04")]
    [InlineData("    needs: fixtures\n", "")]
    [InlineData("qualification container --rid linux-arm64", "qualification container --rid linux-x64")]
    [InlineData("qualification fixtures", "repo check")]
    [InlineData("dotnet tool restore", "dotnet --info")]
    [InlineData("          submodules: true\n", "")]
    [InlineData("          fetch-depth: 0\n", "          fetch-depth: 1\n")]
    [InlineData("persist-credentials: false", "persist-credentials: true")]
    [InlineData("043fb46d1a93c77aae656e7c1c64a875d1fc6a0a", "v7")]
    [InlineData("3e5f45b2cfb9172054b4087a40e8e0b5a5461e7c", "v8")]
    [InlineData("          name: libchdr-synthetic-fixtures", "          name: unrelated-fixtures")]
    [InlineData("          retention-days: 7", "          retention-days: 90")]
    [InlineData("        if: always()", "        if: false")]
    public void LinuxContractChangesFailClosed(string before, string after)
    {
        using var copy = new TemporaryDirectory();
        TestRepository.CopyTo(copy.Path, "eng/pins", "eng/versions", ".github/workflows");
        string path = Path.Combine(copy.Path, ".github", "workflows", "native-libchdr.yml");
        string text = File.ReadAllText(path);
        Assert.Contains(before, text, StringComparison.Ordinal);
        File.WriteAllText(path, text.Replace(before, after, StringComparison.Ordinal));
        Assert.NotNull(RepositoryCheck.Run(copy.Path, null));
    }
}
