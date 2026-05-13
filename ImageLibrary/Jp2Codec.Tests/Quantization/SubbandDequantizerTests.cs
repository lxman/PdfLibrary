using System;
using Jp2Codec.Quantization;

namespace Jp2Codec.Tests.Quantization
{
    public sealed class SubbandDequantizerTests
    {
        // ==== Reversible ====================================================

        [Fact]
        public void Reversible_NoMissingBitPlanes_PassesThrough()
        {
            int[,] q =
            {
                { 0, 5, -3, 10 },
                { -1, 0, 7, -100 },
            };

            int[,] r = SubbandDequantizer.DequantizeReversible(q, missingBitPlanes: 0);

            Assert.Equal(2, r.GetLength(0));
            Assert.Equal(4, r.GetLength(1));
            for (var y = 0; y < 2; y++)
                for (var x = 0; x < 4; x++)
                    Assert.Equal(q[y, x], r[y, x]);
        }

        [Fact]
        public void Reversible_OneMissingBitPlane_AppliesUnitBias()
        {
            // missingBitPlanes = 1 → bias = 2^0 = 1 magnitude, signed away from zero.
            int[,] q =
            {
                { 0, 5, -3 },
            };

            int[,] r = SubbandDequantizer.DequantizeReversible(q, missingBitPlanes: 1);

            Assert.Equal(0, r[0, 0]);
            Assert.Equal(6, r[0, 1]);   // 5 + 1
            Assert.Equal(-4, r[0, 2]);  // -3 - 1
        }

        [Fact]
        public void Reversible_ThreeMissingBitPlanes_AppliesPowerOfTwoBias()
        {
            // 2^(missingBitPlanes - 1) = 2^2 = 4.
            int[,] q =
            {
                { 0, 8, -16 },
            };

            int[,] r = SubbandDequantizer.DequantizeReversible(q, missingBitPlanes: 3);

            Assert.Equal(0, r[0, 0]);
            Assert.Equal(12, r[0, 1]);  // 8 + 4
            Assert.Equal(-20, r[0, 2]); // -16 - 4
        }

        [Fact]
        public void Reversible_RejectsNegativeMissingBitPlanes()
        {
            int[,] q = { { 0 } };
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SubbandDequantizer.DequantizeReversible(q, missingBitPlanes: -1));
        }

        [Fact]
        public void Reversible_OutputIsIndependentInstance()
        {
            // Sanity: editing the output must not affect the input. Guards
            // against accidental aliasing later on (e.g. ref semantics).
            int[,] q = { { 5 } };
            int[,] r = SubbandDequantizer.DequantizeReversible(q, missingBitPlanes: 0);
            r[0, 0] = 99;
            Assert.Equal(5, q[0, 0]);
        }

        // ==== Irreversible ==================================================

        [Fact]
        public void Irreversible_NoMissingBitPlanes_RZero_AppliesPlainStepSize()
        {
            int[,] q =
            {
                { 0, 4, -7 },
            };

            float[,] r = SubbandDequantizer.DequantizeIrreversible(
                q, stepSize: 0.5, missingBitPlanes: 0, reconstructionParameter: 0.0);

            Assert.Equal(0f, r[0, 0]);
            Assert.Equal(2f, r[0, 1]);    // 4 * 0.5
            Assert.Equal(-3.5f, r[0, 2]); // -7 * 0.5
        }

        [Fact]
        public void Irreversible_DefaultMidpointBias_NoMissingBitPlanes()
        {
            // r = 0.5, missingBitPlanes = 0 → 2^0 = 1, so bias = 0.5.
            // Rq = sign(q) * (|q| + 0.5) * Delta.
            int[,] q = { { 4, -7 } };
            float[,] r = SubbandDequantizer.DequantizeIrreversible(
                q, stepSize: 0.5, missingBitPlanes: 0);

            Assert.Equal(4.5f * 0.5f, r[0, 0]);  // (4 + 0.5) * 0.5
            Assert.Equal(-7.5f * 0.5f, r[0, 1]); // (-7 - 0.5) * 0.5
        }

        [Fact]
        public void Irreversible_TwoMissingBitPlanes_RHalfScalesByFour()
        {
            // r = 0.5, missingBitPlanes = 2 → 2^2 = 4, so signed bias magnitude = 2.
            int[,] q = { { 8, -12 } };
            float[,] r = SubbandDequantizer.DequantizeIrreversible(
                q, stepSize: 1.0, missingBitPlanes: 2);

            Assert.Equal(10f, r[0, 0]);  // (8 + 2) * 1.0
            Assert.Equal(-14f, r[0, 1]); // (-12 - 2) * 1.0
        }

        [Fact]
        public void Irreversible_ZeroCoefficientStaysZeroRegardlessOfBias()
        {
            int[,] q = { { 0, 0, 0 } };
            float[,] r = SubbandDequantizer.DequantizeIrreversible(
                q, stepSize: 12.5, missingBitPlanes: 5);

            Assert.All(new[] { r[0, 0], r[0, 1], r[0, 2] }, v => Assert.Equal(0f, v));
        }

        [Fact]
        public void Irreversible_RejectsReconstructionParameterOutOfRange()
        {
            int[,] q = { { 0 } };
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SubbandDequantizer.DequantizeIrreversible(q, 1.0, 0, reconstructionParameter: -0.01));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SubbandDequantizer.DequantizeIrreversible(q, 1.0, 0, reconstructionParameter: 1.0));
        }

        [Fact]
        public void Irreversible_RejectsNegativeMissingBitPlanes()
        {
            int[,] q = { { 0 } };
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SubbandDequantizer.DequantizeIrreversible(q, 1.0, missingBitPlanes: -1));
        }

        [Fact]
        public void Irreversible_StepSizeOneEquivalentToReversibleAtR0()
        {
            // With stepSize = 1 and r = 0, the irreversible path should yield
            // the same numeric values as the reversible passthrough.
            int[,] q =
            {
                { 0, 5, -3, 10 },
                { -1, 0, 7, -100 },
            };

            int[,] rev = SubbandDequantizer.DequantizeReversible(q, missingBitPlanes: 0);
            float[,] irr = SubbandDequantizer.DequantizeIrreversible(
                q, stepSize: 1.0, missingBitPlanes: 0, reconstructionParameter: 0.0);

            for (var y = 0; y < 2; y++)
                for (var x = 0; x < 4; x++)
                    Assert.Equal((float)rev[y, x], irr[y, x]);
        }
    }
}
