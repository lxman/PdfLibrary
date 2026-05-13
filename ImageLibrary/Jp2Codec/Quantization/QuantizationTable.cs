using System;
using System.Collections.Generic;
using System.IO;
using Jp2Codec.Codestream.Segments;

namespace Jp2Codec.Quantization
{
    /// <summary>
    /// Builds the per-subband <see cref="SubbandQuantization"/> table for one
    /// tile-component from a QCD/QCC pair plus the COD-supplied decomposition
    /// level count and component bit depth (R_I from SIZ).
    /// Handles the three QCD styles:
    /// <list type="bullet">
    ///   <item>None — reversible; epsilon_b per subband, mu_b = 0, Delta_b = 1.</item>
    ///   <item>ScalarDerived — irreversible; only (epsilon_0, mu_0) for NLLL is signalled,
    ///         the rest are derived via E-5.</item>
    ///   <item>ScalarExpounded — irreversible; (epsilon_b, mu_b) for every subband.</item>
    /// </list>
    /// </summary>
    internal static class QuantizationTable
    {
        /// <summary>
        /// Build a band table for one tile-component.
        /// </summary>
        /// <param name="numDecompositionLevels">N_L from COD/COC.</param>
        /// <param name="guardBits">G from the Sqcd byte (0..7).</param>
        /// <param name="style">Quantization style from the Sqcd byte.</param>
        /// <param name="exponents">Epsilon values in QCD order (length depends on style).</param>
        /// <param name="mantissas">Mu values; empty for reversible.</param>
        /// <param name="componentBitDepth">R_I, the original sample bit depth from SIZ.</param>
        /// <param name="isReversible">True if the COD specifies the 5/3 reversible kernel.</param>
        public static SubbandQuantization[] Build(
            int numDecompositionLevels,
            int guardBits,
            QuantizationStyle style,
            IReadOnlyList<int> exponents,
            IReadOnlyList<int> mantissas,
            int componentBitDepth,
            bool isReversible)
        {
            if (numDecompositionLevels < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(numDecompositionLevels), numDecompositionLevels, null);
            if (guardBits < 0 || guardBits > 7)
                throw new ArgumentOutOfRangeException(nameof(guardBits), guardBits, null);
            if (componentBitDepth < 1)
                throw new ArgumentOutOfRangeException(
                    nameof(componentBitDepth), componentBitDepth, null);
            if (exponents is null) throw new ArgumentNullException(nameof(exponents));
            if (mantissas is null) throw new ArgumentNullException(nameof(mantissas));

            ValidateStyleConsistency(style, isReversible, mantissas);

            SubbandDescriptor[] descriptors = SubbandLayout.EnumerateQcdOrder(numDecompositionLevels);
            int subbandCount = descriptors.Length;

            ValidateArrayLengths(style, subbandCount, exponents, mantissas);

            var result = new SubbandQuantization[subbandCount];
            for (var i = 0; i < subbandCount; i++)
            {
                SubbandDescriptor d = descriptors[i];
                (int eps, int mu) = ResolveEpsilonMu(style, i, d, numDecompositionLevels, exponents, mantissas);

                int magnitudeBits = guardBits + eps - 1;
                int log2Gain = SubbandLayout.Log2Gain(d.Orientation);
                int rb = componentBitDepth + log2Gain;
                double delta = isReversible
                    ? 1.0
                    : ComputeIrreversibleStepSize(rb, eps, mu);

                result[i] = new SubbandQuantization(
                    descriptor: d,
                    exponent: eps,
                    mantissa: mu,
                    magnitudeBits: magnitudeBits,
                    stepSize: delta,
                    dynamicRange: rb,
                    isReversible: isReversible);
            }

            return result;
        }

        /// <summary>
        /// Compute Delta_b = 2^(R_b - epsilon_b) · (1 + mu_b / 2^11) (E-3).
        /// The first factor is exact (a power of two); the mantissa-driven
        /// scale multiplies a value in [1, 2).
        /// </summary>
        private static double ComputeIrreversibleStepSize(int rb, int eps, int mu)
        {
            int exponent = rb - eps;
            double powerOfTwo = exponent >= 0
                ? unchecked((double)(1L << exponent))
                : 1.0 / unchecked((double)(1L << -exponent));
            double mantissaScale = 1.0 + mu / 2048.0;
            return powerOfTwo * mantissaScale;
        }

        private static void ValidateStyleConsistency(
            QuantizationStyle style, bool isReversible, IReadOnlyList<int> mantissas)
        {
            switch (style)
            {
                case QuantizationStyle.None:
                    if (!isReversible)
                        throw new InvalidDataException(
                            "Quantization style 'None' requires the reversible 5/3 wavelet kernel.");
                    if (mantissas.Count != 0)
                        throw new InvalidDataException(
                            "Quantization style 'None' must have zero mantissas (mu_b implicit zero).");
                    break;
                case QuantizationStyle.ScalarDerived:
                case QuantizationStyle.ScalarExpounded:
                    if (isReversible)
                        throw new InvalidDataException(
                            $"Quantization style '{style}' requires the irreversible 9/7 kernel.");
                    break;
                default:
                    throw new InvalidDataException($"Unsupported quantization style {(int)style}.");
            }
        }

        private static void ValidateArrayLengths(
            QuantizationStyle style,
            int subbandCount,
            IReadOnlyList<int> exponents,
            IReadOnlyList<int> mantissas)
        {
            switch (style)
            {
                case QuantizationStyle.None:
                    if (exponents.Count != subbandCount)
                        throw new InvalidDataException(
                            $"QCD style 'None': expected {subbandCount} exponents, got {exponents.Count}.");
                    break;
                case QuantizationStyle.ScalarDerived:
                    if (exponents.Count != 1 || mantissas.Count != 1)
                        throw new InvalidDataException(
                            "QCD style 'ScalarDerived' must carry exactly one (epsilon, mu) pair.");
                    break;
                case QuantizationStyle.ScalarExpounded:
                    if (exponents.Count != subbandCount || mantissas.Count != subbandCount)
                        throw new InvalidDataException(
                            $"QCD style 'ScalarExpounded': expected {subbandCount} (epsilon, mu) pairs, " +
                            $"got {exponents.Count} epsilons and {mantissas.Count} mantissas.");
                    break;
            }
        }

        private static (int Epsilon, int Mantissa) ResolveEpsilonMu(
            QuantizationStyle style,
            int subbandIndex,
            SubbandDescriptor descriptor,
            int numDecompositionLevels,
            IReadOnlyList<int> exponents,
            IReadOnlyList<int> mantissas)
        {
            switch (style)
            {
                case QuantizationStyle.None:
                    return (exponents[subbandIndex], 0);

                case QuantizationStyle.ScalarExpounded:
                    return (exponents[subbandIndex], mantissas[subbandIndex]);

                case QuantizationStyle.ScalarDerived:
                {
                    // E-5: epsilon_b = epsilon_0 - (N_L - n_b), mu_b = mu_0.
                    // The signalled pair (epsilon_0, mu_0) corresponds to the NLLL band.
                    int eps0 = exponents[0];
                    int mu0 = mantissas[0];
                    int eps = eps0 - (numDecompositionLevels - descriptor.DecompositionLevel);
                    if (eps < 0)
                        throw new InvalidDataException(
                            $"Derived quantization underflowed at subband {descriptor}: " +
                            $"epsilon_0 ({eps0}) - (N_L - n_b) = {eps} < 0.");
                    return (eps, mu0);
                }

                default:
                    throw new InvalidOperationException();
            }
        }
    }
}
