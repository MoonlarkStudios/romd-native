namespace Moonlark.Libchdr;

/// <summary>A seekable source with independent positional reads.</summary>
/// <remarks>Reads must not depend on a shared cursor. The wrapper owns disposal unless opened with leaveOpen.</remarks>
public interface IChdDataSource : IDisposable
{
    /// <summary>The byte length of the source.</summary>
    long Length { get; }

    /// <summary>Reads at an absolute offset; a short read or zero at EOF is permitted.</summary>
    /// <param name="offset">A nonnegative absolute byte offset.</param>
    /// <param name="destination">The memory to fill during this call.</param>
    /// <returns>The number of bytes read, between zero and the destination length.</returns>
    /// <remarks>The source must not retain the span. Exceptions are restored on the managed side of a native call.</remarks>
    int Read(long offset, Span<byte> destination);
}
