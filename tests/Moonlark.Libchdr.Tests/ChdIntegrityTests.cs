using Xunit;

namespace Moonlark.Libchdr.Tests;

/// <summary>Qualifies chd Integrity Tests.</summary>
public sealed class ChdIntegrityTests
{
    /// <summary>Qualifies matches Independently Verified Chdman Hashes.</summary>
    [Theory]
    [InlineData("dvd-lzma.chd")]
    [InlineData("dvd-zlib.chd")]
    [InlineData("dvd-huff.chd")]
    [InlineData("dvd-flac.chd")]
    [InlineData("dvd-zstd.chd")]
    [InlineData("cd-cdlz.chd")]
    [InlineData("cd-cdzl.chd")]
    [InlineData("cd-cdfl.chd")]
    [InlineData("cd-cdzs.chd")]
    [InlineData("cd-subcode.chd")]
    public void MatchesIndependentlyVerifiedChdmanHashes(string name)
    {
        using ChdFile file = NativeTestEnvironment.OpenFixture(name);
        ChdIntegrityResult result = ChdIntegrity.Verify(file);
        Assert.True(result.IsVerified);
        Assert.Equal(file.Header.RawSha1, result.ComputedRawSha1);
        Assert.Equal(file.Header.OverallSha1, result.ComputedOverallSha1);
    }

    /// <summary>Qualifies missing Stored Hashes Are Explicitly Unverified.</summary>
    [Fact]
    public void MissingStoredHashesAreExplicitlyUnverified()
    {
        using ChdFile file = NativeTestEnvironment.OpenFixture("dvd-none.chd");
        ChdIntegrityResult result = ChdIntegrity.Verify(file);
        Assert.False(result.IsVerified);
        Assert.False(result.HasStoredRawSha1);
        Assert.False(result.HasStoredOverallSha1);
        Assert.False(result.RawMatches);
        Assert.False(result.OverallMatches);
        Assert.Equal("5b5cb6873a0de0e512547d8d6815d0be92708ec6", result.ComputedRawSha1.ToString());
        Assert.Equal("52a5c08c5618bc49328a6c769ca7bea735d4e5e1", result.ComputedOverallSha1.ToString());
    }

    /// <summary>Qualifies decoding Success Does Not Hide Missing Or Mismatching Hash.</summary>
    [Theory]
    [InlineData("negative-raw-mismatch.chd", false, true)]
    [InlineData("negative-overall-mismatch.chd", true, false)]
    [InlineData("negative-raw-missing.chd", false, true)]
    public void DecodingSuccessDoesNotHideMissingOrMismatchingHash(string name, bool raw, bool overall)
    {
        using ChdFile file = NativeTestEnvironment.OpenFixture(name);
        ChdIntegrityResult result = ChdIntegrity.Verify(file);
        Assert.False(result.IsVerified);
        Assert.Equal(raw, result.RawMatches);
        Assert.Equal(overall, result.OverallMatches);
    }

    /// <summary>Qualifies checks Cancellation And Lifetime.</summary>
    [Fact]
    public void ChecksCancellationAndLifetime()
    {
        ChdFile file = NativeTestEnvironment.OpenFixture();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => ChdIntegrity.Verify(file, cancel.Token));
        file.Dispose();
        Assert.Throws<ObjectDisposedException>(() => ChdIntegrity.Verify(file));
        Assert.Throws<ArgumentNullException>(() => ChdIntegrity.Verify(null!));
    }
}
