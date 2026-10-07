using Moonlark.Native.Engineering.Cli;
using Moonlark.Native.Engineering.Qualification;
using Moonlark.Native.Engineering.Tests.TestSupport;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Qualification;

/// <summary>Qualification commands reject redirected outputs before opening files or launching tools.</summary>
public sealed class QualificationCommandTests
{
    /// <summary>Neither a redirected directory nor an existing redirected leaf may receive preparation writes.</summary>
    [Theory]
    [InlineData("directory")]
    [InlineData("prepare.log")]
    [InlineData("mame.tar.gz")]
    public void FixturePreparationRejectsLinksBeforeWriting(string leaf)
    {
        using var root = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        TestRepository.CopyTo(root.Path, "eng/pins/mame.json");
        string directory = Path.Combine(root.Path, "artifacts", "qualification", "fixtures");
        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        string sentinel = Path.Combine(outside.Path, "sentinel");
        File.WriteAllText(sentinel, "unchanged");
        if (leaf == "directory") Directory.CreateSymbolicLink(directory, outside.Path);
        else
        {
            Directory.CreateDirectory(directory);
            File.CreateSymbolicLink(Path.Combine(directory, leaf), sentinel);
        }
        using var error = new StringWriter();
        var context = new CommandContext(root.Path, TextWriter.Null, error, new Dictionary<string, string>());
        Assert.Equal(1, QualificationCommand.Fixtures([], context));
        Assert.Contains("symlink", error.ToString(), StringComparison.Ordinal);
        Assert.Equal("unchanged", File.ReadAllText(sentinel));
        Assert.Equal([sentinel], Directory.GetFiles(outside.Path));
        if (leaf != "directory" && leaf != "prepare.log") Assert.False(File.Exists(Path.Combine(directory, "prepare.log")));
    }

    /// <summary>Ambient Git inputs are rejected before source or output work in either host command.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HostCommandsRejectGitOverrides(bool container)
    {
        using var root = new TemporaryDirectory();
        TestRepository.CopyTo(root.Path, "eng/pins/mame.json");
        using var error = new StringWriter();
        var environment = new Dictionary<string, string> { ["GIT_DIR"] = "/unreviewed/git" };
        var context = new CommandContext(root.Path, TextWriter.Null, error, environment);
        int exit = container ? QualificationCommand.Container(["--rid", "linux-arm64"], context) : QualificationCommand.Fixtures([], context);
        Assert.Equal(1, exit);
        Assert.Contains("Unrecorded build environment overrides", error.ToString(), StringComparison.Ordinal);
    }
    /// <summary>A failed subject preflight must remove a stale successful checksum file.</summary>
    [Fact]
    public void FailedSubjectPreparationInvalidatesStaleChecksums()
    {
        using var root = new TemporaryDirectory();
        string output = Path.Combine(root.Path, "artifacts", "signing", "subjects.sha256");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, "stale success");
        using var error = new StringWriter();
        var context = new CommandContext(root.Path, TextWriter.Null, error, new Dictionary<string, string>());
        Assert.Equal(1, QualificationCommand.Subjects(["--source-commit", new string('a', 40)], context));
        Assert.False(File.Exists(output));
    }

    /// <summary>Subject output links are rejected before a target can be removed or overwritten.</summary>
    [Fact]
    public void SubjectOutputCannotRedirectWrites()
    {
        using var root = new TemporaryDirectory();
        string output = Path.Combine(root.Path, "artifacts", "signing", "subjects.sha256");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        string sentinel = Path.Combine(root.Path, "sentinel");
        File.WriteAllText(sentinel, "unchanged");
        File.CreateSymbolicLink(output, sentinel);
        using var error = new StringWriter();
        var context = new CommandContext(root.Path, TextWriter.Null, error, new Dictionary<string, string>());
        Assert.Equal(1, QualificationCommand.Subjects(["--source-commit", new string('a', 40)], context));
        Assert.Contains("symlink", error.ToString(), StringComparison.Ordinal);
        Assert.Equal("unchanged", File.ReadAllText(sentinel));
    }
}
