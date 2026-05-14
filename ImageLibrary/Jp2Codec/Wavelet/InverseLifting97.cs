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
            if (length == 0) return Array.Empty<float>();

            if (length == 1)
            {
                float v = y[0];
                return new[] { startingParity == 0 ? v : v * 0.5f };
            }

            var result = new float[length];
            var buf = new float[length + 2 * Pad];
            ApplyCore(y, length, startingParity, buf, result);
            return result;
        }

        public static void ApplyInPlace(float[] data, int length, int startingParity, float[] workBuf)
        {
            if (length <= 0) return;

            if (length == 1)
            {
                if (startingParity != 0) data[0] *= 0.5f;
                return;
            }

            ApplyCore(data, length, startingParity, workBuf, data);
        }

        private static void ApplyCore(float[] input, int length, int startingParity, float[] buf, float[] result)
        {
            Array.Copy(input, 0, buf, Pad, length);
            SymmetricExtension.Fill(buf, Pad, length);

            int firstEven = (startingParity == 0) ? 0 : 1;
            int firstOdd = (startingParity == 0) ? 1 : 0;

            for (int local = firstEven; local < length; local += 2)
                buf[local + Pad] *= WaveletConstants.K;

            for (int local = firstOdd; local < length; local += 2)
                buf[local + Pad] *= WaveletConstants.InvK;

            SymmetricExtension.Fill(buf, Pad, length);

            for (int local = firstEven; local < length; local += 2)
            {
                int b = local + Pad;
                buf[b] -= WaveletConstants.Delta * (buf[b - 1] + buf[b + 1]);
            }

            SymmetricExtension.Fill(buf, Pad, length);

            for (int local = firstOdd; local < length; local += 2)
            {
                int b = local + Pad;
                buf[b] -= WaveletConstants.Gamma * (buf[b - 1] + buf[b + 1]);
            }

            SymmetricExtension.Fill(buf, Pad, length);

            for (int local = firstEven; local < length; local += 2)
            {
                int b = local + Pad;
                buf[b] -= WaveletConstants.Beta * (buf[b - 1] + buf[b + 1]);
            }

            SymmetricExtension.Fill(buf, Pad, length);

            for (int local = firstOdd; local < length; local += 2)
            {
                int b = local + Pad;
                buf[b] -= WaveletConstants.Alpha * (buf[b - 1] + buf[b + 1]);
            }

            Array.Copy(buf, Pad, result, 0, length);
        }
    }
}
