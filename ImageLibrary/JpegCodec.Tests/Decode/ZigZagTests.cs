using JpegCodec.Decode;

namespace JpegCodec.Tests.Decode;

public class ZigZagTests
{
    [Fact]
    public void Inverse_ScansFigure5_Order()
    {
        // T.81 Figure 5: zigzag position 0 = (0,0), 1 = (0,1), 2 = (1,0),
        // 5 = (2,0), 14 = (4,0), 63 = (7,7).
        Assert.Equal(0, ZigZag.ZigzagToNatural[0]);    // (0,0)
        Assert.Equal(1, ZigZag.ZigzagToNatural[1]);    // (0,1)
        Assert.Equal(8, ZigZag.ZigzagToNatural[2]);    // (1,0)
        Assert.Equal(16, ZigZag.ZigzagToNatural[3]);   // (2,0)
        Assert.Equal(9, ZigZag.ZigzagToNatural[4]);    // (1,1)
        Assert.Equal(2, ZigZag.ZigzagToNatural[5]);    // (0,2)
        Assert.Equal(63, ZigZag.ZigzagToNatural[63]);  // (7,7)
    }

    [Fact]
    public void Forward_IsInverse_OfInverse()
    {
        for (byte k = 0; k < 64; k++)
            Assert.Equal(k, ZigZag.NaturalToZigzag[ZigZag.ZigzagToNatural[k]]);
        for (byte n = 0; n < 64; n++)
            Assert.Equal(n, ZigZag.ZigzagToNatural[ZigZag.NaturalToZigzag[n]]);
    }
}
