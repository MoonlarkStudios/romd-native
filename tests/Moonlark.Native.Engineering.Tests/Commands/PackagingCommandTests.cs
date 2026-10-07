using Moonlark.Native.Engineering.Cli;
using Moonlark.Native.Engineering.Tests.TestSupport;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Commands;

/// <summary>The package CLI selects fixed verified inputs and rejects ambiguous or release-like requests.</summary>
public sealed class PackagingCommandTests
{
    /// <summary>Invalid RID lists fail before creating outputs or starting a build.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("osx-arm64,osx-arm64")]
    [InlineData("linux-x64,")]
    [InlineData("linux-x64, win-x64")]
    [InlineData("LINUX-X64")]
    [InlineData("../../outside")]
    public void CandidateRejectsInvalidRids(string rids)
    {
        using var root = new TemporaryDirectory();
        using var error = new StringWriter();
        var context = new CommandContext(root.Path, TextWriter.Null, error, new Dictionary<string, string>());
        Assert.Equal(1, PackagingCommand.Candidate(["--rids", rids, "--output", "artifacts/candidate"], context));
        Assert.Contains("RID", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(root.Path));
    }

    /// <summary>Only the documented closed options are accepted; no version/qualification/native-path overrides.</summary>
    [Theory]
    [InlineData(false, "--version")]
    [InlineData(false, "--manifest")]
    [InlineData(false, "--qualified")]
    [InlineData(true, "--fixture")]
    [InlineData(true, "--source")]
    public void CommandsRejectExtraAuthority(bool consumer, string option)
    {
        using var root = new TemporaryDirectory();
        using var error = new StringWriter();
        var context = new CommandContext(root.Path, TextWriter.Null, error, new Dictionary<string, string>());
        int exit = consumer ? PackagingCommand.Consumer([option, "unreviewed"], context)
            : PackagingCommand.Candidate([option, "unreviewed"], context);
        Assert.Equal(1, exit);
        Assert.Contains("Unrecognized argument", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(root.Path));
    }

    /// <summary>Missing required values must fail before source or file access.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CommandsRequireExplicitInputs(bool consumer)
    {
        using var root = new TemporaryDirectory();
        using var error = new StringWriter();
        var context = new CommandContext(root.Path, TextWriter.Null, error, new Dictionary<string, string>());
        Assert.Equal(1, consumer ? PackagingCommand.Consumer([], context) : PackagingCommand.Candidate([], context));
        Assert.Contains("Missing required option", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(root.Path));
    }
}
