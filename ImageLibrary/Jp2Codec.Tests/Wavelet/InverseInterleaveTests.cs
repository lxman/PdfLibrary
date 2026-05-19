using Jp2Codec.Wavelet;

namespace Jp2Codec.Tests.Wavelet
{
    public sealed class InverseInterleaveTests
    {
        // ==== int overloads ================================================

        [Fact]
        public void Int_LowFirst_EvenLength_AlternatesLowAndHigh()
        {
            int[] aL = { 10, 20, 30, 40 };
            int[] aH = { 11, 21, 31, 41 };

            int[] x = InverseInterleave.Combine(aL, aH, startingParity: 0);

            Assert.Equal(new[] { 10, 11, 20, 21, 30, 31, 40, 41 }, x);
        }

        [Fact]
        public void Int_HighFirst_EvenLength_AlternatesHighAndLow()
        {
            int[] aL = { 10, 20, 30, 40 };
            int[] aH = { 11, 21, 31, 41 };

            int[] x = InverseInterleave.Combine(aL, aH, startingParity: 1);

            Assert.Equal(new[] { 11, 10, 21, 20, 31, 30, 41, 40 }, x);
        }

        [Fact]
        public void Int_LowFirst_OddLength_LowEndsTheSignal()
        {
            // i0 = 0 (even), i1 = 7 → 4 lows, 3 highs.
            int[] aL = { 10, 20, 30, 40 };
            int[] aH = { 11, 21, 31 };

            int[] x = InverseInterleave.Combine(aL, aH, startingParity: 0);

            Assert.Equal(new[] { 10, 11, 20, 21, 30, 31, 40 }, x);
        }

        [Fact]
        public void Int_HighFirst_OddLength_HighEndsTheSignal()
        {
            // i0 = 1 (odd), i1 = 8 → 3 lows, 4 highs.
            int[] aL = { 10, 20, 30 };
            int[] aH = { 11, 21, 31, 41 };

            int[] x = InverseInterleave.Combine(aL, aH, startingParity: 1);

            Assert.Equal(new[] { 11, 10, 21, 20, 31, 30, 41 }, x);
        }

        [Fact]
        public void Int_SingleLowSample_ReturnsLowOnly()
        {
            int[] aL = { 42 };
            int[] aH = Array.Empty<int>();

            int[] x = InverseInterleave.Combine(aL, aH, startingParity: 0);

            Assert.Equal(new[] { 42 }, x);
        }

        [Fact]
        public void Int_SingleHighSample_ReturnsHighOnly()
        {
            int[] aL = Array.Empty<int>();
            int[] aH = { 99 };

            int[] x = InverseInterleave.Combine(aL, aH, startingParity: 1);

            Assert.Equal(new[] { 99 }, x);
        }

        [Fact]
        public void Int_LengthMismatch_Throws()
        {
            // total 5, parity 0 → expect 3 lows + 2 highs. Pass 2 lows + 3 highs.
            int[] aL = { 1, 2 };
            int[] aH = { 10, 20, 30 };

            Assert.Throws<ArgumentException>(() =>
                InverseInterleave.Combine(aL, aH, startingParity: 0));
        }

        [Fact]
        public void Int_InvalidParity_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                InverseInterleave.Combine(new[] { 1 }, Array.Empty<int>(), startingParity: 2));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                InverseInterleave.Combine(new[] { 1 }, Array.Empty<int>(), startingParity: -1));
        }

        // ==== float overloads ==============================================

        [Fact]
        public void Float_LowFirst_AlternatesLowAndHigh()
        {
            float[] aL = { 1.0f, 2.0f, 3.0f };
            float[] aH = { 10.0f, 20.0f, 30.0f };

            float[] x = InverseInterleave.Combine(aL, aH, startingParity: 0);

            Assert.Equal(new[] { 1f, 10f, 2f, 20f, 3f, 30f }, x);
        }

        [Fact]
        public void Float_HighFirst_AlternatesHighAndLow()
        {
            float[] aL = { 1.0f, 2.0f, 3.0f };
            float[] aH = { 10.0f, 20.0f, 30.0f };

            float[] x = InverseInterleave.Combine(aL, aH, startingParity: 1);

            Assert.Equal(new[] { 10f, 1f, 20f, 2f, 30f, 3f }, x);
        }

        [Fact]
        public void Float_LengthMismatch_Throws()
        {
            float[] aL = { 1f, 2f };
            float[] aH = { 10f };

            // total 3, parity 0 → expect 2 lows + 1 high. (Correct sizes — no throw.)
            float[] ok = InverseInterleave.Combine(aL, aH, startingParity: 0);
            Assert.Equal(3, ok.Length);

            // total 3, parity 1 → expect 1 low + 2 highs. Reject 2/1.
            Assert.Throws<ArgumentException>(() =>
                InverseInterleave.Combine(aL, aH, startingParity: 1));
        }
    }
}
