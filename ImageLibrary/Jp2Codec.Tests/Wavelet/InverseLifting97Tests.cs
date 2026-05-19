using Jp2Codec.Wavelet;

namespace Jp2Codec.Tests.Wavelet
{
    public sealed class InverseLifting97Tests
    {
        // Round-trip tolerance: 9/7 is float-precision, drift grows mildly
        // with signal length. 1e-3 is comfortably above the observed error
        // for the tested signal magnitudes (±1024).
        private const float RoundTripTolerance = 1e-3f;

        // ==== Length-1 (F.3.6) =============================================

        [Fact]
        public void LengthOne_EvenStart_PassesThrough()
        {
            float[] x = InverseLifting97.Apply(new[] { 42.5f }, startingParity: 0);
            Assert.Single(x);
            Assert.Equal(42.5f, x[0]);
        }

        [Fact]
        public void LengthOne_OddStart_HalvesValue()
        {
            float[] x = InverseLifting97.Apply(new[] { 84.0f }, startingParity: 1);
            Assert.Single(x);
            Assert.Equal(42.0f, x[0]);
        }

        // ==== DC reconstruction: low-only Y reproduces flat signal =========

        [Fact]
        public void EvenStart_ConstantLow_ZeroHigh_ReconstructsFlatSignal()
        {
            // For a flat input X = [c]*L, the forward 9/7 produces
            // Y_low = c, Y_high = 0 (high-freq cancels). So the inverse of
            // an interleaved Y = [c, 0, c, 0, ...] must give back [c]*L.
            const float c = 3.0f;
            float[] y = { c, 0f, c, 0f, c, 0f, c, 0f };
            float[] x = InverseLifting97.Apply(y, startingParity: 0);

            Assert.Equal(8, x.Length);
            foreach (float v in x)
                Assert.InRange(v, c - 1e-5f, c + 1e-5f);
        }

        // ==== Round-trip ===================================================

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(16)]
        [InlineData(17)]
        [InlineData(32)]
        public void RoundTrip_EvenStart_RandomFloatInputs(int length)
        {
            float[] x = MakeRandom(length, seed: 71 + length);
            float[] y = Forward97(x, startingParity: 0);
            float[] xRecovered = InverseLifting97.Apply(y, startingParity: 0);

            AssertCloseElementwise(x, xRecovered, RoundTripTolerance);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(16)]
        [InlineData(17)]
        public void RoundTrip_OddStart_RandomFloatInputs(int length)
        {
            float[] x = MakeRandom(length, seed: 311 + length);
            float[] y = Forward97(x, startingParity: 1);
            float[] xRecovered = InverseLifting97.Apply(y, startingParity: 1);

            AssertCloseElementwise(x, xRecovered, RoundTripTolerance);
        }

        [Fact]
        public void RoundTrip_NegativeAndPositive_StillReversible()
        {
            float[] x = { -500.5f, 250.25f, -125.125f, 60f, -30.5f, 15f, -7f, 3.5f };
            float[] y = Forward97(x, startingParity: 0);
            float[] xRecovered = InverseLifting97.Apply(y, startingParity: 0);
            AssertCloseElementwise(x, xRecovered, RoundTripTolerance);
        }

        [Fact]
        public void RoundTrip_Impulse_OddStart_Length7()
        {
            float[] x = { 0f, 0f, 0f, 100f, 0f, 0f, 0f };
            float[] y = Forward97(x, startingParity: 1);
            float[] xRecovered = InverseLifting97.Apply(y, startingParity: 1);
            AssertCloseElementwise(x, xRecovered, RoundTripTolerance);
        }

        // ==== Constants smoke test =========================================

        [Fact]
        public void Constants_KAndInvK_AreReciprocal()
        {
            // Sanity: float-cast K · InvK is within float-epsilon of 1.
            float product = WaveletConstants.K * WaveletConstants.InvK;
            Assert.InRange(product, 1f - 1e-6f, 1f + 1e-6f);
        }

        // ==== Test helpers =================================================

        /// <summary>
        /// Forward 9/7 lifting per ISO/IEC 15444-1 F.4.8.2 (1D_FILTD9-7I).
        /// Six steps in order: P0 → U0 → P1 → U1 → scale-odd-by-K →
        /// scale-even-by-1/K.
        /// </summary>
        private static float[] Forward97(float[] x, int startingParity)
        {
            int length = x.Length;
            if (length == 1)
            {
                float v = x[0];
                return new[] { startingParity == 0 ? v : v * 2f };
            }

            const int pad = 2;
            var buf = new float[length + 2 * pad];
            Array.Copy(x, 0, buf, pad, length);
            SymmetricExtension.Fill(buf, pad, length);

            int firstEven = (startingParity == 0) ? 0 : 1;
            int firstOdd = (startingParity == 0) ? 1 : 0;

            // STEP 1 — forward P0 on canvas-odd: Y(2n+1) = X(2n+1) + α·(X(2n)+X(2n+2)).
            for (int local = firstOdd; local < length; local += 2)
            {
                int b = local + pad;
                buf[b] += WaveletConstants.Alpha * (buf[b - 1] + buf[b + 1]);
            }
            SymmetricExtension.Fill(buf, pad, length);

            // STEP 2 — forward U0 on canvas-even.
            for (int local = firstEven; local < length; local += 2)
            {
                int b = local + pad;
                buf[b] += WaveletConstants.Beta * (buf[b - 1] + buf[b + 1]);
            }
            SymmetricExtension.Fill(buf, pad, length);

            // STEP 3 — forward P1 on canvas-odd.
            for (int local = firstOdd; local < length; local += 2)
            {
                int b = local + pad;
                buf[b] += WaveletConstants.Gamma * (buf[b - 1] + buf[b + 1]);
            }
            SymmetricExtension.Fill(buf, pad, length);

            // STEP 4 — forward U1 on canvas-even.
            for (int local = firstEven; local < length; local += 2)
            {
                int b = local + pad;
                buf[b] += WaveletConstants.Delta * (buf[b - 1] + buf[b + 1]);
            }

            // STEP 5 — scale odd by K.
            for (int local = firstOdd; local < length; local += 2)
                buf[local + pad] *= WaveletConstants.K;

            // STEP 6 — scale even by 1/K.
            for (int local = firstEven; local < length; local += 2)
                buf[local + pad] *= WaveletConstants.InvK;

            var result = new float[length];
            Array.Copy(buf, pad, result, 0, length);
            return result;
        }

        private static float[] MakeRandom(int length, int seed)
        {
            var rng = new Random(seed);
            var arr = new float[length];
            for (var i = 0; i < length; i++)
                arr[i] = (float)(rng.NextDouble() * 2048.0 - 1024.0);
            return arr;
        }

        private static void AssertCloseElementwise(float[] expected, float[] actual, float tolerance)
        {
            Assert.Equal(expected.Length, actual.Length);
            for (var i = 0; i < expected.Length; i++)
            {
                float diff = MathF.Abs(expected[i] - actual[i]);
                Assert.True(
                    diff <= tolerance,
                    $"At index {i}: expected {expected[i]:R}, got {actual[i]:R}, diff {diff:R} > {tolerance}");
            }
        }
    }
}
