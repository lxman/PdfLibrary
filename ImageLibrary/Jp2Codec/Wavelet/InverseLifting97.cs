using System;

namespace Jp2Codec.Wavelet
{
    /// <summary>
    /// 1D inverse 9/7 irreversible lifting filter per ISO/IEC 15444-1
    /// F.3.8.2 (1D_FILTR9-7I). The six-step procedure from Equation F-7
    /// is applied in order:
    /// <list type="number">
    /// <item><description>STEP 1 — scale canvas-even by K.</description></item>
    /// <item><description>STEP 2 — scale canvas-odd by 1/K.</description></item>
    /// <item><description>STEP 3 — inverse U1: X(2n) −= δ·(X(2n−1) + X(2n+1)).</description></item>
    /// <item><description>STEP 4 — inverse P1: X(2n+1) −= γ·(X(2n) + X(2n+2)).</description></item>
    /// <item><description>STEP 5 — inverse U0: X(2n) −= β·(X(2n−1) + X(2n+1)).</description></item>
    /// <item><description>STEP 6 — inverse P0: X(2n+1) −= α·(X(2n) + X(2n+2)).</description></item>
    /// </list>
    /// Boundary access uses whole-sample symmetric extension (F.3.7). The
    /// padding is refreshed between every step that modifies a parity class
    /// so each subsequent step reads neighbors reflecting the latest values.
    /// </summary>
    internal static class InverseLifting97
    {
        private const int Pad = 2;

        public static float[] Apply(float[] y, int startingParity)
        {
            if (y is null) throw new ArgumentNullException(nameof(y));
            if (startingParity != 0 && startingParity != 1)
                throw new ArgumentOutOfRangeException(
                    nameof(startingParity), startingParity, "Must be 0 or 1.");

            int length = y.Length;

            // Empty input — empty output (edge-tile subband entirely outside
            // the tile slice).
            if (length == 0) return Array.Empty<float>();

            // F.3.6 length-1 case applies to both 5/3 and 9/7. The forward
            // emits 2·X for a single odd-start sample, so the inverse halves.
            if (length == 1)
            {
                float v = y[0];
                return new[] { startingParity == 0 ? v : v * 0.5f };
            }

            int bufLen = length + 2 * Pad;
            var buf = new float[bufLen];
            Array.Copy(y, 0, buf, Pad, length);
            SymmetricExtension.Fill(buf, Pad, length);

            int firstEven = (startingParity == 0) ? 0 : 1;
            int firstOdd = (startingParity == 0) ? 1 : 0;

            // STEP 1 — scale canvas-even by K.
            for (int local = firstEven; local < length; local += 2)
                buf[local + Pad] *= WaveletConstants.K;

            // STEP 2 — scale canvas-odd by 1/K.
            for (int local = firstOdd; local < length; local += 2)
                buf[local + Pad] *= WaveletConstants.InvK;

            SymmetricExtension.Fill(buf, Pad, length);

            // STEP 3 — inverse U1 on canvas-even.
            for (int local = firstEven; local < length; local += 2)
            {
                int b = local + Pad;
                buf[b] -= WaveletConstants.Delta * (buf[b - 1] + buf[b + 1]);
            }

            SymmetricExtension.Fill(buf, Pad, length);

            // STEP 4 — inverse P1 on canvas-odd.
            for (int local = firstOdd; local < length; local += 2)
            {
                int b = local + Pad;
                buf[b] -= WaveletConstants.Gamma * (buf[b - 1] + buf[b + 1]);
            }

            SymmetricExtension.Fill(buf, Pad, length);

            // STEP 5 — inverse U0 on canvas-even.
            for (int local = firstEven; local < length; local += 2)
            {
                int b = local + Pad;
                buf[b] -= WaveletConstants.Beta * (buf[b - 1] + buf[b + 1]);
            }

            SymmetricExtension.Fill(buf, Pad, length);

            // STEP 6 — inverse P0 on canvas-odd.
            for (int local = firstOdd; local < length; local += 2)
            {
                int b = local + Pad;
                buf[b] -= WaveletConstants.Alpha * (buf[b - 1] + buf[b + 1]);
            }

            var result = new float[length];
            Array.Copy(buf, Pad, result, 0, length);
            return result;
        }
    }
}
