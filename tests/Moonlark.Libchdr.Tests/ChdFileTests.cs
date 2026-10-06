using System.Buffers.Binary;
using Xunit;

namespace Moonlark.Libchdr.Tests;

/// <summary>Qualifies chd File Tests.</summary>
public sealed class ChdFileTests
{
    /// <summary>Qualifies decodes Every Hunk And Range Exactly.</summary>
    [Theory]
    [InlineData("dvd-lzma")]
    [InlineData("dvd-zlib")]
    [InlineData("dvd-huff")]
    [InlineData("dvd-flac")]
    [InlineData("dvd-zstd")]
    [InlineData("dvd-none")]
    [InlineData("cd-cdlz")]
    [InlineData("cd-cdzl")]
    [InlineData("cd-cdfl")]
    [InlineData("cd-cdzs")]
    [InlineData("cd-subcode")]
    public void DecodesEveryHunkAndRangeExactly(string name)
    {
        byte[] logical = File.ReadAllBytes(NativeTestEnvironment.Fixture(name + ".logical"));
        using ChdFile file = NativeTestEnvironment.OpenFixture(name + ".chd");
        Assert.Equal((ulong)logical.Length, file.Header.LogicalBytes);
        Assert.False(file.Header.HasParent);
        Assert.Equal(0UL, file.ReadAheadBytes);
        byte[] hunk = new byte[file.Header.HunkBytes];
        for (uint index = 0; index < file.Header.HunkCount; index++)
        {
            file.ReadHunk(index, hunk);
            int offset = checked((int)(index * file.Header.HunkBytes));
            int take = Math.Min(hunk.Length, logical.Length - offset);
            Assert.True(logical.AsSpan(offset, take).SequenceEqual(hunk.AsSpan(0, take)));
        }
        byte[] range = new byte[hunk.Length + 73];
        for (int offset = 0; offset < logical.Length; offset += range.Length - 19)
        {
            Array.Fill(range, (byte)0xCC);
            int count = file.ReadAt(offset, range);
            Assert.Equal(Math.Min(range.Length, logical.Length - offset), count);
            Assert.True(logical.AsSpan(offset, count).SequenceEqual(range.AsSpan(0, count)));
            Assert.True(range.AsSpan(count).IndexOfAnyExcept((byte)0xCC) < 0);
        }
        Assert.Equal(0, file.ReadAt(logical.Length, range));
        Assert.Equal(0, file.ReadAt(long.MaxValue, range));
    }

