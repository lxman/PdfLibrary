using System;

namespace JpegCodec.Decode;

// 8x8 Inverse DCT, T.81 §A.3.3.
//
// Uses the separable property of the 2D DCT-II inverse: do a 1D IDCT on
// each of the 8 rows, then a 1D IDCT on each of the 8 resulting columns.
// The implementation is plain floating-point per the spec equation —
// straightforward and easy to audit. A faster fixed-point (Loeffler /
// AAN) variant can replace this once Phase 5/6 verifies the output.
//
// Input  : 64 coefficients in NATURAL order (caller must un-zigzag first)
//          dequantized to the spec scaling
// Output : 64 samples in NATURAL order, signed (no level shift applied)
internal static class InverseDct
{
    private const double C0 = 0.7071067811865475;  // 1/sqrt(2)
    private const double Pi = Math.PI;

    // Cosine table: cosTable[k, n] = cos((2n+1) * k * pi / 16).
    private static readonly double[,] CosTable = BuildCosTable();

    private static double[,] BuildCosTable()
    {
        var t = new double[8, 8];
        for (var k = 0; k < 8; k++)
            for (var n = 0; n < 8; n++)
                t[k, n] = Math.Cos((2 * n + 1) * k * Pi / 16.0);
        return t;
    }

    public static void Apply(short[] inputNaturalOrder, short[] outputNaturalOrder)
    {
        if (inputNaturalOrder is null) throw new ArgumentNullException(nameof(inputNaturalOrder));
        if (outputNaturalOrder is null) throw new ArgumentNullException(nameof(outputNaturalOrder));
        if (inputNaturalOrder.Length != 64)
            throw new ArgumentException("Input must have 64 coefficients.", nameof(inputNaturalOrder));
        if (outputNaturalOrder.Length != 64)
            throw new ArgumentException("Output must have 64 samples.", nameof(outputNaturalOrder));
        Apply((ReadOnlySpan<short>)inputNaturalOrder, (Span<short>)outputNaturalOrder);
    }

    public static void Apply(ReadOnlySpan<short> inputNaturalOrder, Span<short> outputNaturalOrder)
    {
        if (inputNaturalOrder.Length != 64)
            throw new ArgumentException("Input must have 64 coefficients.", nameof(inputNaturalOrder));
        if (outputNaturalOrder.Length != 64)
            throw new ArgumentException("Output must have 64 samples.", nameof(outputNaturalOrder));

        // Working buffers.
        Span<double> rowOut = stackalloc double[64];
        Span<double> intermediate = stackalloc double[64];

        // 1D IDCT on each row, writing into intermediate[y, x].
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var sum = 0.0;
                for (var u = 0; u < 8; u++)
                {
                    double cu = u == 0 ? C0 : 1.0;
                    sum += cu * inputNaturalOrder[y * 8 + u] * CosTable[u, x];
                }
                intermediate[y * 8 + x] = sum / 2.0;
            }
        }

        // 1D IDCT on each column of 'intermediate', writing rowOut[y, x].
        for (var x = 0; x < 8; x++)
        {
            for (var y = 0; y < 8; y++)
            {
                var sum = 0.0;
                for (var v = 0; v < 8; v++)
                {
                    double cv = v == 0 ? C0 : 1.0;
                    sum += cv * intermediate[v * 8 + x] * CosTable[v, y];
                }
                rowOut[y * 8 + x] = sum / 2.0;
            }
        }

        // Round to nearest integer and store as short.
        for (var i = 0; i < 64; i++)
        {
            double v = rowOut[i];
            var r = (int)Math.Round(v);
            if (r > short.MaxValue) r = short.MaxValue;
            else if (r < short.MinValue) r = short.MinValue;
            outputNaturalOrder[i] = (short)r;
        }
    }
}
