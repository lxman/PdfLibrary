using System;

namespace JpegCodec.Encode;

// Forward quantization — divide DCT coefficients by per-position quant
// values and round to nearest integer. T.81 §F.1.1.4.
internal static class Quantize
{
    public static void Apply(short[] zigzagCoefficients, ushort[] quantizationTable)
    {
        if (zigzagCoefficients is null) throw new ArgumentNullException(nameof(zigzagCoefficients));
        if (quantizationTable is null) throw new ArgumentNullException(nameof(quantizationTable));
        if (zigzagCoefficients.Length != 64)
            throw new ArgumentException("Coefficient block must have 64 entries.", nameof(zigzagCoefficients));
        if (quantizationTable.Length != 64)
            throw new ArgumentException("Quantization table must have 64 entries.", nameof(quantizationTable));

        for (var k = 0; k < 64; k++)
        {
            int q = quantizationTable[k];
            if (q == 0) continue;
            int half = q >> 1;
            int v = zigzagCoefficients[k];
            v = v >= 0 ? (v + half) / q : -((-v + half) / q);
            if (v > short.MaxValue) v = short.MaxValue;
            else if (v < short.MinValue) v = short.MinValue;
            zigzagCoefficients[k] = (short)v;
        }
    }

    public static void Apply(Span<short> zigzagCoefficients, ReadOnlySpan<ushort> quantizationTable)
    {
        for (var k = 0; k < 64; k++)
        {
            int q = quantizationTable[k];
            if (q == 0) continue;
            int half = q >> 1;
            int v = zigzagCoefficients[k];
            v = v >= 0 ? (v + half) / q : -((-v + half) / q);
            if (v > short.MaxValue) v = short.MaxValue;
            else if (v < short.MinValue) v = short.MinValue;
            zigzagCoefficients[k] = (short)v;
        }
    }
}
