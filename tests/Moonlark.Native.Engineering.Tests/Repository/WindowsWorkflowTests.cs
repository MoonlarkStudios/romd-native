using Moonlark.Native.Engineering.Cli;
using Moonlark.Native.Engineering.Repository;
using Moonlark.Native.Engineering.Tests.TestSupport;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Repository;

/// <summary>Windows qualification is explicit and cannot become per-change execution silently.</summary>
public sealed class WindowsWorkflowTests
{
    /// <summary>The manual job uses same-run fixtures, the selected x64 developer environment and retained evidence.</summary>
    [Fact]
    public void WindowsRunsOnlyOnManualDispatch()
    {
        string text = Workflow();
        Assert.Contains("  win-x64:\n", text, StringComparison.Ordinal);
        string job = text.Split("  win-x64:\n", StringSplitOptions.None)[1].Split("\n  prepare-linux-subjects:", StringSplitOptions.None)[0];
        Assert.Contains("    if: github.event_name == 'workflow_dispatch'\n", job, StringComparison.Ordinal);
        Assert.Contains("    needs: fixtures\n", job, StringComparison.Ordinal);
        Assert.Contains("    runs-on: windows-2022\n", job, StringComparison.Ordinal);
        Assert.Contains("          name: libchdr-synthetic-fixtures\n", job, StringComparison.Ordinal);
        Assert.Contains("        shell: cmd\n", job, StringComparison.Ordinal);
        Assert.Contains("-arch=x64 -host_arch=x64", job, StringComparison.Ordinal);
        Assert.Contains("qualification windows", job, StringComparison.Ordinal);
        Assert.Contains("generate --check", job, StringComparison.Ordinal);
        Assert.Contains("        if: always()\n        with:\n          name: libchdr-win-x64-evidence", job, StringComparison.Ordinal);
        Assert.Contains("        if: always()\n        with:\n          name: libchdr-win-x64-generation", job, StringComparison.Ordinal);
        Assert.DoesNotContain("write", job, StringComparison.Ordinal);
    }

    /// <summary>Removing or broadening the manual guard invalidates the exact reviewed workflow.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("    if: github.event_name != 'pull_request'\n")]
    public void WindowsSchedulingChangesFailClosed(string replacement)
    {
        using var copy = new TemporaryDirectory();
        TestRepository.CopyTo(copy.Path, "eng/pins", "eng/versions", ".github/workflows");
        string path = Path.Combine(copy.Path, ".github", "workflows", "native-libchdr.yml");
        string text = File.ReadAllText(path);
        const string guard = "    if: github.event_name == 'workflow_dispatch'\n";
        Assert.Contains(guard, text, StringComparison.Ordinal);
        File.WriteAllText(path, text.Replace(guard, replacement, StringComparison.Ordinal));
        Assert.NotNull(RepositoryCheck.Run(copy.Path, null));
    }

    /// <summary>The Windows command has a fixed RID and rejects options before collecting evidence.</summary>
    [Fact]
    public void WindowsCommandRejectsArguments()
    {
        using var error = new StringWriter();
        Assert.Equal(1, CommandRouter.Run(["qualification", "windows", "--rid", "linux-x64"], TextWriter.Null, error));
        Assert.Contains("qualification windows accepts no arguments", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>Only checkout receives the Git conversion settings; pinned submodule bytes remain unchanged.</summary>
    [Fact]
    public void WindowsCheckoutPreservesPinnedBytes()
    {
        string job = Workflow().Split("  win-x64:\n", StringSplitOptions.None)[1].Split("\n  prepare-linux-subjects:", StringSplitOptions.None)[0];
        Assert.Contains("      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1\n"
            + "        env:\n          GIT_CONFIG_COUNT: 2\n          GIT_CONFIG_KEY_0: core.autocrlf\n"
            + "          GIT_CONFIG_VALUE_0: 'false'\n          GIT_CONFIG_KEY_1: core.eol\n          GIT_CONFIG_VALUE_1: lf\n        with:\n", job, StringComparison.Ordinal);
        Assert.Equal(1, job.Split("GIT_CONFIG_COUNT", StringSplitOptions.None).Length - 1);
    }

    private static string Workflow() => File.ReadAllText(Path.Combine(TestRepository.Root, ".github", "workflows", "native-libchdr.yml"));
}
