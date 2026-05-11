using JpegCodec.Encode;

namespace JpegCodec.Tests.Encode;

public class BitWriterTests
{
    [Fact]
    public void Write_StuffsFF00_OnAnyFFByte()
    {
        var bytes = new List<byte>();
        var w = new BitWriter(bytes);
        w.WriteBits(0xFF, 8);
        w.Flush();
        Assert.Equal(new byte[] { 0xFF, 0x00 }, bytes.ToArray());
    }

    [Fact]
    public void Write_PassesThrough_NonFFBytes()
    {
        var bytes = new List<byte>();
        var w = new BitWriter(bytes);
        w.WriteBits(0xA5, 8);
        w.WriteBits(0x3C, 8);
        w.Flush();
        Assert.Equal(new byte[] { 0xA5, 0x3C }, bytes.ToArray());
    }

    [Fact]
    public void Write_PacksMultipleSmallFieldsIntoOneByte()
    {
        var bytes = new List<byte>();
        var w = new BitWriter(bytes);
        // 4 bits + 4 bits = 1 byte
        w.WriteBits(0b1010, 4);
        w.WriteBits(0b0101, 4);
        Assert.Equal(new byte[] { 0xA5 }, bytes.ToArray());
        // No flush needed; we hit byte boundary.
    }

    [Fact]
    public void Flush_PadsPartialByte_WithOnes()
    {
        var bytes = new List<byte>();
        var w = new BitWriter(bytes);
        // 5 bits 11001 → after pad with 1s → 11001111 = 0xCF.
        w.WriteBits(0b11001, 5);
        w.Flush();
        Assert.Equal(new byte[] { 0xCF }, bytes.ToArray());
    }
}