    /// <summary>Qualifies validates Bounds And Alignment Before Calling Native.</summary>
    [Fact]
    public void ValidatesBoundsAndAlignmentBeforeCallingNative()
    {
        using ChdFile file = NativeTestEnvironment.OpenFixture();
        byte[] memory = new byte[file.Header.HunkBytes + 2];
        Assert.Throws<ArgumentOutOfRangeException>(() => file.ReadHunk(file.Header.HunkCount, memory));
        Assert.Throws<ArgumentException>(() => file.ReadHunk(0, memory.AsSpan(0, (int)file.Header.HunkBytes - 1)));
        Assert.Throws<ArgumentException>(() => file.ReadHunk(0, memory.AsSpan(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => file.ReadAt(-1, memory));
        file.ReadHunk(0, memory.AsSpan(2));
    }

    /// <summary>Qualifies metadata Never Silently Truncates Or Allocates Per Entry.</summary>
    [Fact]
    public void MetadataNeverSilentlyTruncatesOrAllocatesPerEntry()
    {
        using ChdFile file = NativeTestEnvironment.OpenFixture("cd-cdlz.chd");
        Assert.False(file.TryGetMetadata(ChdMetadataTag.CdTrackV2, 0, [], out ChdMetadataInfo info));
        Assert.True(info.Length > 1);
        byte[] shortBuffer = Enumerable.Repeat((byte)0xCC, checked((int)info.Length - 1)).ToArray();
        Assert.False(file.TryGetMetadata(ChdMetadataTag.CdTrackV2, 0, shortBuffer, out ChdMetadataInfo same));
        Assert.Equal(info, same);
        Assert.All(shortBuffer, value => Assert.Equal(0xCC, value));
        byte[] payload = new byte[info.Length];
        Assert.True(file.TryGetMetadata(ChdMetadataTag.CdTrackV2, 0, payload, out same));
        Assert.Equal(0, payload[^1]);
        Assert.False(file.TryGetMetadata(ChdMetadataTag.Dvd, 0, payload, out same));
        Assert.Equal(default, same);
        _ = CountMetadata(file);
        long start = GC.GetAllocatedBytesForCurrentThread();
        int count = 0;
        for (int repeat = 0; repeat < 1024; repeat++) count += CountMetadata(file);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(2048, count);
        Assert.Equal(0, bytes);
    }

    /// <summary>Qualifies native Read Ahead Never Exceeds Ceiling.</summary>
    [Theory]
    [InlineData(0UL)]
    [InlineData(32768UL)]
    [InlineData(1048576UL)]
    public void NativeReadAheadNeverExceedsCeiling(ulong ceiling)
    {
        using ChdFile file = ChdFile.Open(NativeTestEnvironment.Fixture("dvd-zstd.chd"), new ChdOpenOptions { ReadAheadBytes = ceiling });
        Assert.InRange(file.ReadAheadBytes, 0UL, ceiling);
        byte[] buffer = new byte[file.Header.HunkBytes];
        file.ReadHunk(0, buffer);
    }

    /// <summary>Qualifies steady State Hunk And Range Reads Allocate Nothing.</summary>
    [Theory]
    [InlineData("dvd-lzma.chd")]
    [InlineData("dvd-flac.chd")]
    [InlineData("dvd-zstd.chd")]
    [InlineData("cd-cdlz.chd")]
    public void SteadyStateHunkAndRangeReadsAllocateNothing(string name)
    {
        using ChdFile file = NativeTestEnvironment.OpenFixture(name);
        byte[] hunk = new byte[file.Header.HunkBytes];
        byte[] range = new byte[257];
        for (int index = 0; index < 256; index++) { file.ReadHunk((uint)index % file.Header.HunkCount, hunk); file.ReadAt(index % 2, range); }
        long start = GC.GetAllocatedBytesForCurrentThread();
        int read = 0;
        for (int index = 0; index < 2048; index++) { file.ReadHunk((uint)index % file.Header.HunkCount, hunk); read += file.ReadAt(index % 2, range); }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.Equal(2048 * range.Length, read);
        Assert.Equal(0, bytes);
    }

    /// <summary>Qualifies source Fault Escapes Try Open With Original Identity Even If Cleanup Fails.</summary>
    [Fact]
    public void SourceFaultEscapesTryOpenWithOriginalIdentityEvenIfCleanupFails()
    {
        _ = NativeTestEnvironment.Root;
        var error = new ChdException(ChdError.ReadError, "source sentinel");
        var source = new TestSource([], error, new IOException("cleanup sentinel"));
        Assert.Same(error, Assert.Throws<ChdException>(() => ChdFile.TryOpen(source, false, out _, out _)));
        Assert.Equal(1, source.Disposals);
    }

    /// <summary>Qualifies source Fault During Native Read Escapes With Original Identity.</summary>
    [Fact]
    public void SourceFaultDuringNativeReadEscapesWithOriginalIdentity()
    {
        using var source = new TestSource(File.ReadAllBytes(NativeTestEnvironment.Fixture("dvd-zstd.chd")));
        using ChdFile file = ChdFile.Open(source, true);
        var fault = new IOException("decode source sentinel");
        source.ReadFault = fault;
        byte[] hunk = new byte[file.Header.HunkBytes];
        Assert.Same(fault, Assert.Throws<IOException>(() => file.ReadHunk(0, hunk)));
    }

    /// <summary>Qualifies stream Adapter Preserves Position And Explicit Ownership.</summary>
    [Fact]
    public void StreamAdapterPreservesPositionAndExplicitOwnership()
    {
        var stream = new MemoryStream(File.ReadAllBytes(NativeTestEnvironment.Fixture("dvd-zstd.chd")));
        stream.Position = 37;
        using (ChdFile file = ChdFile.Open(stream, true))
        {
            file.ReadHunk(0, new byte[file.Header.HunkBytes]);
            Assert.Equal(37, stream.Position);
        }
        Assert.True(stream.CanRead);
        using (ChdFile file = ChdFile.Open(stream, false)) _ = file.Header;
        Assert.False(stream.CanRead);
        stream.Dispose();
    }

    /// <summary>Qualifies rejects Malformed Envelope Without Entering Native Parser.</summary>
    [Theory]
    [InlineData("magic")]
    [InlineData("version")]
    [InlineData("length")]
    [InlineData("zero-hunk")]
    [InlineData("zero-unit")]
    [InlineData("logical-overflow")]
    [InlineData("metadata-outside")]
    [InlineData("metadata-cycle")]
    [InlineData("metadata-truncated")]
    public void RejectsMalformedEnvelopeWithoutEnteringNativeParser(string mutation)
    {
        byte[] bytes = File.ReadAllBytes(NativeTestEnvironment.Fixture("dvd-zstd.chd"));
        int metadata = checked((int)BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(48)));
        switch (mutation)
        {
            case "magic": bytes[0] = 0; break;
            case "version": BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12), 100); break;
            case "length": BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 125); break;
            case "zero-hunk": BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(56), 0); break;
            case "zero-unit": BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(60), 0); break;
            case "logical-overflow": BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(32), ulong.MaxValue); break;
            case "metadata-outside": BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(48), (ulong)bytes.Length); break;
            case "metadata-cycle": BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(metadata + 8), (ulong)metadata); break;
            case "metadata-truncated": BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(metadata + 4), 0x01FFFFFF); break;
        }
        using var source = new TestSource(bytes);
        Assert.False(ChdFile.TryOpen(source, false, out ChdFile? file, out ChdError error));
        Assert.Null(file);
        Assert.NotEqual(ChdError.None, error);
        Assert.Equal(1, source.Disposals);
    }

    /// <summary>Qualifies disposal Invalidates Members And Existing Enumerator.</summary>
    [Fact]
    public void DisposalInvalidatesMembersAndExistingEnumerator()
    {
        ChdFile file = NativeTestEnvironment.OpenFixture();
        ChdMetadataEnumerator entries = file.EnumerateMetadata();
        Assert.True(entries.MoveNext());
        file.Dispose();
        file.Dispose();
        Assert.Throws<ObjectDisposedException>(() => file.Header);
        Assert.Throws<ObjectDisposedException>(() => file.ReadAheadBytes);
        Assert.Throws<ObjectDisposedException>(() => file.ReadHunk(0, []));
        Assert.Throws<ObjectDisposedException>(() => file.ReadAt(0, []));
        Assert.Throws<ObjectDisposedException>(() => file.EnumerateMetadata());
        Assert.Throws<ObjectDisposedException>(() => file.TryGetMetadata(ChdMetadataTag.Dvd, 0, [], out _));
        Assert.Throws<ObjectDisposedException>(() => entries.Current);
        Assert.Throws<ObjectDisposedException>(() => entries.MoveNext());
        Assert.Throws<InvalidOperationException>(() => default(ChdMetadataEnumerator).MoveNext());
    }

    private static int CountMetadata(ChdFile file)
    {
        int count = 0;
        foreach (ChdMetadataInfo info in file.EnumerateMetadata()) { if (info.Length > 0) count++; }
        return count;
    }

    private sealed class TestSource(byte[] bytes, Exception? readFault = null, Exception? closeFault = null) : IChdDataSource
    {
        internal Exception? ReadFault { get; set; } = readFault;
        internal int Disposals { get; private set; }
        public long Length { get { if (ReadFault is { } error) throw error; return bytes.LongLength; } }
        public int Read(long offset, Span<byte> destination)
        {
            if (ReadFault is { } error) throw error;
            int take = (int)Math.Min(destination.Length, Math.Max(0, bytes.LongLength - offset));
            bytes.AsSpan(checked((int)offset), take).CopyTo(destination);
            return take;
        }
        /// <summary>Qualifies dispose.</summary>
        public void Dispose() { Disposals++; if (closeFault is { } error) throw error; }
    }
}
