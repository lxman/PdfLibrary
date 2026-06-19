using Jp2Codec.Wavelet;

namespace Jp2Codec.Tests.Wavelet
{
    public sealed class InverseLifting53Tests
    {
        // ==== Length-1 (F.3.6) =============================================

        [Fact]
        public void LengthOne_EvenStart_PassesThrough()
        {
            int[] x = InverseLifting53.Apply(new[] { 42 }, startingParity: 0);
            Assert.Equal(new[] { 42 }, x);
        }

        [Fact]
        public void LengthOne_OddStart_HalvesValue()
        {
            // Forward emits 2·X for a single odd-start sample; inverse halves it.
            int[] x = InverseLifting53.Apply(new[] { 84 }, startingParity: 1);
            Assert.Equal(new[] { 42 }, x);
        }

        [Fact]
        public void LengthOne_OddStart_NegativeHalvedTowardNegativeInfinity()
        {
            // Inverse uses arithmetic shift = floor division, matching OpenJPEG.
            // floor(-3 / 2) = -2 (not -1 from truncation).
            int[] x = InverseLifting53.Apply(new[] { -3 }, startingParity: 1);
            Assert.Equal(new[] { -2 }, x);
        }

        // ==== Hand-computed direct tests ==================================

        [Fact]
        public void EvenStart_Length4_DiracLowFirst()
        {
            // Y = [10, 0, 20, 0] with parity 0 (low,high,low,high).
            // F-5: X(0)=10 - ⌊(2·0+2)/4⌋ = 10. X(2)=20 - ⌊(0+0+2)/4⌋ = 20.
            // F-6: X(1)=0 + ⌊(10+20)/2⌋ = 15.
            //      X(3)=0 + ⌊(X(2)+X(4))/2⌋ = ⌊(20+20)/2⌋ = 20 (X(4) reflects to X(2)).
            int[] result = InverseLifting53.Apply(new[] { 10, 0, 20, 0 }, startingParity: 0);
            Assert.Equal(new[] { 10, 15, 20, 20 }, result);
        }

        [Fact]
        public void EvenStart_Length4_AllZero_ProducesAllZero()
        {
            int[] result = InverseLifting53.Apply(new[] { 0, 0, 0, 0 }, startingParity: 0);
            Assert.Equal(new[] { 0, 0, 0, 0 }, result);
        }

        // ==== Constant-input identity (low-only Y reconstructs a flat X) ===

        [Fact]
        public void EvenStart_ConstantLow_FlatHigh_ReconstructsFlatSignal()
        {
            // Forward 5/3 of [c,c,c,c,c,c,c,c] produces low=[c,c,c,c], high=[0,0,0,0]
            // interleaved as [c,0,c,0,c,0,c,0]. The inverse should recover [c]*8.
            var c = 42;
            int[] y = { c, 0, c, 0, c, 0, c, 0 };
            int[] x = InverseLifting53.Apply(y, startingParity: 0);
            Assert.Equal(Repeat(c, 8), x);
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
        public void RoundTrip_EvenStart_RandomInputs(int length)
        {
            int[] x = MakeRandom(length, seed: 17 + length);
            int[] y = Forward53(x, startingParity: 0);
            int[] xRecovered = InverseLifting53.Apply(y, startingParity: 0);
            Assert.Equal(x, xRecovered);
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
        public void RoundTrip_OddStart_RandomInputs(int length)
        {
            int[] x = MakeRandom(length, seed: 91 + length);
            int[] y = Forward53(x, startingParity: 1);
            int[] xRecovered = InverseLifting53.Apply(y, startingParity: 1);
            Assert.Equal(x, xRecovered);
        }

        [Fact]
        public void RoundTrip_NegativeValues_StillReversible()
        {
            int[] x = { -100, 50, -25, 12, -6, 3, -1, 0 };
            int[] y = Forward53(x, startingParity: 0);
            Assert.Equal(x, InverseLifting53.Apply(y, startingParity: 0));
        }

        [Fact]
        public void RoundTrip_Impulse_OddStart_Length7()
        {
            int[] x = { 0, 0, 0, 100, 0, 0, 0 };
            int[] y = Forward53(x, startingParity: 1);
            Assert.Equal(x, InverseLifting53.Apply(y, startingParity: 1));
        }

        // ==== Test helpers =================================================

        /// <summary>
        /// Forward 5/3 lifting per ISO/IEC 15444-1 F.4.8.1 (1D_FILTD5-3R)
        /// used to round-trip the inverse. Produces an interleaved Y signal
        /// where canvas-even positions hold low-pass and canvas-odd positions
        /// hold high-pass coefficients.
        /// </summary>
        private static int[] Forward53(int[] x, int startingParity)
        {
            int length = x.Length;
            if (length == 1)
            {
                int v = x[0];
                return new[] { startingParity == 0 ? v : v * 2 };
            }

            const int pad = 2;
            var buf = new int[length + 2 * pad];
            Array.Copy(x, 0, buf, pad, length);
            SymmetricExtension.Fill(buf, pad, length);

            // F-3 (forward predict on canvas-odd):
            //   Y(2n+1) = X(2n+1) - ⌊(X(2n) + X(2n+2)) / 2⌋
            int firstOdd = (startingParity == 0) ? 1 : 0;
            for (int local = firstOdd; local < length; local += 2)
            {
                int b = local + pad;
                int sum = buf[b - 1] + buf[b + 1];
                buf[b] -= sum >> 1;
            }

            // Refresh padding so canvas-odd slots reflect updated Y.
            SymmetricExtension.Fill(buf, pad, length);

            // F-4 (forward update on canvas-even):
            //   Y(2n) = X(2n) + ⌊(Y(2n-1) + Y(2n+1) + 2) / 4⌋
            int firstEven = (startingParity == 0) ? 0 : 1;
            for (int local = firstEven; local < length; local += 2)
            {
                int b = local + pad;
                int sum = buf[b - 1] + buf[b + 1] + 2;
                buf[b] += sum >> 2;
            }

            var result = new int[length];
            Array.Copy(buf, pad, result, 0, length);
            return result;
        }

        private static int[] MakeRandom(int length, int seed)
        {
            var rng = new Random(seed);
            var arr = new int[length];
            for (var i = 0; i < length; i++) arr[i] = rng.Next(-1024, 1024);
            return arr;
        }

        private static int[] Repeat(int value, int count)
        {
            var arr = new int[count];
            Array.Fill(arr, value);
            return arr;
        }
    }
}
