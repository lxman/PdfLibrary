using JpegCodec.Decode;

namespace JpegCodec.Tests.Decode;

public class LevelShiftTests
{
    [Fact]
    public void Shift_AddsOneTwentyEight()
    {
        Assert.Equal(0, LevelShift.Shift(-128));
        Assert.Equal(128, LevelShift.Shift(0));
        Assert.Equal(255, LevelShift.Shift(127));
    }

    [Fact]
    public void Shift_ClampsAtBoundaries()
    {
        Assert.Equal(0, LevelShift.Shift(-200));
        Assert.Equal(0, LevelShift.Shift(-128));
        Assert.Equal(255, LevelShift.Shift(127));
        Assert.Equal(255, LevelShift.Shift(200));
        Assert.Equal(255, LevelShift.Shift(1000));
    }

    [Fact]
    public void ShiftBlockInPlace_WritesAllSamples()
    {
        var block = new short[64];
        for (var i = 0; i < 64; i++) block[i] = (short)(i - 32);

        var dst = new byte[64];
        LevelShift.ShiftBlockInPlace(block, dst, 0, 8);

        for (var i = 0; i < 64; i++)
            Assert.Equal(LevelShift.Shift(i - 32), dst[i]);
    }
}
