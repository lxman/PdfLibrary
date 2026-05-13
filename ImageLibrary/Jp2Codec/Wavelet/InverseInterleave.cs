using System;

namespace Jp2Codec.Wavelet
{
    /// <summary>
    /// 2D_INTERLEAVE step from ISO/IEC 15444-1 F.4.6. Merges a low-pass
    /// subband strip <c>aL</c> and a high-pass subband strip <c>aH</c> into
    /// a single interleaved signal <c>X</c> of length <c>aL.Length +
    /// aH.Length</c> (i.e., the length of the reconstructed parent signal).
    ///
    /// <para>
    /// The <c>startingParity</c> argument is <c>i0 mod 2</c>, where
    /// <c>i0</c> is the absolute canvas index of the first sample in the
    /// reconstructed signal. JPEG 2000 places low-pass samples at the
    /// even canvas positions and high-pass samples at the odd canvas
    /// positions, so the parity controls which subband contributes the
    /// first local sample:
    /// </para>
    /// <list type="bullet">
    /// <item><c>parity = 0</c> → X[0] = aL[0], X[1] = aH[0], X[2] = aL[1], …</item>
    /// <item><c>parity = 1</c> → X[0] = aH[0], X[1] = aL[0], X[2] = aH[1], …</item>
    /// </list>
    /// </summary>
    internal static class InverseInterleave
    {
        public static int[] Combine(int[] aL, int[] aH, int startingParity)
        {
            ValidateArgs(aL, aH, startingParity);
            int total = aL.Length + aH.Length;
            var x = new int[total];
            int liIdx = 0, hiIdx = 0;
            for (var k = 0; k < total; k++)
            {
                bool isLowPosition = ((k + startingParity) & 1) == 0;
                x[k] = isLowPosition ? aL[liIdx++] : aH[hiIdx++];
            }
            return x;
        }

        public static float[] Combine(float[] aL, float[] aH, int startingParity)
        {
            ValidateArgs(aL, aH, startingParity);
            int total = aL.Length + aH.Length;
            var x = new float[total];
            int liIdx = 0, hiIdx = 0;
            for (var k = 0; k < total; k++)
            {
                bool isLowPosition = ((k + startingParity) & 1) == 0;
                x[k] = isLowPosition ? aL[liIdx++] : aH[hiIdx++];
            }
            return x;
        }

        private static void ValidateArgs<T>(T[] aL, T[] aH, int startingParity)
        {
            if (aL is null) throw new ArgumentNullException(nameof(aL));
            if (aH is null) throw new ArgumentNullException(nameof(aH));
            if (startingParity != 0 && startingParity != 1)
                throw new ArgumentOutOfRangeException(
                    nameof(startingParity), startingParity,
                    "Starting parity must be 0 (low-first) or 1 (high-first).");

            // The two strips must agree on signal length given the parity:
            // total = aL + aH, and the counts are determined by total and parity.
            int total = aL.Length + aH.Length;
            int expectedLow = ExpectedLowCount(total, startingParity);
            int expectedHigh = total - expectedLow;
            if (aL.Length != expectedLow || aH.Length != expectedHigh)
                throw new ArgumentException(
                    $"Low/high subband lengths ({aL.Length}/{aH.Length}) inconsistent " +
                    $"with combined signal length {total} and starting parity {startingParity}; " +
                    $"expected ({expectedLow}/{expectedHigh}).");
        }

        private static int ExpectedLowCount(int total, int parity) =>
            // Number of even-local positions = ceil((total - parity_offset) / 2) where
            // parity_offset = parity (0 if low-first, 1 if high-first).
            parity == 0 ? (total + 1) / 2 : total / 2;
    }
}
