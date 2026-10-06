namespace Moonlark.Libchdr;

/// <summary>A codec identifier in a version 5 CHD header.</summary>
public enum ChdCodec : uint
{
    /// <summary>No compression codec.</summary>
    None = 0,
    /// <summary>Zlib-format compression.</summary>
    Zlib = 0x7A6C6962,
    /// <summary>LZMA compression.</summary>
    Lzma = 0x6C7A6D61,
    /// <summary>Huffman compression.</summary>
    Huffman = 0x68756666,
    /// <summary>FLAC audio compression.</summary>
    Flac = 0x666C6163,
    /// <summary>Zstandard compression.</summary>
    Zstd = 0x7A737464,
    /// <summary>Audio/video Huffman compression.</summary>
    AvHuffman = 0x61766875,
    /// <summary>CD-sector preprocessing with zlib-format compression.</summary>
    CdZlib = 0x63647A6C,
    /// <summary>CD-sector preprocessing with LZMA compression.</summary>
    CdLzma = 0x63646C7A,
    /// <summary>CD-sector preprocessing with FLAC compression.</summary>
    CdFlac = 0x6364666C,
    /// <summary>CD-sector preprocessing with Zstandard compression.</summary>
    CdZstd = 0x63647A73,
}
