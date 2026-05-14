using System;

namespace Jp2Codec.Wavelet
{
    /// <summary>
    /// 1D inverse 5/3 reversible lifting filter per ISO/IEC 15444-1 F.3.8.1
    /// (1D_FILTR5-3R). Operates on an already-interleaved signal Y produced
    /// by <see cref="InverseInterleave"/> and produces the reconstructed
    /// integer signal X in-place.
    ///
    /// <para>
    /// The two lifting steps from the spec are:
    /// </para>
    /// <list type="number">
    /// <item>
    /// <description>
    /// <c>F-5: X(2n) = Y(2n) − ⌊(Y(2n−1) + Y(2n+1) + 2) / 4⌋</c>
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <c>F-6: X(2n+1) = Y(2n+1) + ⌊(X(2n) + X(2n+2)) / 2⌋</c>
    /// </description>
    /// </item>
    /// </list>
    ///
    /// <para>
    /// Boundary access uses whole-sample symmetric extension (1D_EXTR,
    /// F.3.7). The padding is refreshed between the two steps so that the
    /// neighbor accesses in F-6 see the reflected post-update X values.
    /// </para>
    /// </summary>
    internal static class InverseLifting53
    {
        private const int Pad = 2;

        /// <summary>
        /// Inverse 1D 5/3 filter. <paramref name="startingParity"/> is
        /// <c>i0 mod 2</c> — 0 if the first sample is canvas-even
        /// (low-pass), 1 if canvas-odd (high-pass). Returns a freshly
        /// allocated <c>int[]</c> of length <paramref name="y"/>.Length.
        /// </summary>
        public static int[] Apply(int[] y, int startingParity)
        {
            if (y is null) throw new ArgumentNullException(nameof(y));
            if (startingParity != 0 && startingParity != 1)
                throw new ArgumentOutOfRangeException(
                    nameof(startingParity), startingParity, "Must be 0 or 1.");

            int length = y.Length;
            if (length == 0) return Array.Empty<int>();

            if (length == 1)
            {
                int v = y[0];
                int x = startingParity == 0 ? v : ArithmeticShiftRight(v, 1);
                return new[] { x };
            }

            var result = new int[length];
            int bufLen = length + 2 * Pad;
            var buf = new int[bufLen];
            ApplyCore(y, length, startingParity, buf, result);
            return result;
        }

        public static void ApplyInPlace(int[] data, int length, int startingParity, int[] workBuf)
        {
            if (length <= 0) return;

            if (length == 1)
            {
                if (startingParity != 0) data[0] = ArithmeticShiftRight(data[0], 1);
                return;
            }

            ApplyCore(data, length, startingParity, workBuf, data);
        }

        private static void ApplyCore(int[] input, int length, int startingParity, int[] buf, int[] result)
        {
            Array.Copy(input, 0, buf, Pad, length);
            SymmetricExtension.Fill(buf, Pad, length);

            int firstEvenLocal = (startingParity == 0) ? 0 : 1;
            for (int local = firstEvenLocal; local < length; local += 2)
            {
                int bufIdx = local + Pad;
                int sum = buf[bufIdx - 1] + buf[bufIdx + 1] + 2;
                buf[bufIdx] -= FloorDiv4(sum);
            }

            SymmetricExtension.Fill(buf, Pad, length);

            int firstOddLocal = (startingParity == 0) ? 1 : 0;
            for (int local = firstOddLocal; local < length; local += 2)
            {
                int bufIdx = local + Pad;
                int sum = buf[bufIdx - 1] + buf[bufIdx + 1];
                buf[bufIdx] += FloorDiv2(sum);
            }

            Array.Copy(buf, Pad, result, 0, length);
        }

        private static int FloorDiv4(int a)
        {
            // Arithmetic shift right by 2 gives floor(a / 4) for both signs.
            return a >> 2;
        }

        private static int FloorDiv2(int a)
        {
            return a >> 1;
        }

        private static int ArithmeticShiftRight(int value, int shift)
        {
            return value >> shift;
        }
    }
}
