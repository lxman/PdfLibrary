using System;

namespace JpegCodec.Decode;

// T.81 §F.2.1.4 / Annex A.3.4 — dequantization. The quantized coefficient
// array is in zigzag order. Multiplying by the quantization table (also
// zigzag) yields the dequantized coefficient array, still in zigzag order;
// the caller is responsible for reordering to natural before IDCT.
internal static class Dequantize
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
            zigzagCoefficients[k] = (short)(zigzagCoefficients[k] * quantizationTable[k]);
    }
}
