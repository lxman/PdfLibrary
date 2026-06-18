using System;

namespace ImageResampling;

/// <summary>
/// Pure pixel-buffer resampler for 8-bit/channel images (gray / RGB / RGBA).
/// Uses box-filter (area-average) downsampling, which minimises aliasing when
/// reducing resolution — suitable for DPI downsampling of PDF image XObjects.
/// No dependencies on any codec or PDF layer.
/// </summary>
public static class ImageResampler
{
    /// <summary>
    /// Resample a raw interleaved pixel buffer from (srcWidth × srcHeight) to
    /// (dstWidth × dstHeight).
    /// </summary>
    /// <param name="src">
    ///   Source pixels in interleaved row-major order:
    ///   pixel[y, x] starts at src[(y * srcWidth + x) * numberOfChannels].
    /// </param>
    /// <param name="srcWidth">Source image width in pixels (> 0).</param>
    /// <param name="srcHeight">Source image height in pixels (> 0).</param>
    /// <param name="numberOfChannels">Channels per pixel: 1 (gray), 3 (RGB), or 4 (RGBA).</param>
    /// <param name="dstWidth">Target width in pixels (> 0).</param>
    /// <param name="dstHeight">Target height in pixels (> 0).</param>
    /// <returns>Resampled pixel buffer with dstWidth * dstHeight * numberOfChannels bytes.</returns>
    public static byte[] Resample(
        byte[] src,
        int srcWidth, int srcHeight,
        int numberOfChannels,
        int dstWidth, int dstHeight)
    {
        if (src is null) throw new ArgumentNullException(nameof(src));
        if (dstWidth <= 0) throw new ArgumentOutOfRangeException(nameof(dstWidth), "Must be > 0.");
        if (dstHeight <= 0) throw new ArgumentOutOfRangeException(nameof(dstHeight), "Must be > 0.");
        if (numberOfChannels is not (1 or 2 or 3 or 4))
            throw new ArgumentOutOfRangeException(nameof(numberOfChannels), "Must be 1, 2, 3, or 4.");

        // Trivial no-op: dimensions unchanged.
        if (srcWidth == dstWidth && srcHeight == dstHeight)
        {
            var copy = new byte[src.Length];
            Buffer.BlockCopy(src, 0, copy, 0, src.Length);
            return copy;
        }

        var dst = new byte[dstWidth * dstHeight * numberOfChannels];

        // Box-filter (area-average) resampling.
        // For each destination pixel (dx, dy), compute the corresponding
        // fractional source rectangle and average all source pixels within it.
        double xScale = (double)srcWidth / dstWidth;
        double yScale = (double)srcHeight / dstHeight;

        for (var dy = 0; dy < dstHeight; dy++)
        {
            // Source row range for this destination row.
            double srcY0 = dy * yScale;
            double srcY1 = srcY0 + yScale;
            int iy0 = (int)srcY0;
            int iy1 = Math.Min((int)Math.Ceiling(srcY1), srcHeight);

            for (var dx = 0; dx < dstWidth; dx++)
            {
                // Source column range for this destination column.
                double srcX0 = dx * xScale;
                double srcX1 = srcX0 + xScale;
                int ix0 = (int)srcX0;
                int ix1 = Math.Min((int)Math.Ceiling(srcX1), srcWidth);

                // Accumulate weighted sum for each channel.
                Span<double> accum = stackalloc double[4]; // max 4 channels
                double totalWeight = 0.0;

                for (int sy = iy0; sy < iy1; sy++)
                {
                    // Fractional weight in y direction.
                    double wy = OverlapFraction(sy, sy + 1, srcY0, srcY1);

                    for (int sx = ix0; sx < ix1; sx++)
                    {
                        double wx = OverlapFraction(sx, sx + 1, srcX0, srcX1);
                        double w = wx * wy;

                        int srcIdx = (sy * srcWidth + sx) * numberOfChannels;
                        for (var c = 0; c < numberOfChannels; c++)
                            accum[c] += src[srcIdx + c] * w;

                        totalWeight += w;
                    }
                }

                int dstIdx = (dy * dstWidth + dx) * numberOfChannels;
                if (totalWeight > 0)
                {
                    for (var c = 0; c < numberOfChannels; c++)
                        dst[dstIdx + c] = ClampToByte(accum[c] / totalWeight);
                }
                else
                {
                    // Fallback: nearest-neighbour when weight is zero (shouldn't
                    // happen with valid inputs, but guard against it).
                    int nearY = Math.Min((int)Math.Round(srcY0), srcHeight - 1);
                    int nearX = Math.Min((int)Math.Round(srcX0), srcWidth - 1);
                    int nearIdx = (nearY * srcWidth + nearX) * numberOfChannels;
                    for (var c = 0; c < numberOfChannels; c++)
                        dst[dstIdx + c] = src[nearIdx + c];
                }
            }
        }

        return dst;
    }

    /// <summary>
    /// Convenience overload: resample using DPI values rather than explicit pixel counts.
    /// The target pixel dimensions are computed as:
    ///   dstWidth  = max(1, round(srcWidth  * targetDpi / sourceDpi))
    ///   dstHeight = max(1, round(srcHeight * targetDpi / sourceDpi))
    /// </summary>
    /// <param name="src">Source pixel buffer.</param>
    /// <param name="srcWidth">Source width in pixels.</param>
    /// <param name="srcHeight">Source height in pixels.</param>
    /// <param name="numberOfChannels">Channels per pixel (1, 2, 3, or 4).</param>
    /// <param name="sourceDpi">DPI of the source image (> 0).</param>
    /// <param name="targetDpi">Desired output DPI (> 0).</param>
    /// <returns>Resampled pixel buffer.</returns>
    public static byte[] ResampleByDpi(
        byte[] src,
        int srcWidth, int srcHeight,
        int numberOfChannels,
        double sourceDpi, double targetDpi)
    {
        if (sourceDpi <= 0) throw new ArgumentOutOfRangeException(nameof(sourceDpi), "Must be > 0.");
        if (targetDpi <= 0) throw new ArgumentOutOfRangeException(nameof(targetDpi), "Must be > 0.");

        double scale = targetDpi / sourceDpi;
        int dstWidth = Math.Max(1, (int)Math.Round(srcWidth * scale));
        int dstHeight = Math.Max(1, (int)Math.Round(srcHeight * scale));

        return Resample(src, srcWidth, srcHeight, numberOfChannels, dstWidth, dstHeight);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Fraction of the unit interval [pixelStart, pixelEnd) that overlaps
    /// with the continuous source range [srcStart, srcEnd).
    /// Both intervals are assumed to have length ≤ 2, so clamping is cheap.
    /// </summary>
    private static double OverlapFraction(double pixelStart, double pixelEnd, double srcStart, double srcEnd)
    {
        double lo = Math.Max(pixelStart, srcStart);
        double hi = Math.Min(pixelEnd, srcEnd);
        double overlap = hi - lo;
        return overlap > 0 ? overlap : 0.0;
    }

    private static byte ClampToByte(double value)
    {
        if (value <= 0) return 0;
        if (value >= 255) return 255;
        return (byte)(value + 0.5); // round-to-nearest
    }
}
