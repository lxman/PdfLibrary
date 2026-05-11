using JpegCodec.Decode;

namespace JpegCodec.Tests.Decode;

public class InverseDctTests
{
    [Fact]
    public void Idct_DcOnly_ProducesFlatBlock()
    {
        // T.81 §A.3.3 IDCT: a single non-zero DC coefficient at S(0,0)=D
        // produces the constant block s(y,x) = D * (1/sqrt(2))^2 / 4 = D/8.
        //
        // With D = 1024, output = 128 for every pixel.
        var coeffs = new short[64];
        coeffs[0] = 1024;
        var output = new short[64];

        InverseDct.Apply(coeffs, output);

        for (var i = 0; i < 64; i++)
            Assert.Equal((short)128, output[i]);
    }

    [Fact]
    public void Idct_ZeroInput_ZeroOutput()
    {
        var coeffs = new short[64];
        var output = new short[64];

        InverseDct.Apply(coeffs, output);

        for (var i = 0; i < 64; i++)
            Assert.Equal((short)0, output[i]);
    }

    [Fact]
    public void Idct_DcCoefficient_NegativeValueProducesNegativeFlat()
    {
        var coeffs = new short[64];
        coeffs[0] = -512;
        var output = new short[64];

        InverseDct.Apply(coeffs, output);

        for (var i = 0; i < 64; i++)
            Assert.Equal((short)-64, output[i]);
    }

    [Fact]
    public void Idct_AcOnly_FirstHarmonic_HorizontalCosine()
    {
        // S(0,1) = a, all else zero. T.81 §A.3.3:
        //
        //   s(y,x) = (1/4) * C(0) * C(1) * a * cos((2x+1)*pi/16) * cos(0)
        //         = (a / (4*sqrt(2))) * cos((2x+1) * pi / 16)
        //
        // The y dimension should be constant since C(0)*cos(0) is constant
        // in y. Use a large amplitude to make rounding error negligible.
        const int a = 1024;
        var coeffs = new short[64];
        coeffs[1] = a;
        var output = new short[64];

        InverseDct.Apply(coeffs, output);

        double amplitude = a / (4.0 * Math.Sqrt(2.0));
        for (var x = 0; x < 8; x++)
        {
            var expected = (int)Math.Round(amplitude * Math.Cos((2 * x + 1) * Math.PI / 16.0));
            for (var y = 0; y < 8; y++)
                Assert.Equal(expected, output[y * 8 + x]);
        }
    }

    [Fact]
    public void Idct_AcOnly_FirstHarmonic_VerticalCosine()
    {
        // S(1,0) = a, all else zero. Vertical cosine, constant in x.
        const int a = 1024;
        var coeffs = new short[64];
        coeffs[8] = a; // natural index (1,0) = S(v=1, u=0)
        var output = new short[64];

        InverseDct.Apply(coeffs, output);

        double amplitude = a / (4.0 * Math.Sqrt(2.0));
        for (var y = 0; y < 8; y++)
        {
            var expected = (int)Math.Round(amplitude * Math.Cos((2 * y + 1) * Math.PI / 16.0));
            for (var x = 0; x < 8; x++)
                Assert.Equal(expected, output[y * 8 + x]);
        }
    }
}
