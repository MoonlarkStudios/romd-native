using Xunit;

namespace Moonlark.Libchdr.Tests;

/// <summary>Qualifies chd Stream Tests.</summary>
public sealed class ChdStreamTests
{
    /// <summary>Qualifies seeks And Reads Across Hunks With Normal Stream Eof.</summary>
    [Fact]
    public void SeeksAndReadsAcrossHunksWithNormalStreamEof()
    {
        using ChdFile file = NativeTestEnvironment.OpenFixture();
        using var stream = new ChdStream(file, true);
        byte[] source = File.ReadAllBytes(NativeTestEnvironment.Fixture("dvd-source.iso"));
        Assert.True(stream.CanRead);
        Assert.True(stream.CanSeek);
        Assert.False(stream.CanWrite);
        Assert.Equal(source.Length, stream.Length);
        Assert.Equal(file.Header.HunkBytes - 3, stream.Seek(file.Header.HunkBytes - 3, SeekOrigin.Begin));
        byte[] range = new byte[117];
        Assert.Equal(range.Length, stream.Read(range, 0, range.Length));
        Assert.True(source.AsSpan((int)file.Header.HunkBytes - 3, range.Length).SequenceEqual(range));
        Assert.Equal(source.Length - 1, stream.Seek(-1, SeekOrigin.End));
        Assert.Equal(source[^1], stream.ReadByte());
        Assert.Equal(-1, stream.ReadByte());
        stream.Position = long.MaxValue;
        Assert.Equal(0, stream.Read(range));
        Assert.Throws<IOException>(() => stream.Seek(1, SeekOrigin.Current));
        Assert.Throws<IOException>(() => stream.Seek(-1, SeekOrigin.Begin));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(0, (SeekOrigin)7));
    }

    /// <summary>Qualifies explicit Ownership And All Overridden Operations Respect Disposal.</summary>
    [Fact]
    public async Task ExplicitOwnershipAndAllOverriddenOperationsRespectDisposal()
    {
        using ChdFile file = NativeTestEnvironment.OpenFixture();
        var kept = new ChdStream(file, true);
        kept.Dispose();
        _ = file.Header;
        Assert.Throws<ObjectDisposedException>(() => kept.CanRead);
        Assert.Throws<ObjectDisposedException>(() => kept.CanSeek);
        Assert.Throws<ObjectDisposedException>(() => kept.CanWrite);
        Assert.Throws<ObjectDisposedException>(() => kept.CanTimeout);
        Assert.Throws<ObjectDisposedException>(() => kept.Position);
        Assert.Throws<ObjectDisposedException>(() => kept.Length);
        Assert.Throws<ObjectDisposedException>(() => kept.Flush());
        Assert.Throws<ObjectDisposedException>(() => kept.Write([]));
        Assert.Throws<ObjectDisposedException>(() => kept.SetLength(0));
        Assert.Throws<ObjectDisposedException>(() => kept.Read([]));
        Assert.Throws<ObjectDisposedException>(() => kept.Seek(0, SeekOrigin.Begin));
        Assert.Throws<ObjectDisposedException>(() => kept.ReadTimeout);
        Assert.Throws<ObjectDisposedException>(() => kept.WriteTimeout);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => kept.ReadAsync(Memory<byte>.Empty, default).AsTask());
        var owned = new ChdStream(file);
        owned.Dispose();
        Assert.Throws<ObjectDisposedException>(() => file.Header);
    }

    /// <summary>Qualifies never Queues Fake Asynchronous Native Reads.</summary>
    [Fact]
    public async Task NeverQueuesFakeAsynchronousNativeReads()
    {
        using ChdFile file = NativeTestEnvironment.OpenFixture();
        using Stream stream = new ChdStream(file, true);
        await Assert.ThrowsAsync<NotSupportedException>(() => stream.ReadAsync(Memory<byte>.Empty).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => stream.ReadAsync([], 0, 0));
        Assert.Throws<NotSupportedException>(() => stream.BeginRead([], 0, 0, null, null));
        await Assert.ThrowsAsync<NotSupportedException>(() => stream.CopyToAsync(Stream.Null));
        Assert.Throws<NotSupportedException>(() => stream.Write([]));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
    }
}
