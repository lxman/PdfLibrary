using System;
using Jp2Codec.Wavelet;

namespace Jp2Codec.Tests.Wavelet
{
    public sealed class InverseDwt1DTests
    {
        private const float Tolerance97 = 1e-3f;

        // ==== 5/3 reversible ===============================================

        [Fact]
        public void Reverse53_EvenStart_ConstantSignal_Reconstructs()
        {
            // Forward 5/3 of [c,c,c,c,c,c,c,c] → low = [c]*4, high = [0]*4.
            int[] aL = { 7, 7, 7, 7 };
            int[] aH = { 0, 0, 0, 0 };
            int[] x = InverseDwt1D.Reverse53(aL, aH, startingParity: 0);
            Assert.Equal(new[] { 7, 7, 7, 7, 7, 7, 7, 7 }, x);
        }

        [Fact]
        public void Reverse53_OddStart_AllZero_StaysZero()
        {
            int[] aL = { 0, 0, 0 };
            int[] aH = { 0, 0, 0, 0 };
            int[] x = InverseDwt1D.Reverse53(aL, aH, startingParity: 1);
            Assert.Equal(new[] { 0, 0, 0, 0, 0, 0, 0 }, x);
        }

        [Fact]
        public void Reverse53_OnlyHigh_OddStart_ProducesHalvedSignal()
        {
            // Length-1 case via the lifting (here L=2 actually): the
            // orchestrator simply delegates to the lifting kernel.
            int[] aL = Array.Empty<int>();
            int[] aH = { 84 };
            int[] x = InverseDwt1D.Reverse53(aL, aH, startingParity: 1);
            Assert.Equal(new[] { 42 }, x);
        }

        // ==== 9/7 irreversible =============================================

        [Fact]
        public void Reverse97_EvenStart_ConstantSignal_Reconstructs()
        {
            float[] aL = { 5f, 5f, 5f, 5f };
            float[] aH = { 0f, 0f, 0f, 0f };
            float[] x = InverseDwt1D.Reverse97(aL, aH, startingParity: 0);
            Assert.Equal(8, x.Length);
            foreach (float v in x)
                Assert.InRange(v, 5f - 1e-5f, 5f + 1e-5f);
        }

        [Fact]
        public void Reverse97_OddStart_AllZero_StaysZero()
        {
            float[] aL = { 0f, 0f, 0f };
            float[] aH = { 0f, 0f, 0f, 0f };
            float[] x = InverseDwt1D.Reverse97(aL, aH, startingParity: 1);
            foreach (float v in x)
                Assert.Equal(0f, v);
        }

        // ==== Length mismatch propagates from interleave ===================

        [Fact]
        public void Reverse53_LengthMismatch_Throws()
        {
            // total=4, parity 0 expects 2 lows + 2 highs. Pass 3+1.
            Assert.Throws<ArgumentException>(() =>
                InverseDwt1D.Reverse53(new[] { 1, 2, 3 }, new[] { 10 }, 0));
        }

        [Fact]
        public void Reverse97_LengthMismatch_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                InverseDwt1D.Reverse97(new[] { 1f, 2f, 3f }, new[] { 10f }, 0));
        }
    }
}
