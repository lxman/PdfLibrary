using Jp2Codec.Codestream;
using Jp2Codec.Codestream.Segments;
using Jp2Codec.Quantization;

namespace Jp2Codec.Tests.Quantization
{
    public sealed class QuantizationTableTests
    {
        // ==== None (reversible) =============================================

        [Fact]
        public void None_PerSubbandExponents_PopulateInQcdOrder()
        {
            // NL=1, 4 subbands, exponents one byte each, mu_b = 0.
            int[] eps = [8, 7, 7, 6];
            SubbandQuantization[] table = QuantizationTable.Build(
                numDecompositionLevels: 1,
                guardBits: 2,
                style: QuantizationStyle.None,
                exponents: eps,
                mantissas: [],
                componentBitDepth: 8,
                isReversible: true);

            Assert.Equal(4, table.Length);
            Assert.Equal(8, table[0].Exponent);
            Assert.Equal(0, table[0].Mantissa);
            // M_b = G + epsilon_b - 1 = 2 + 8 - 1 = 9.
            Assert.Equal(9, table[0].MagnitudeBits);
            Assert.Equal(1.0, table[0].StepSize);
            Assert.True(table[0].IsReversible);
        }

        [Fact]
        public void None_PerSubbandExponents_DynamicRangeAddsLog2Gain()
        {
            int[] eps = [8, 7, 7, 6];
            SubbandQuantization[] table = QuantizationTable.Build(
                1, 2, QuantizationStyle.None, eps, [], componentBitDepth: 8, isReversible: true);

            // R_b = R_I + log2(gain_b): LL→8, HL→9, LH→9, HH→10.
            Assert.Equal(8, table[0].DynamicRange);  // LL
            Assert.Equal(9, table[1].DynamicRange);  // HL
            Assert.Equal(9, table[2].DynamicRange);  // LH
            Assert.Equal(10, table[3].DynamicRange); // HH
        }

        [Fact]
        public void None_StyleRejectedForIrreversibleKernel()
        {
            Assert.Throws<InvalidDataException>(() => QuantizationTable.Build(
                1, 0, QuantizationStyle.None, [8, 7, 7, 6], [], 8, isReversible: false));
        }

        [Fact]
        public void None_RequiresEmptyMantissasArray()
        {
            Assert.Throws<InvalidDataException>(() => QuantizationTable.Build(
                1, 0, QuantizationStyle.None, [8], [123], 8, isReversible: true));
        }

        // ==== ScalarExpounded ==============================================

        [Fact]
        public void Expounded_FullEpsilonMuArrays_PopulatePerSubband()
        {
            // NL=2, 7 subbands.
            int[] eps = [12, 11, 11, 10, 10, 10, 9];
            int[] mu = [100, 200, 200, 400, 500, 500, 600];

            SubbandQuantization[] table = QuantizationTable.Build(
                2, 1, QuantizationStyle.ScalarExpounded, eps, mu, 8, isReversible: false);

            Assert.Equal(7, table.Length);
            for (var i = 0; i < table.Length; i++)
            {
                Assert.Equal(eps[i], table[i].Exponent);
                Assert.Equal(mu[i], table[i].Mantissa);
                Assert.False(table[i].IsReversible);
            }
        }

        [Fact]
        public void Expounded_StepSizeMatchesE3()
        {
            // Single-level expounded with known values. Verify against E-3:
            // Delta_b = 2^(R_b - epsilon_b) · (1 + mu_b / 2^11).
            int[] eps = [12, 11, 11, 10];
            int[] mu = [0, 1024, 2047, 0]; // 0, half, max-1, 0.

            SubbandQuantization[] table = QuantizationTable.Build(
                1, 0, QuantizationStyle.ScalarExpounded, eps, mu, componentBitDepth: 8, isReversible: false);

            // LL: R_b=8, eps=12 → 2^(-4) · (1 + 0/2048) = 0.0625.
            Assert.Equal(0.0625, table[0].StepSize, 10);
            // HL: R_b=9, eps=11 → 2^(-2) · (1 + 1024/2048) = 0.25 · 1.5 = 0.375.
            Assert.Equal(0.375, table[1].StepSize, 10);
            // LH: R_b=9, eps=11 → 0.25 · (1 + 2047/2048) ≈ 0.4998779...
            Assert.Equal(0.25 * (1.0 + 2047.0 / 2048.0), table[2].StepSize, 12);
            // HH: R_b=10, eps=10 → 1.0 · 1.0 = 1.0.
            Assert.Equal(1.0, table[3].StepSize, 10);
        }

        [Fact]
        public void Expounded_StyleRejectedForReversibleKernel()
        {
            Assert.Throws<InvalidDataException>(() => QuantizationTable.Build(
                1, 0, QuantizationStyle.ScalarExpounded, [8, 8, 8, 8], [0, 0, 0, 0], 8, isReversible: true));
        }

