using System;

namespace JpegCodec.Decode;

// 8x8 Inverse DCT, T.81 §A.3.3.
//
// Loeffler/Ligtenberg/Moschytz butterfly decomposition with 13-bit
// fixed-point constants (same algorithm as libjpeg-turbo jidctint.c).
// 11 multiplies + 29 adds per 1D pass vs the naive 64 multiply-adds.
//
// Input  : 64 coefficients in NATURAL order (caller must un-zigzag first)
//          dequantized to the spec scaling
// Output : 64 samples in NATURAL order, signed (no level shift applied)
internal static class InverseDct
{
    private const int ConstBits = 13;
    private const int Pass1Bits = 2;

    private const int Fix0298 = 2446;   // FIX(0.298631336)
    private const int Fix0390 = 3196;   // FIX(0.390180644)
    private const int Fix0541 = 4433;   // FIX(0.541196100)
    private const int Fix0765 = 6270;   // FIX(0.765366865)
    private const int Fix0899 = 7373;   // FIX(0.899976223)
    private const int Fix1175 = 9633;   // FIX(1.175875602)
    private const int Fix1501 = 12299;  // FIX(1.501321110)
    private const int Fix1847 = 15137;  // FIX(1.847759065)
    private const int Fix1961 = 16069;  // FIX(1.961570560)
    private const int Fix2053 = 16819;  // FIX(2.053119869)
    private const int Fix2562 = 20995;  // FIX(2.562915447)
    private const int Fix3072 = 25172;  // FIX(3.072711026)

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

    public static void Apply(ReadOnlySpan<short> input, Span<short> output)
    {
        if (input.Length != 64)
            throw new ArgumentException("Input must have 64 coefficients.", nameof(input));
        if (output.Length != 64)
            throw new ArgumentException("Output must have 64 samples.", nameof(output));

        Span<int> workspace = stackalloc int[64];

        // Pass 1: rows. Output is scaled up by Pass1Bits.
        for (var row = 0; row < 8; row++)
        {
            int b = row * 8;

            if (input[b + 1] == 0 && input[b + 2] == 0 && input[b + 3] == 0 &&
                input[b + 4] == 0 && input[b + 5] == 0 && input[b + 6] == 0 &&
                input[b + 7] == 0)
            {
                int dc = input[b] << Pass1Bits;
                workspace[b] = dc;
                workspace[b + 1] = dc;
                workspace[b + 2] = dc;
                workspace[b + 3] = dc;
                workspace[b + 4] = dc;
                workspace[b + 5] = dc;
                workspace[b + 6] = dc;
                workspace[b + 7] = dc;
                continue;
            }

            RowPass(
                input[b], input[b + 1], input[b + 2], input[b + 3],
                input[b + 4], input[b + 5], input[b + 6], input[b + 7],
                workspace, b);
        }

        // Pass 2: columns. Descales by ConstBits + Pass1Bits + 3.
        for (var col = 0; col < 8; col++)
        {
            if (workspace[8 + col] == 0 && workspace[16 + col] == 0 &&
                workspace[24 + col] == 0 && workspace[32 + col] == 0 &&
                workspace[40 + col] == 0 && workspace[48 + col] == 0 &&
                workspace[56 + col] == 0)
            {
                short dc = Descale(workspace[col], Pass1Bits + 3);
                output[col] = dc;
                output[8 + col] = dc;
                output[16 + col] = dc;
                output[24 + col] = dc;
                output[32 + col] = dc;
                output[40 + col] = dc;
                output[48 + col] = dc;
                output[56 + col] = dc;
                continue;
            }

            ColPass(
                workspace[col], workspace[8 + col], workspace[16 + col], workspace[24 + col],
                workspace[32 + col], workspace[40 + col], workspace[48 + col], workspace[56 + col],
                output, col);
        }
    }

