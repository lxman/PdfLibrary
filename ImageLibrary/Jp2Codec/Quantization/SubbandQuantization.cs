namespace Jp2Codec.Quantization
{
    /// <summary>
    /// Per-subband inverse-quantization parameters derived from QCD/QCC + COD +
    /// SIZ per ISO/IEC 15444-1 Annex E. Holds the raw (epsilon_b, mu_b) pair
    /// signalled (or derived) for this subband, the magnitude-bit count
    /// M_b = G + epsilon_b - 1 (E-2), the dequantization step size Delta_b
    /// (E-3 for irreversible / 1 for reversible), and the dynamic range
    /// R_b = R_I + log2(gain_b) (E-4).
    /// </summary>
    internal sealed class SubbandQuantization
    {
        public SubbandDescriptor Descriptor { get; }

        /// <summary>Exponent epsilon_b (0..31).</summary>
        public int Exponent { get; }

        /// <summary>Mantissa mu_b (0..2047). Zero for reversible (style None).</summary>
        public int Mantissa { get; }

        /// <summary>Total magnitude bit count M_b = G + epsilon_b - 1.</summary>
        public int MagnitudeBits { get; }

        /// <summary>
        /// Dequantization step size Delta_b. Equals 1 for reversible (E.1.2.1);
        /// for irreversible equals 2^(R_b - epsilon_b) · (1 + mu_b / 2^11) (E-3).
        /// </summary>
        public double StepSize { get; }

        /// <summary>Nominal dynamic range R_b = R_I + log2(gain_b). Used by E-3.</summary>
        public int DynamicRange { get; }

        /// <summary>True if the wavelet transform is the reversible 5/3 kernel.</summary>
        public bool IsReversible { get; }

        public SubbandQuantization(
            SubbandDescriptor descriptor,
            int exponent,
            int mantissa,
            int magnitudeBits,
            double stepSize,
            int dynamicRange,
            bool isReversible)
        {
            Descriptor = descriptor;
            Exponent = exponent;
            Mantissa = mantissa;
            MagnitudeBits = magnitudeBits;
            StepSize = stepSize;
            DynamicRange = dynamicRange;
            IsReversible = isReversible;
        }
    }
}
