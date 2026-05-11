using JpegCodec.Stream;

namespace JpegCodec.Tests.Stream;

public class JpegBitReaderTests
{
    [Fact]
    public void ReadBit_MsbFirst()
    {
        // 0xA5 = 1010 0101, MSB first.
        var source = new JpegByteSource([0xA5], 0);
        var reader = new JpegBitReader(source);

        Assert.Equal(1, reader.ReadBit());
        Assert.Equal(0, reader.ReadBit());
        Assert.Equal(1, reader.ReadBit());
        Assert.Equal(0, reader.ReadBit());
        Assert.Equal(0, reader.ReadBit());
        Assert.Equal(1, reader.ReadBit());
        Assert.Equal(0, reader.ReadBit());
        Assert.Equal(1, reader.ReadBit());
    }

    [Fact]
    public void ReadBits_AcrossByteBoundary()
    {
        // 0xAB 0xCD = 1010 1011 1100 1101 — top 12 bits = 0xABC.
        var source = new JpegByteSource([0xAB, 0xCD], 0);
        var reader = new JpegBitReader(source);

        int twelveBits = reader.ReadBits(12);
        Assert.Equal(0xABC, twelveBits);
    }

    [Fact]
    public void ReadBits_AssemblesIntegerMsbFirst()
    {
        // First byte 0xFF, then stuffed 0x00, then 0x12. ReadBits(16) must
        // see 0xFF12 because the byte source destuffs the 00.
        var source = new JpegByteSource([0xFF, 0x00, 0x12], 0);
        var reader = new JpegBitReader(source);

        Assert.Equal(0xFF12, reader.ReadBits(16));
    }

    [Fact]
    public void ReadBits_PadsZeroAfterDestuffedEOI()
    {
        // T.81 §F.2.2.5: when the byte source halts at a marker, subsequent
        // bit reads must produce zero bits indefinitely.
        var source = new JpegByteSource([0x12, 0xFF, 0xD9], 0);
        var reader = new JpegBitReader(source);

        // Drain the first 8 bits (0x12).
        Assert.Equal(0x12, reader.ReadBits(8));

        // Now exhausted. Subsequent reads must yield zero.
        Assert.Equal(0, reader.ReadBits(8));
        Assert.Equal(0, reader.ReadBits(16));
        Assert.True(reader.Exhausted);
        Assert.True(reader.AtMarker);
    }

    [Fact]
    public void Receive_SignedExtension_F1_2_1()
    {
        // T.81 §F.1.2.1.1, Figure F.12: RECEIVE(SSSS=4) over bits 0011
        // returns -12. Construct a byte whose top 4 bits are 0011 = 3:
        // 0011 xxxx → 0x30.
        var source = new JpegByteSource([0x30], 0);
        var reader = new JpegBitReader(source);

        Assert.Equal(-12, reader.Receive(4));
    }

    [Fact]
    public void Receive_PositiveValue_NoExtension()
    {
        // Top 4 bits = 1010 = 10. Leading bit is 1, so Extend leaves the
        // value unchanged.
        var source = new JpegByteSource([0xA0], 0);
        var reader = new JpegBitReader(source);

        Assert.Equal(10, reader.Receive(4));
    }

    [Fact]
    public void Receive_ZeroBits_ReturnsZero()
    {
        var source = new JpegByteSource([0xFF, 0x00, 0xAA], 0);
        var reader = new JpegBitReader(source);

        // Reading zero bits consumes nothing.
        Assert.Equal(0, reader.Receive(0));

        // The full 16-bit value after destuffing is 0xFFAA.
        Assert.Equal(0xFFAA, reader.ReadBits(16));
    }

    [Fact]
    public void Extend_SpecValues()
    {
        // Spot-check the EXTEND table from T.81 §F.2.1.3.1 / Table F.1.
        //
        //  SSSS | DIFF range
        //   1   | -1, 1
        //   2   | -3..-2, 2..3
        //   3   | -7..-4, 4..7
        //
        // Verify a handful of values:
        Assert.Equal(-1, JpegBitReader.Extend(0, 1));
        Assert.Equal(1, JpegBitReader.Extend(1, 1));
        Assert.Equal(-3, JpegBitReader.Extend(0, 2));
        Assert.Equal(-2, JpegBitReader.Extend(1, 2));
        Assert.Equal(2, JpegBitReader.Extend(2, 2));
        Assert.Equal(3, JpegBitReader.Extend(3, 2));
        Assert.Equal(-7, JpegBitReader.Extend(0, 3));
        Assert.Equal(-4, JpegBitReader.Extend(3, 3));
        Assert.Equal(4, JpegBitReader.Extend(4, 3));
        Assert.Equal(7, JpegBitReader.Extend(7, 3));
    }
}
