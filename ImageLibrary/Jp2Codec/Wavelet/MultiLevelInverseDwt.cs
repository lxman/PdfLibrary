using System;

namespace Jp2Codec.Wavelet
{
    /// <summary>
    /// Multi-level inverse DWT — the IDWT procedure from ISO/IEC 15444-1
    /// F.3.1. Iterates from the deepest decomposition level (N_L) down to
    /// level 1, applying <see cref="InverseDwt2D"/> at each step.
    ///
    /// <para>
    /// Inputs: the LL subband at level N_L (the coarsest approximation)
    /// plus an array of <see cref="WaveletLevel53"/> / <see cref="WaveletLevel97"/>
    /// describing levels 1..N_L. The level-array is indexed from 1 (index 0)
    /// to N_L (index <c>levels.Length - 1</c>) and traversed in reverse so
    /// the outermost reconstruction step uses the LL_{N_L} input.
    /// </para>
    ///
    /// <para>
    /// A zero-level decomposition is valid and corresponds to N_L = 0:
    /// the input LL <i>is</i> the reconstructed tile-component.
    /// </para>
    /// </summary>
    internal static class MultiLevelInverseDwt
    {
        public static int[,] Reverse53(int[,] llDeepest, WaveletLevel53[] levels)
        {
            if (llDeepest is null) throw new ArgumentNullException(nameof(llDeepest));
            if (levels is null) throw new ArgumentNullException(nameof(levels));

            int[,] currentLL = llDeepest;
            for (int i = levels.Length - 1; i >= 0; i--)
            {
                WaveletLevel53 lev = levels[i];
                currentLL = InverseDwt2D.Reverse53(
                    currentLL, lev.HL, lev.LH, lev.HH,
                    lev.U0ParityAtParent, lev.V0ParityAtParent);
            }
            return currentLL;
        }

        public static float[,] Reverse97(float[,] llDeepest, WaveletLevel97[] levels)
        {
            if (llDeepest is null) throw new ArgumentNullException(nameof(llDeepest));
            if (levels is null) throw new ArgumentNullException(nameof(levels));

            float[,] currentLL = llDeepest;
            for (int i = levels.Length - 1; i >= 0; i--)
            {
                WaveletLevel97 lev = levels[i];
                currentLL = InverseDwt2D.Reverse97(
                    currentLL, lev.HL, lev.LH, lev.HH,
                    lev.U0ParityAtParent, lev.V0ParityAtParent);
            }
            return currentLL;
        }
    }
}