    private static void RowPass(
        int d0, int d1, int d2, int d3,
        int d4, int d5, int d6, int d7,
        Span<int> ws, int b)
    {
        const int shift = ConstBits - Pass1Bits;
        const int bias = 1 << (shift - 1);

        // Even part.
        int tmp0 = (d0 + d4) << ConstBits;
        int tmp1 = (d0 - d4) << ConstBits;

        int z1 = (d2 + d6) * Fix0541;
        int tmp2 = z1 - d6 * Fix1847;
        int tmp3 = z1 + d2 * Fix0765;

        int tmp10 = tmp0 + tmp3;
        int tmp13 = tmp0 - tmp3;
        int tmp11 = tmp1 + tmp2;
        int tmp12 = tmp1 - tmp2;

        // Odd part.
        int z1o = d7 + d1;
        int z2o = d5 + d3;
        int z3 = d7 + d3;
        int z4 = d5 + d1;
        int z5 = (z3 + z4) * Fix1175;

        int t0 = d7 * Fix0298;
        int t1 = d5 * Fix2053;
        int t2 = d3 * Fix3072;
        int t3 = d1 * Fix1501;
        z1o *= -Fix0899;
        z2o *= -Fix2562;
        z3 = z3 * -Fix1961 + z5;
        z4 = z4 * -Fix0390 + z5;

        t0 += z1o + z3;
        t1 += z2o + z4;
        t2 += z2o + z3;
        t3 += z1o + z4;

        ws[b]     = (tmp10 + t3 + bias) >> shift;
        ws[b + 7] = (tmp10 - t3 + bias) >> shift;
        ws[b + 1] = (tmp11 + t2 + bias) >> shift;
        ws[b + 6] = (tmp11 - t2 + bias) >> shift;
        ws[b + 2] = (tmp12 + t1 + bias) >> shift;
        ws[b + 5] = (tmp12 - t1 + bias) >> shift;
        ws[b + 3] = (tmp13 + t0 + bias) >> shift;
        ws[b + 4] = (tmp13 - t0 + bias) >> shift;
    }

    private static void ColPass(
        int d0, int d1, int d2, int d3,
        int d4, int d5, int d6, int d7,
        Span<short> output, int col)
    {
        const int shift = ConstBits + Pass1Bits + 3;

        // Even part.
        int tmp0 = (d0 + d4) << ConstBits;
        int tmp1 = (d0 - d4) << ConstBits;

        int z1 = (d2 + d6) * Fix0541;
        int tmp2 = z1 - d6 * Fix1847;
        int tmp3 = z1 + d2 * Fix0765;

        int tmp10 = tmp0 + tmp3;
        int tmp13 = tmp0 - tmp3;
        int tmp11 = tmp1 + tmp2;
        int tmp12 = tmp1 - tmp2;

        // Odd part.
        int z1o = d7 + d1;
        int z2o = d5 + d3;
        int z3 = d7 + d3;
        int z4 = d5 + d1;
        int z5 = (z3 + z4) * Fix1175;

        int t0 = d7 * Fix0298;
        int t1 = d5 * Fix2053;
        int t2 = d3 * Fix3072;
        int t3 = d1 * Fix1501;
        z1o *= -Fix0899;
        z2o *= -Fix2562;
        z3 = z3 * -Fix1961 + z5;
        z4 = z4 * -Fix0390 + z5;

        t0 += z1o + z3;
        t1 += z2o + z4;
        t2 += z2o + z3;
        t3 += z1o + z4;

        output[col]      = Descale(tmp10 + t3, shift);
        output[56 + col] = Descale(tmp10 - t3, shift);
        output[8 + col]  = Descale(tmp11 + t2, shift);
        output[48 + col] = Descale(tmp11 - t2, shift);
        output[16 + col] = Descale(tmp12 + t1, shift);
        output[40 + col] = Descale(tmp12 - t1, shift);
        output[24 + col] = Descale(tmp13 + t0, shift);
        output[32 + col] = Descale(tmp13 - t0, shift);
    }

    private static short Descale(int value, int shift)
    {
        return (short)((value + (1 << (shift - 1))) >> shift);
    }
}
