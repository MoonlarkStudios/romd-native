using System.Security.Cryptography;
using Moonlark.Native.Engineering.Qualification;
using Moonlark.Native.Engineering.Tests.Native;
using Moonlark.Native.Engineering.Tests.TestSupport;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Qualification;

/// <summary>Downloaded toolchain bytes must match their reviewed authority before extraction.</summary>
public sealed class LinuxBuilderTests
{
    /// <summary>Both archive authorities fail closed after byte corruption.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArchiveBytesMustMatchAuthority(bool sha512)
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "tool.tar.gz");
        byte[] original = "reviewed tool archive"u8.ToArray();
        string digest = Convert.ToHexStringLower(sha512 ? SHA512.HashData(original) : SHA256.HashData(original));
        File.WriteAllBytes(path, original);
        Assert.Null(LinuxBuilder.VerifyArchive(path, digest, sha512));
        File.WriteAllBytes(path, "substituted tool archive"u8.ToArray());
        Assert.NotNull(LinuxBuilder.VerifyArchive(path, digest, sha512));
    }

    /// <summary>Missing archives cannot be mistaken for a completed download.</summary>
    [Fact]
    public void MissingArchiveFails()
    {
        using var directory = new TemporaryDirectory();
        Assert.NotNull(LinuxBuilder.VerifyArchive(Path.Combine(directory.Path, "missing"), new string('0', 64), false));
    }

    /// <summary>A linked archive is not an owned downloaded regular file.</summary>
    [Fact]
    public void LinkedArchiveFails()
    {
        using var directory = new TemporaryDirectory();
        string target = Path.Combine(directory.Path, "real");
        string link = Path.Combine(directory.Path, "link");
        File.WriteAllBytes(target, "bytes"u8.ToArray());
        File.CreateSymbolicLink(link, target);
        Assert.NotNull(LinuxBuilder.VerifyArchive(link, Convert.ToHexStringLower(SHA256.HashData("bytes"u8)), false));
    }
    /// <summary>Timed-out and failed attached Docker clients cannot leave their daemon-side container running.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContainerIsRemovedAfterFailureOrTimeout(bool timeout)
    {
        if (OperatingSystem.IsWindows()) return;
        using var directory = new TemporaryDirectory();
        string removed = Path.Combine(directory.Path, "removed");
        string script = "#!/bin/sh\nif [ \"$1\" = rm ]; then\n"
            + "  if [ \"$#\" != 3 ] || [ \"$2\" != --force ]; then exit 97; fi\n"
            + "  printf '%s' \"$3\" > " + FakeTools.Quote(removed) + "; exit 0; fi\n"
            + "if [ \"$1\" != run ] || [ \"$2\" != --cidfile ] || [ \"$4\" != --name ]; then exit 98; fi\n"
            + (timeout ? "/bin/sleep 30\n" : "exit 95\n");
        FakeTools.WriteExecutable(Path.Combine(directory.Path, "docker"), script);
        using var log = new StringWriter();
        var result = LinuxBuilder.RunContainer(["synthetic-image"], directory.Path, FakeTools.OnlyOnPath(directory.Path), log,
            timeout ? TimeSpan.FromMilliseconds(300) : TimeSpan.FromSeconds(30));
        Assert.False(result.Succeeded);
        Assert.Contains(timeout ? "timed out" : "95", result.Failure.Message, StringComparison.Ordinal);
        // The child may time out before initializing any marker; the parent records its target before launch.
        string run = Assert.Single(log.ToString().Split('\n'), line => line.StartsWith("$ docker run ", StringComparison.Ordinal));
        string name = run.Split(" --name ", StringSplitOptions.None)[1].Split(' ')[0];
        Assert.Matches("^moonlark-qualification-[a-f0-9]{32}$", name);
        Assert.True(File.Exists(removed));
        Assert.Equal(name, File.ReadAllText(removed));
    }
    /// <summary>A preexisting cache symlink must be rejected before a writable container mount is constructed.</summary>
    [Fact]
    public void RedirectedPackageCacheFailsBeforeHostWork()
    {
        using var root = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        string output = Path.Combine(root.Path, "artifacts", "qualification", "linux-arm64");
        Directory.CreateDirectory(output);
        Directory.CreateSymbolicLink(Path.Combine(output, "nuget"), outside.Path);
        var failure = LinuxBuilder.Run(root.Path, "linux-arm64", new Dictionary<string, string>(), TextWriter.Null);
        Assert.NotNull(failure);
        Assert.Contains("symlink", failure.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(outside.Path));
    }
}
