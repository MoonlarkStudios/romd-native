using System.Buffers.Binary;
using Moonlark.Libchdr.Interop;

namespace Moonlark.Libchdr.Internal;

internal static class ChdHeaderReader
{
    internal static MetadataEntry[] ValidateEnvelope(ChdSourceContext source)
    {
        if (source.SourceLength < 16) throw new ChdValidationException(ChdError.InvalidFile, "open truncated header");
        Span<byte> header = stackalloc byte[124];
        ChdMetadataIndex.ReadExactly(source, 0, header[..16]);
        if (!header[..8].SequenceEqual("MComprHD"u8)) throw new ChdValidationException(ChdError.InvalidData, "open header signature");
        uint version = BinaryPrimitives.ReadUInt32BigEndian(header[12..]);
        uint expected = version switch { 1 => 76, 2 => 80, 3 => 120, 4 => 108, 5 => 124, _ => 0 };
        if (expected == 0) throw new ChdValidationException(ChdError.UnsupportedVersion, $"open version {version}");
        if (BinaryPrimitives.ReadUInt32BigEndian(header[8..]) != expected || source.SourceLength < expected)
            throw new ChdValidationException(ChdError.InvalidData, "open header length");
        ChdMetadataIndex.ReadExactly(source, 0, header[..(int)expected]);
        if (version == 5)
        {
            ulong logical = BinaryPrimitives.ReadUInt64BigEndian(header[32..]);
            uint hunk = BinaryPrimitives.ReadUInt32BigEndian(header[56..]);
            uint unit = BinaryPrimitives.ReadUInt32BigEndian(header[60..]);
            if (logical is 0 or > long.MaxValue || hunk is 0 or > int.MaxValue || unit == 0 || logical / hunk + (logical % hunk == 0 ? 0UL : 1UL) > uint.MaxValue)
                throw new ChdValidationException(ChdError.InvalidData, "open logical/hunk/unit geometry");
            // Validate termination before native header guessing/enumeration can
            // hide truncation or spend its full per-lookup cycle allowance.
            return ChdMetadataIndex.Read(source, BinaryPrimitives.ReadUInt64BigEndian(header[48..]), expected);
        }
        else if (version is 3 or 4)
        {
            ulong logical = BinaryPrimitives.ReadUInt64BigEndian(header[28..]);
            uint hunk = BinaryPrimitives.ReadUInt32BigEndian(header[(version == 3 ? 76 : 44)..]);
            if (logical is 0 or > long.MaxValue || hunk is 0 or > int.MaxValue)
                throw new ChdValidationException(ChdError.InvalidData, "open logical/hunk geometry");
            return ChdMetadataIndex.Read(source, BinaryPrimitives.ReadUInt64BigEndian(header[36..]), expected);
        }
        return [];
    }

    internal static unsafe ChdHeader Snapshot(chd_header* value)
    {
        if (value == null) throw new ChdValidationException(ChdError.InvalidFile, "read header pointer");
        uint count = value->version == 5 ? value->hunkcount : value->totalhunks;
        if (value->logicalbytes is 0 or > long.MaxValue || value->hunkbytes is 0 or > int.MaxValue ||
            value->unitbytes == 0 || count == 0 || value->logicalbytes / value->hunkbytes +
            (value->logicalbytes % value->hunkbytes == 0 ? 0UL : 1UL) != count)
            throw new ChdValidationException(ChdError.InvalidData, "read header geometry");
        ChdSha1 overall = ChdSha1.FromBytes(new ReadOnlySpan<byte>(&value->sha1.e0, 20));
        ChdSha1 raw = value->version == 3 ? overall : ChdSha1.FromBytes(new ReadOnlySpan<byte>(&value->rawsha1.e0, 20));
        ChdSha1 parent = ChdSha1.FromBytes(new ReadOnlySpan<byte>(&value->parentsha1.e0, 20));
        return new(value->version, value->logicalbytes, value->hunkbytes, value->unitbytes, count,
            (ChdCodec)value->compression[0], (ChdCodec)value->compression[1],
            (ChdCodec)value->compression[2], (ChdCodec)value->compression[3], raw, overall, parent,
            value->version < 5 ? (value->flags & 1) != 0 : !parent.IsEmpty);
    }
}
