using System;
using JpegCodec.Decode;

namespace JpegCodec.Encode;

// Encodes one block: FDCT → quantize → zigzag → DC delta + AC RLE.
// Used by the public encoder per MCU, per component, per subblock.
internal static class BlockEncoder
{
    // Returns the new DC predictor value (raw, not delta).
    public static int EncodeBlock(
        BitWriter writer,
        ReadOnlySpan<short> samplesNaturalOrder,
        ReadOnlySpan<ushort> quantTable,
        HuffmanEncoder dcEncoder,
        HuffmanEncoder acEncoder,
        int dcPredictor)
    {
        Span<short> coeffsNatural = stackalloc short[64];
        ForwardDct.Apply(samplesNaturalOrder, coeffsNatural);

        // Re-zigzag while quantizing.
        Span<short> coeffsZigzag = stackalloc short[64];
        for (var k = 0; k < 64; k++)
        {
            int natIdx = ZigZag.ZigzagToNatural[k];
            int q = quantTable[k];
            int v = coeffsNatural[natIdx];
            if (q == 0) { coeffsZigzag[k] = (short)v; continue; }
            int half = q >> 1;
            v = v >= 0 ? (v + half) / q : -((-v + half) / q);
            if (v > short.MaxValue) v = short.MaxValue;
            else if (v < short.MinValue) v = short.MinValue;
            coeffsZigzag[k] = (short)v;
        }

        // DC: encode (delta) as (SSSS, signed magnitude bits).
        int dcValue = coeffsZigzag[0];
        int dcDiff = dcValue - dcPredictor;
        EncodeDcCoefficient(writer, dcEncoder, dcDiff);

        // AC: walk 1..63, run-length-encode zero runs.
        var run = 0;
        for (var k = 1; k < 64; k++)
        {
            int ac = coeffsZigzag[k];
            if (ac == 0)
            {
                run++;
                continue;
            }
            while (run >= 16)
            {
                // ZRL — 16 zeros.
                acEncoder.Encode(writer, 0xF0);
                run -= 16;
            }
            int size = MagnitudeBits(ac);
            int rs = (run << 4) | size;
            acEncoder.Encode(writer, rs);
            WriteSignedMagnitude(writer, ac, size);
            run = 0;
        }
        if (run > 0)
        {
            // Trailing zeros — emit EOB.
            acEncoder.Encode(writer, 0x00);
        }

        return dcValue;
    }

    private static void EncodeDcCoefficient(BitWriter writer, HuffmanEncoder dcEncoder, int dcDiff)
    {
        int size = MagnitudeBits(dcDiff);
        dcEncoder.Encode(writer, size);
        if (size > 0) WriteSignedMagnitude(writer, dcDiff, size);
    }

    private static int MagnitudeBits(int value)
    {
        int abs = value < 0 ? -value : value;
        var bits = 0;
        while (abs != 0) { bits++; abs >>= 1; }
        return bits;
    }

    private static void WriteSignedMagnitude(BitWriter writer, int value, int size)
    {
        // T.81 §F.1.2.1 inverse of RECEIVE+EXTEND: for negative values,
        // the encoded bits are `value + (2^size - 1)` taken as size-bit
        // unsigned.
        if (value < 0) value += (1 << size) - 1;
        writer.WriteBits(value, size);
    }
}
