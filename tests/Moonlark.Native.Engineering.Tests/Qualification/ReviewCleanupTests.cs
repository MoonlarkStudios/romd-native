using System.Globalization;
using Moonlark.Native.Engineering.Qualification;
using Moonlark.Native.Engineering.Tests.Native;
using Moonlark.Native.Engineering.Tests.TestSupport;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Qualification;

/// <summary>Independent bounded review of cleanup failures; tools are local shell stand-ins.</summary>
public sealed class ReviewCleanupTests
{
    /// <summary>A cleanup failure cannot convert a successful container operation into accepted evidence.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(95, 0)]
    [InlineData(0, 97)]
    [InlineData(95, 97)]
    public void CleanupFailureIsReported(int runExit, int cleanupExit)
    {
        if (OperatingSystem.IsWindows()) return;
        using var directory = new TemporaryDirectory();
        string script = "#!/bin/sh\nif [ \"$1\" = rm ]; then exit " + cleanupExit.ToString(CultureInfo.InvariantCulture)
            + "; fi\nexit " + runExit.ToString(CultureInfo.InvariantCulture) + "\n";
        FakeTools.WriteExecutable(Path.Combine(directory.Path, "docker"), script);
        var result = LinuxBuilder.RunContainer(["synthetic-image"], directory.Path, FakeTools.OnlyOnPath(directory.Path), TextWriter.Null, TimeSpan.FromSeconds(3));
        Assert.Equal(runExit == 0 && cleanupExit == 0, result.Succeeded);
        if (cleanupExit != 0) Assert.Contains("Container cleanup failed", result.Failure.Message, StringComparison.Ordinal);
        if (runExit != 0) Assert.Contains("95", result.Failure.Message, StringComparison.Ordinal);
    }
}
