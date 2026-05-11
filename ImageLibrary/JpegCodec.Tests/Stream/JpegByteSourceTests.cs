using JpegCodec.Stream;

namespace JpegCodec.Tests.Stream;

public class JpegByteSourceTests
{
    [Fact]
    public void Read_PassesThrough_NormalBytes()
    {
        var source = new JpegByteSource([0x01, 0x02, 0x03, 0xFE], 0);

        Assert.Equal(0x01, source.ReadByte());
        Assert.Equal(0x02, source.ReadByte());
        Assert.Equal(0x03, source.ReadByte());
        Assert.Equal(0xFE, source.ReadByte());
        Assert.Equal(-1, source.ReadByte()); // EOF
        Assert.False(source.AtMarker);
    }

    [Fact]
    public void Read_DestuffsFF00_InEntropySegment()
    {
        // [12 FF 00 34] → deliver [12 FF 34], drop the stuffed 00.
        var source = new JpegByteSource([0x12, 0xFF, 0x00, 0x34], 0);

        Assert.Equal(0x12, source.ReadByte());
        Assert.Equal(0xFF, source.ReadByte());
        Assert.Equal(0x34, source.ReadByte());
        Assert.Equal(-1, source.ReadByte());
        Assert.False(source.AtMarker);
    }

    [Fact]
    public void Read_StopsAt_FFDx_Marker()
    {
        // [12 34 FF D9] (EOI) — two literal bytes, then signal marker D9.
        var source = new JpegByteSource([0x12, 0x34, 0xFF, 0xD9], 0);

        Assert.Equal(0x12, source.ReadByte());
        Assert.Equal(0x34, source.ReadByte());
        Assert.Equal(-1, source.ReadByte());
        Assert.True(source.AtMarker);
        Assert.Equal(JpegMarker.Eoi, source.EncounteredMarker);

        // Subsequent reads stay at -1 until ConsumeMarker.
        Assert.Equal(-1, source.ReadByte());
    }

    [Fact]
    public void Read_PassesThrough_RstMarkerSignal()
    {
        // Restart markers (D0..D7) are also encountered mid-scan.
        var source = new JpegByteSource([0xAA, 0xFF, 0xD0], 0);

        Assert.Equal(0xAA, source.ReadByte());
        Assert.Equal(-1, source.ReadByte());
        Assert.True(source.AtMarker);
        Assert.Equal(JpegMarker.Rst0, source.EncounteredMarker);
    }

    [Fact]
    public void Read_HandlesPaddedFFFillBytes()
    {
        // T.81 §B.1.1.2: any number of 0xFF fill bytes may precede the actual
        // marker byte. [FF FF FF D9] is still EOI.
        var source = new JpegByteSource([0xFF, 0xFF, 0xFF, 0xD9], 0);

        Assert.Equal(-1, source.ReadByte());
        Assert.True(source.AtMarker);
        Assert.Equal(JpegMarker.Eoi, source.EncounteredMarker);
    }

    [Fact]
    public void Read_RejectsTruncated_FFAlone()
    {
        var source = new JpegByteSource([0x12, 0xFF], 0);

        Assert.Equal(0x12, source.ReadByte());
        Assert.Throws<InvalidOperationException>(() => source.ReadByte());
    }

    [Fact]
    public void ConsumeMarker_ClearsMarkerState_AllowsContinuation()
    {
        var source = new JpegByteSource([0xAA, 0xFF, 0xD0, 0xBB], 0);

        source.ReadByte(); // 0xAA
        source.ReadByte(); // -1, at marker D0
        Assert.True(source.AtMarker);

        source.ConsumeMarker();
        Assert.False(source.AtMarker);
        Assert.Equal(0xBB, source.ReadByte());
    }
}
