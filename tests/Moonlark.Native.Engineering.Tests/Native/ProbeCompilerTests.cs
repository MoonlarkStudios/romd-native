using Moonlark.Native.Engineering.Native;
using Xunit;

namespace Moonlark.Native.Engineering.Tests.Native;

/// <summary>MSVC compiler evidence comes from CMake and executed target macros, without unsupported GNU switches.</summary>
public sealed class ProbeCompilerTests
{
    /// <summary>Neither MSVC description needs to invoke cl with unsupported --version or -dumpmachine switches.</summary>
    [Theory]
    [InlineData("19.44.35217.0")]
    [InlineData("19.50.35718.0")]
    public void MsvcUsesTheMeasuredCMakeIdentityAndValidatedProbeTarget(string version)
    {
        var compiler = new CompilerIdentity("MSVC", version, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing-cl.exe"));
        var environment = new Dictionary<string, string>();
        using var log = new StringWriter();
        Assert.Equal($"MSVC {version} (CMake toolchains reply)", LayoutProbe.CompilerVersion(compiler, environment, log).Value);
        Assert.Equal("win-x64", LayoutProbe.CompilerTarget(compiler, "win-x64", environment, log).Value);
        Assert.Equal("", log.ToString());
    }

    /// <summary>A future compiler must not silently omit the required probe.</summary>
    [Fact]
    public void UnknownCompilerCannotOmitTheProbe() => Assert.False(LayoutProbe.IsBuiltBy(new CompilerIdentity("Unknown", "1", "cc")));
}