        [Fact]
        public void Expounded_RejectsMismatchedArrayLength()
        {
            Assert.Throws<InvalidDataException>(() => QuantizationTable.Build(
                2, 0, QuantizationStyle.ScalarExpounded, [8, 7, 7], [0, 0, 0], 8, isReversible: false));
        }

        // ==== ScalarDerived =================================================

        [Fact]
        public void Derived_RequiresExactlyOnePair()
        {
            // Two pairs supplied → invalid.
            Assert.Throws<InvalidDataException>(() => QuantizationTable.Build(
                2, 0, QuantizationStyle.ScalarDerived, [12, 11], [0, 0], 8, isReversible: false));
        }

        [Fact]
        public void Derived_EpsilonDerivesPerE5()
        {
            // E-5: epsilon_b = epsilon_0 - (N_L - n_b). mu_b = mu_0 for every band.
            int[] eps0 = [12];
            int[] mu0 = [555];

            SubbandQuantization[] table = QuantizationTable.Build(
                numDecompositionLevels: 3,
                guardBits: 1,
                style: QuantizationStyle.ScalarDerived,
                exponents: eps0,
                mantissas: mu0,
                componentBitDepth: 8,
                isReversible: false);

            // NL=3: subband layout NLLL(n=3), NLHL(n=3), NLLH(n=3), NLHH(n=3),
            // (NL-1)HL(n=2), (NL-1)LH(n=2), (NL-1)HH(n=2), 1HL(n=1), 1LH(n=1), 1HH(n=1).
            int[] expectedEpsilons =
            [
                12, 12, 12, 12,      // n_b = 3 → eps = 12 - (3-3) = 12
                11, 11, 11,          // n_b = 2 → eps = 12 - (3-2) = 11
                10, 10, 10,          // n_b = 1 → eps = 12 - (3-1) = 10
            ];

            for (var i = 0; i < table.Length; i++)
            {
                Assert.Equal(expectedEpsilons[i], table[i].Exponent);
                Assert.Equal(555, table[i].Mantissa);
            }
        }

        [Fact]
        public void Derived_StepSizeUsesPerBandEpsilonAndSharedMu()
        {
            // Single decomposition. The four bands all have n_b = 1 == N_L,
            // so no epsilon adjustment kicks in for any band.
            int[] eps0 = [12];
            int[] mu0 = [0];

            SubbandQuantization[] table = QuantizationTable.Build(
                1, 0, QuantizationStyle.ScalarDerived, eps0, mu0, componentBitDepth: 8, isReversible: false);

            // Same eps everywhere → Delta varies only by R_b: 2^(R_b - 12).
            Assert.Equal(Math.Pow(2, 8 - 12), table[0].StepSize, 12); // LL
            Assert.Equal(Math.Pow(2, 9 - 12), table[1].StepSize, 12); // HL
            Assert.Equal(Math.Pow(2, 9 - 12), table[2].StepSize, 12); // LH
            Assert.Equal(Math.Pow(2, 10 - 12), table[3].StepSize, 12); // HH
        }

        [Fact]
        public void Derived_DetectsEpsilonUnderflow()
        {
            // epsilon_0 = 1, N_L = 5 → for n_b = 1: epsilon = 1 - 4 = -3.
            Assert.Throws<InvalidDataException>(() => QuantizationTable.Build(
                5, 0, QuantizationStyle.ScalarDerived, [1], [0], 8, isReversible: false));
        }

        // ==== End-to-end QCD round-trip =====================================

        [Fact]
        public void Build_AcceptsQcdSegmentExponentsDirectly()
        {
            // Parsing a hand-built QCD then feeding the arrays into Build should
            // yield identical results — this ties the QCD parser and the
            // quantization-table builder together.
            byte sqcd = 0x20; // No-quantization style, 1 guard bit (high 3 bits = 001).
            // For NL=1, 4 subbands. Each byte: epsilon in top 5 bits, low 3 = 0.
            byte[] payload =
            [
                sqcd,
                (8 << 3), (7 << 3), (7 << 3), (6 << 3),
            ];

            var reader = new CodestreamReader(payload, offset: 0, length: payload.Length);
            QcdSegment qcd = QcdSegment.Parse(reader);

            SubbandQuantization[] table = QuantizationTable.Build(
                numDecompositionLevels: 1,
                guardBits: qcd.GuardBits,
                style: qcd.Style,
                exponents: qcd.Exponents,
                mantissas: qcd.Mantissas,
                componentBitDepth: 8,
                isReversible: true);

            Assert.Equal(1, qcd.GuardBits);
            Assert.Equal(QuantizationStyle.None, qcd.Style);
            Assert.Equal(8, table[0].Exponent);
            Assert.Equal(8, table[0].MagnitudeBits); // 1 + 8 - 1
            Assert.Equal(6, table[3].Exponent);
            Assert.Equal(6, table[3].MagnitudeBits);
        }
    }
}
