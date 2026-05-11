using JpegCodec.Decode;
using JpegCodec.Encode;

namespace JpegCodec.Tests.Encode;

public class ForwardDctTests
{
    [Fact]
    public void Fdct_FlatBlock_ProducesOnlyDcCoefficient()
    {
        // All samples = 0 (post-level-shift). Expected: all coefficients
        // zero (no signal).
        var input = new short[64];
        var output = new short[64];

        ForwardDct.Apply(input, output);

        for (var i = 0; i < 64; i++)
            Assert.Equal((short)0, output[i]);
    }

    [Fact]
    public void Fdct_AllSamples127_ProducesOnlyDcCoefficient()
    {
        // Constant block at +127 (signed pre-level-shift = 255 byte value).
        var input = new short[64];
        for (var i = 0; i < 64; i++) input[i] = 127;
        var output = new short[64];

        ForwardDct.Apply(input, output);

        // DC = 127 * 8 = 1016. AC coefficients should be 0.
        Assert.InRange(output[0], 1015, 1017);
        for (var i = 1; i < 64; i++)
            Assert.InRange(output[i], -1, 1);
    }

    [Fact]
    public void Fdct_Idct_RoundTrip_RandomBlocks()
    {
        var rng = new Random(12345);
        for (var trial = 0; trial < 50; trial++)
        {
            var input = new short[64];
            for (var i = 0; i < 64; i++) input[i] = (short)rng.Next(-128, 128);

            var coeffs = new short[64];
            var roundtrip = new short[64];
            ForwardDct.Apply(input, coeffs);
            InverseDct.Apply(coeffs, roundtrip);

            for (var i = 0; i < 64; i++)
            {
                int delta = Math.Abs(input[i] - roundtrip[i]);
                Assert.True(delta <= 1,
                    $"Round-trip failed at index {i} (trial {trial}): " +
                    $"input={input[i]}, output={roundtrip[i]}, Δ={delta}");
            }
        }
    }

    [Fact]
    public void Fdct_Idct_RoundTrip_EdgeBlocks()
    {
        short[][] inputs =
        [
            new short[64],
            CreateBlock((y, x) => 127),
            CreateBlock((y, x) => -128),
            CreateBlock((y, x) => (short)(((y + x) & 1) == 0 ? 127 : -128)),
            CreateBlock((y, x) => (short)(x * 16 - 64)),
        ];

        for (var t = 0; t < inputs.Length; t++)
        {
            var coeffs = new short[64];
            var roundtrip = new short[64];
            ForwardDct.Apply(inputs[t], coeffs);
            InverseDct.Apply(coeffs, roundtrip);

            for (var i = 0; i < 64; i++)
            {
                int delta = Math.Abs(inputs[t][i] - roundtrip[i]);
                Assert.True(delta <= 1,
                    $"Edge block {t} round-trip failed at index {i}: " +
                    $"input={inputs[t][i]}, output={roundtrip[i]}");
            }
        }
    }

    private static short[] CreateBlock(Func<int, int, short> f)
    {
        var b = new short[64];
        for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
                b[y * 8 + x] = f(y, x);
        return b;
    }
}
