using JpegCodec.Decode;
using JpegCodec.Encode;

namespace JpegCodec.Tests.Encode;

public class QuantizeTests
{
    [Fact]
    public void Quantize_IdentityTable_PassesThrough()
    {
        var coeffs = new short[64];
        for (var i = 0; i < 64; i++) coeffs[i] = (short)(i - 32);
        var q = new ushort[64];
        for (var i = 0; i < 64; i++) q[i] = 1;

        var expected = (short[])coeffs.Clone();
        Quantize.Apply(coeffs, q);
        Assert.Equal(expected, coeffs);
    }

    [Fact]
    public void Quantize_Dequantize_RoundTrip_LossWithinHalfQ()
    {
        var rng = new Random(999);
        var q = new ushort[64];
        for (var i = 0; i < 64; i++) q[i] = (ushort)(1 + i);

        for (var trial = 0; trial < 20; trial++)
        {
            var original = new short[64];
            for (var i = 0; i < 64; i++) original[i] = (short)rng.Next(-2000, 2000);

            var quantized = (short[])original.Clone();
            Quantize.Apply(quantized, q);

            var dequantized = (short[])quantized.Clone();
            Dequantize.Apply(dequantized, q);

            for (var i = 0; i < 64; i++)
            {
                int delta = Math.Abs(original[i] - dequantized[i]);
                int halfQ = (q[i] + 1) / 2;
                Assert.True(delta <= halfQ,
                    $"Quantize/dequantize at index {i}: original={original[i]}, " +
                    $"dequant={dequantized[i]}, q={q[i]}, Δ={delta}, halfQ={halfQ}");
            }
        }
    }
}
