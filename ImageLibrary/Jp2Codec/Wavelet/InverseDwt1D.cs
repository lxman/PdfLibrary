using System;

namespace Jp2Codec.Wavelet
{
    /// <summary>
    /// 1D inverse DWT orchestrator — the 1D_SR procedure from
    /// ISO/IEC 15444-1 F.3.6 minus the special length-1 handling (which is
    /// embedded in the lifting kernels themselves). Combines
    /// <see cref="InverseInterleave"/> with the appropriate lifting kernel
    /// for one row or one column of a tile-component.
    /// </summary>
    internal static class InverseDwt1D
    {
        /// <summary>
        /// Reversible 5/3 inverse: takes the low-pass and high-pass subband
        /// strips and reconstructs the parent integer signal.
        /// </summary>
        public static int[] Reverse53(int[] aL, int[] aH, int startingParity)
        {
            int[] y = InverseInterleave.Combine(aL, aH, startingParity);
            return InverseLifting53.Apply(y, startingParity);
        }

        /// <summary>
        /// Irreversible 9/7 inverse: takes the low-pass and high-pass
        /// subband strips and reconstructs the parent float signal.
        /// </summary>
        public static float[] Reverse97(float[] aL, float[] aH, int startingParity)
        {
            float[] y = InverseInterleave.Combine(aL, aH, startingParity);
            return InverseLifting97.Apply(y, startingParity);
        }
    }
}
