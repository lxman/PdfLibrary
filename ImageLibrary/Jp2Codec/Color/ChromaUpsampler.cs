using System;

namespace Jp2Codec.Color
{
    /// <summary>
    /// Per-component upsampling to the reference grid resolution, matching
    /// CSJ2K's <c>Resampler.GetInternCompData</c> by pixel replication —
    /// each subsampled input pixel is repeated <c>factor</c> times in each
    /// direction.
    ///
    /// <para>
    /// JPEG 2000 lets each component carry its own (X<sub>R</sub>, Y<sub>R</sub>)
    /// subsampling factors. For a 4:2:0 image (file3.jp2 in the conformance
    /// suite) luma is at full resolution while Cb/Cr are at half resolution
    /// in both axes; this helper inflates every component back to the image
    /// reference grid so downstream colour conversion can index by a single
    /// (x, y) without per-channel arithmetic.
    /// </para>
    ///
    /// <para>
    /// Pixel replication is what CSJ2K's Resampler does, so the output is
    /// bit-exact against CSJ2K's default decode path (after Resampler, before
    /// any colorspace mapping). A higher-quality bilinear / sinc upsampler
    /// would diverge from CSJ2K and is left as a follow-up.
    /// </para>
    /// </summary>
    public static class ChromaUpsampler
    {
        /// <summary>
        /// Returns a fresh per-component array of length <c>result.Width *
        /// result.Height</c> for every component, with subsampled channels
        /// pixel-replicated up to the full image resolution. Components that
        /// already match the image dimensions are returned unchanged (same
        /// reference).
        /// </summary>
        public static int[][] UpsampleAll(Jp2DecodeResult result)
        {
            if (result is null) throw new ArgumentNullException(nameof(result));

            int nc = result.NumberOfComponents;
            var output = new int[nc][];
            for (var c = 0; c < nc; c++)
            {
                int cw = result.ComponentWidth[c];
                int ch = result.ComponentHeight[c];
                if (cw == result.Width && ch == result.Height)
                {
                    output[c] = result.ComponentData[c];
                    continue;
                }
                output[c] = ReplicateComponent(
                    result.ComponentData[c], cw, ch,
                    result.Width, result.Height);
            }
            return output;
        }

        private static int[] ReplicateComponent(int[] src, int srcW, int srcH, int dstW, int dstH)
        {
            if (srcW == 0 || srcH == 0)
                return new int[dstW * dstH];

            // Compute the per-axis factor implied by the ceil-division the
            // encoder did when generating the component grid:
            //   srcW = ceil(dstW / factorX), similarly for Y.
            // Solve for factorX by inverting that — any factor that maps an
            // output index to a valid input index works.
            int factorX = (dstW + srcW - 1) / srcW;
            int factorY = (dstH + srcH - 1) / srcH;

            var dst = new int[dstW * dstH];
            for (var y = 0; y < dstH; y++)
            {
                int yIn = y / factorY;
                if (yIn >= srcH) yIn = srcH - 1;
                int srcRow = yIn * srcW;
                int dstRow = y * dstW;
                for (var x = 0; x < dstW; x++)
                {
                    int xIn = x / factorX;
                    if (xIn >= srcW) xIn = srcW - 1;
                    dst[dstRow + x] = src[srcRow + xIn];
                }
            }
            return dst;
        }
    }
}
