using JpegCodec.Decode;

namespace JpegCodec.Tests.Decode;

public class DequantizeTests
{
    [Fact]
    public void Dequantize_IdentityTable_PassesThrough()
    {
        var block = new short[64];
        for (var i = 0; i < 64; i++) block[i] = (short)(i * 3 - 50);
        var q = new ushort[64];
        for (var i = 0; i < 64; i++) q[i] = 1;

        var expected = (short[])block.Clone();
        Dequantize.Apply(block, q);
        Assert.Equal(expected, block);
    }

    [Fact]
    public void Dequantize_ScalesElementwise()
    {
        var block = new short[64];
        for (var i = 0; i < 64; i++) block[i] = 1;
        var q = new ushort[64];
        for (var i = 0; i < 64; i++) q[i] = (ushort)(i + 1);

        Dequantize.Apply(block, q);

        for (var i = 0; i < 64; i++)
            Assert.Equal((short)(i + 1), block[i]);
    }
}
