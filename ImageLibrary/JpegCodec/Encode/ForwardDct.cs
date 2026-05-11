using System;

namespace JpegCodec.Encode;

// 8x8 Forward DCT, T.81 §A.3.3.
//
// Mirror of InverseDct using the same separable approach: 1D DCT on
// rows, then 1D DCT on columns. Plain floating-point — straightforward
// and easy to audit against the IDCT round-trip property.
internal static class ForwardDct
{
    private const double C0 = 0.7071067811865475;  // 1/sqrt(2)
    private const double Pi = Math.PI;

    private static readonly double[,] CosTable = BuildCosTable();

    private static double[,] BuildCosTable()
    {
        var t = new double[8, 8];
        for (var k = 0; k < 8; k++)
            for (var n = 0; n < 8; n++)
                t[k, n] = Math.Cos((2 * n + 1) * k * Pi / 16.0);
        return t;
    }

    // Input  : 64 samples in NATURAL order (signed, post-level-shift)
    // Output : 64 DCT coefficients in NATURAL order
    public static void Apply(ReadOnlySpan<short> inputNaturalOrder, Span<short> outputNaturalOrder)
    {
        if (inputNaturalOrder.Length != 64)
            throw new ArgumentException("Input must have 64 samples.", nameof(inputNaturalOrder));
        if (outputNaturalOrder.Length != 64)
            throw new ArgumentException("Output must have 64 coefficients.", nameof(outputNaturalOrder));

        Span<double> intermediate = stackalloc double[64];
        Span<double> result = stackalloc double[64];

        // 1D FDCT on each row.
        for (var y = 0; y < 8; y++)
        {
            for (var u = 0; u < 8; u++)
            {
                var sum = 0.0;
                for (var x = 0; x < 8; x++)
                    sum += inputNaturalOrder[y * 8 + x] * CosTable[u, x];
                double cu = u == 0 ? C0 : 1.0;
                intermediate[y * 8 + u] = sum * cu / 2.0;
            }
        }

        // 1D FDCT on each column of 'intermediate'.
        for (var u = 0; u < 8; u++)
        {
            for (var v = 0; v < 8; v++)
            {
                var sum = 0.0;
                for (var y = 0; y < 8; y++)
                    sum += intermediate[y * 8 + u] * CosTable[v, y];
                double cv = v == 0 ? C0 : 1.0;
                result[v * 8 + u] = sum * cv / 2.0;
            }
        }

        for (var i = 0; i < 64; i++)
        {
            var r = (int)Math.Round(result[i]);
            if (r > short.MaxValue) r = short.MaxValue;
            else if (r < short.MinValue) r = short.MinValue;
            outputNaturalOrder[i] = (short)r;
        }
    }
}
