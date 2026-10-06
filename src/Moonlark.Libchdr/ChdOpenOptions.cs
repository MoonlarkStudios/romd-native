namespace Moonlark.Libchdr;

/// <summary>Options for opening a read-only CHD file.</summary>
public sealed record ChdOpenOptions
{
    /// <summary>A ceiling on libchdr's compressed read-ahead memory, in bytes. Zero disables read-ahead.</summary>
    /// <remarks>A budget smaller than a hunk remains disabled; the file reports the budget actually taken.</remarks>
    public ulong ReadAheadBytes { get; init; }
}
