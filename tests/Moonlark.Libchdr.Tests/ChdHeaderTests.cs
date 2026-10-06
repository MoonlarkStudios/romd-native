using Xunit;

namespace Moonlark.Libchdr.Tests;

/// <summary>Verifies slot order, forward-compatible codec values and index bounds.</summary>
public sealed class ChdHeaderTests
{
    /// <summary>All four slots retain their numeric identities, including an unknown future codec.</summary>
    [Fact]
    public void CodecSlotsPreserveOrderAndUnknownValues()
    {
        var future = (ChdCodec)0xdeadbeef;
        var header = new ChdHeader(5, 4096, 4096, 2048, 1,
            ChdCodec.Lzma, ChdCodec.Zstd, future, ChdCodec.None, default, default, default, false);
        Assert.Equal(ChdCodec.Lzma, header.GetCodec(0));
        Assert.Equal(ChdCodec.Zstd, header.GetCodec(1));
        Assert.Equal(future, header.GetCodec(2));
        Assert.Equal(ChdCodec.None, header.GetCodec(3));
        foreach (int slot in new[] { -1, 4, int.MinValue, int.MaxValue })
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => header.GetCodec(slot));
            Assert.Equal("slot", exception.ParamName);
            Assert.Equal(slot, exception.ActualValue);
        }
    }
}
