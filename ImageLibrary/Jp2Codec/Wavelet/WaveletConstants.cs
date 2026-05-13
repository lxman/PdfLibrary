namespace Jp2Codec.Wavelet
{
    /// <summary>
    /// Lifting parameters for the 9/7 irreversible wavelet transform per
    /// ISO/IEC 15444-1 Table F.4. Single-precision approximations of the
    /// closed-form values are used since the IDWT operates on float[]
    /// coefficient grids (Tier-1 output is signed quantized indices that
    /// are converted to float by <see cref="Quantization.SubbandDequantizer.DequantizeIrreversible"/>).
    /// </summary>
    internal static class WaveletConstants
    {
        /// <summary>Predict-0 coefficient α = −g4/g3 ≈ −1.586134342.</summary>
        public const float Alpha = -1.586134342059924f;

        /// <summary>Update-0 coefficient β = g3/r1 ≈ −0.052980119.</summary>
        public const float Beta = -0.052980118572961f;

        /// <summary>Predict-1 coefficient γ = r1/s0 ≈ 0.882911076.</summary>
        public const float Gamma = 0.882911075530934f;

        /// <summary>Update-1 coefficient δ = s0/t0 ≈ 0.443506852.</summary>
        public const float Delta = 0.443506852043971f;

        /// <summary>Scaling factor K = 1/t0 ≈ 1.230174105.</summary>
        public const float K = 1.230174104914001f;

        /// <summary>Reciprocal scaling factor 1/K ≈ 0.812893066.</summary>
        public const float InvK = 0.812893066115961f;
    }
}
