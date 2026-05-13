using System;

namespace Jp2Codec.Quantization
{
    /// <summary>
    /// Inverse quantization per ISO/IEC 15444-1 Annex E. Consumes a Tier-1
    /// signed quantized-index grid (q_b in E-1) and produces the reconstructed
    /// transform coefficient grid R_q_b that feeds the inverse DWT (Annex F).
    ///
    /// <para>
    /// The reversible path returns int (the 5/3 IDWT runs on integers). The
    /// irreversible path returns float (the 9/7 IDWT runs on floats).
    /// </para>
    ///
    /// <para>
    /// <c>missingBitPlanes</c> is M_b − N_b — the number of bit-planes that
    /// were truncated before reaching bit-plane zero. It is supplied as a
    /// per-block uniform value because the orchestrator decodes a whole block
    /// to the same truncation point. For fully-decoded code-blocks this is
    /// zero and reconstruction reduces to identity (reversible) or
    /// q · Delta_b (irreversible with r=0).
    /// </para>
    /// </summary>
    internal static class SubbandDequantizer
    {
        /// <summary>
        /// Default reconstruction parameter r (E-6 / E-8). The spec allows any
        /// value in [0, 1); 0.5 is the common mid-point bias.
        /// </summary>
        public const double DefaultReconstructionParameter = 0.5;

        /// <summary>
        /// Inverse quantization for the reversible (5/3) transform per E.1.2.
        /// For coefficients with no missing bit-planes the value passes through
        /// unchanged (E-7). For truncated coefficients an integer mid-point
        /// bias is applied (E-8 with r=0.5 rounded into integer arithmetic):
        /// for missingBitPlanes ≥ 1 the bias magnitude is 2^(missingBitPlanes − 1).
        /// </summary>
        public static int[,] DequantizeReversible(int[,] quantizedIndices, int missingBitPlanes)
        {
            if (quantizedIndices is null) throw new ArgumentNullException(nameof(quantizedIndices));
            if (missingBitPlanes < 0)
                throw new ArgumentOutOfRangeException(nameof(missingBitPlanes), missingBitPlanes, null);

            int height = quantizedIndices.GetLength(0);
            int width = quantizedIndices.GetLength(1);
            var output = new int[height, width];

            if (missingBitPlanes == 0)
            {
                // E-7: R_q_b = q_b. Plain copy preserves signed indices verbatim.
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        output[y, x] = quantizedIndices[y, x];
                    }
                }
                return output;
            }

            // Integer mid-point bias 2^(missingBitPlanes - 1). Pushed away from
            // zero per the sign-aware form of E-8.
            int bias = 1 << (missingBitPlanes - 1);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    int q = quantizedIndices[y, x];
                    if (q > 0)
                        output[y, x] = q + bias;
                    else if (q < 0)
                        output[y, x] = q - bias;
                    // q == 0 stays 0.
                }
            }
            return output;
        }

        /// <summary>
        /// Inverse quantization for the irreversible (9/7) transform per E.1.1.
        /// Applies E-6: R_q_b(u,v) = (q_b ± r·2^(M_b−N_b)) · Delta_b, where the
        /// sign of the bias matches the sign of q_b (push away from zero).
        /// Zero coefficients map to zero unchanged.
        /// </summary>
        /// <param name="quantizedIndices">Tier-1 output (signed indices).</param>
        /// <param name="stepSize">Delta_b for this subband (E-3).</param>
        /// <param name="missingBitPlanes">M_b − N_b for this code-block.</param>
        /// <param name="reconstructionParameter">r in [0, 1); defaults to 1/2.</param>
        public static float[,] DequantizeIrreversible(
            int[,] quantizedIndices,
            double stepSize,
            int missingBitPlanes,
            double reconstructionParameter = DefaultReconstructionParameter)
        {
            if (quantizedIndices is null) throw new ArgumentNullException(nameof(quantizedIndices));
            if (missingBitPlanes < 0)
                throw new ArgumentOutOfRangeException(nameof(missingBitPlanes), missingBitPlanes, null);
            if (reconstructionParameter < 0.0 || reconstructionParameter >= 1.0)
                throw new ArgumentOutOfRangeException(
                    nameof(reconstructionParameter), reconstructionParameter,
                    "Reconstruction parameter r must lie in [0, 1).");

            int height = quantizedIndices.GetLength(0);
            int width = quantizedIndices.GetLength(1);
            var output = new float[height, width];

            // 2^(M_b - N_b) is exact for any reasonable bit-plane count; build it once.
            double biasScale = missingBitPlanes <= 0
                ? 1.0
                : unchecked((double)(1L << missingBitPlanes));
            double bias = reconstructionParameter * biasScale;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    int q = quantizedIndices[y, x];
                    if (q == 0)
                    {
                        output[y, x] = 0f;
                        continue;
                    }
                    double signedBias = q > 0 ? bias : -bias;
                    output[y, x] = (float)((q + signedBias) * stepSize);
                }
            }
            return output;
        }
    }
}
