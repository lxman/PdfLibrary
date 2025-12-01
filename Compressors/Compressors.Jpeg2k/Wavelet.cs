using System;

namespace Compressors.Jpeg2k;

/// <summary>
/// Discrete Wavelet Transform (DWT) for JPEG2000.
/// Implements both CDF 9/7 (lossy) and CDF 5/3 (lossless) wavelets
/// using the lifting scheme.
/// </summary>
public static class Wavelet
{
    /// <summary>
    /// Performs forward 2D DWT on image data.
    /// </summary>
    /// <param name="data">Image data (modified in place)</param>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <param name="levels">Number of decomposition levels</param>
    /// <param name="lossy">True for CDF 9/7, false for CDF 5/3</param>
    public static void Forward2D(Span<float> data, int width, int height, int levels, bool lossy = true)
    {
        int currentWidth = width;
        int currentHeight = height;

        for (int level = 0; level < levels; level++)
        {
            // Transform rows
            for (int y = 0; y < currentHeight; y++)
            {
                var row = data.Slice(y * width, currentWidth);
                if (lossy)
                    Forward1D_97(row);
                else
                    Forward1D_53(row);
            }

            // Transform columns
            Span<float> column = stackalloc float[currentHeight];
            for (int x = 0; x < currentWidth; x++)
            {
                // Extract column
                for (int y = 0; y < currentHeight; y++)
                    column[y] = data[y * width + x];

                if (lossy)
                    Forward1D_97(column);
                else
                    Forward1D_53(column);

                // Write back
                for (int y = 0; y < currentHeight; y++)
                    data[y * width + x] = column[y];
            }

            // Next level operates on LL subband
            currentWidth = (currentWidth + 1) / 2;
            currentHeight = (currentHeight + 1) / 2;
        }
    }

    /// <summary>
    /// Performs inverse 2D DWT on wavelet coefficients.
    /// </summary>
    public static void Inverse2D(Span<float> data, int width, int height, int levels, bool lossy = true)
    {
        // Calculate subband sizes for each level
        Span<int> widths = stackalloc int[levels + 1];
        Span<int> heights = stackalloc int[levels + 1];

        widths[levels] = width;
        heights[levels] = height;

        for (int i = levels - 1; i >= 0; i--)
        {
            widths[i] = (widths[i + 1] + 1) / 2;
            heights[i] = (heights[i + 1] + 1) / 2;
        }

        // Inverse from deepest level to full resolution
        for (int level = levels - 1; level >= 0; level--)
        {
            int currentWidth = widths[level + 1];
            int currentHeight = heights[level + 1];

            // Inverse transform columns
            Span<float> column = stackalloc float[currentHeight];
            for (int x = 0; x < currentWidth; x++)
            {
                // Extract column
                for (int y = 0; y < currentHeight; y++)
                    column[y] = data[y * width + x];

                if (lossy)
                    Inverse1D_97(column);
                else
                    Inverse1D_53(column);

                // Write back
                for (int y = 0; y < currentHeight; y++)
                    data[y * width + x] = column[y];
            }

            // Inverse transform rows
            for (int y = 0; y < currentHeight; y++)
            {
                var row = data.Slice(y * width, currentWidth);
                if (lossy)
                    Inverse1D_97(row);
                else
                    Inverse1D_53(row);
            }
        }
    }

    /// <summary>
    /// Forward 1D CDF 9/7 wavelet transform using lifting.
    /// Output: [L0, L1, ..., H0, H1, ...] (low-pass followed by high-pass)
    /// </summary>
    private static void Forward1D_97(Span<float> data)
    {
        int n = data.Length;
        if (n < 2) return;

        int half = (n + 1) / 2;  // Number of low-pass coefficients

        // Temporary storage for interleaved processing
        Span<float> temp = stackalloc float[n];
        data.CopyTo(temp);

        // Step 1: Predict (alpha)
        for (int i = 1; i < n - 1; i += 2)
        {
            temp[i] += Jp2kConstants.Cdf97.Alpha * (temp[i - 1] + temp[i + 1]);
        }
        if (n % 2 == 0)
        {
            temp[n - 1] += 2 * Jp2kConstants.Cdf97.Alpha * temp[n - 2];
        }

        // Step 2: Update (beta)
        temp[0] += 2 * Jp2kConstants.Cdf97.Beta * temp[1];
        for (int i = 2; i < n - 1; i += 2)
        {
            temp[i] += Jp2kConstants.Cdf97.Beta * (temp[i - 1] + temp[i + 1]);
        }
        if (n % 2 != 0 && n > 1)
        {
            temp[n - 1] += 2 * Jp2kConstants.Cdf97.Beta * temp[n - 2];
        }

        // Step 3: Predict (gamma)
        for (int i = 1; i < n - 1; i += 2)
        {
            temp[i] += Jp2kConstants.Cdf97.Gamma * (temp[i - 1] + temp[i + 1]);
        }
        if (n % 2 == 0)
        {
            temp[n - 1] += 2 * Jp2kConstants.Cdf97.Gamma * temp[n - 2];
        }

        // Step 4: Update (delta)
        temp[0] += 2 * Jp2kConstants.Cdf97.Delta * temp[1];
        for (int i = 2; i < n - 1; i += 2)
        {
            temp[i] += Jp2kConstants.Cdf97.Delta * (temp[i - 1] + temp[i + 1]);
        }
        if (n % 2 != 0 && n > 1)
        {
            temp[n - 1] += 2 * Jp2kConstants.Cdf97.Delta * temp[n - 2];
        }

        // Step 5: Scale and deinterleave
        for (int i = 0; i < half; i++)
        {
            data[i] = temp[2 * i] * Jp2kConstants.Cdf97.InvK;  // Low-pass
        }
        for (int i = 0; i < n - half; i++)
        {
            data[half + i] = temp[2 * i + 1] * Jp2kConstants.Cdf97.K;  // High-pass
        }
    }

    /// <summary>
    /// Inverse 1D CDF 9/7 wavelet transform using lifting.
    /// Input: [L0, L1, ..., H0, H1, ...] (low-pass followed by high-pass)
    /// </summary>
    private static void Inverse1D_97(Span<float> data)
    {
        int n = data.Length;
        if (n < 2) return;

        int half = (n + 1) / 2;

        // Temporary storage
        Span<float> temp = stackalloc float[n];

        // Step 1: Interleave and unscale
        for (int i = 0; i < half; i++)
        {
            temp[2 * i] = data[i] * Jp2kConstants.Cdf97.K;  // Low-pass
        }
        for (int i = 0; i < n - half; i++)
        {
            temp[2 * i + 1] = data[half + i] * Jp2kConstants.Cdf97.InvK;  // High-pass
        }

        // Step 2: Undo update (delta)
        temp[0] -= 2 * Jp2kConstants.Cdf97.Delta * temp[1];
        for (int i = 2; i < n - 1; i += 2)
        {
            temp[i] -= Jp2kConstants.Cdf97.Delta * (temp[i - 1] + temp[i + 1]);
        }
        if (n % 2 != 0 && n > 1)
        {
            temp[n - 1] -= 2 * Jp2kConstants.Cdf97.Delta * temp[n - 2];
        }

        // Step 3: Undo predict (gamma)
        for (int i = 1; i < n - 1; i += 2)
        {
            temp[i] -= Jp2kConstants.Cdf97.Gamma * (temp[i - 1] + temp[i + 1]);
        }
        if (n % 2 == 0)
        {
            temp[n - 1] -= 2 * Jp2kConstants.Cdf97.Gamma * temp[n - 2];
        }

        // Step 4: Undo update (beta)
        temp[0] -= 2 * Jp2kConstants.Cdf97.Beta * temp[1];
        for (int i = 2; i < n - 1; i += 2)
        {
            temp[i] -= Jp2kConstants.Cdf97.Beta * (temp[i - 1] + temp[i + 1]);
        }
        if (n % 2 != 0 && n > 1)
        {
            temp[n - 1] -= 2 * Jp2kConstants.Cdf97.Beta * temp[n - 2];
        }

        // Step 5: Undo predict (alpha)
        for (int i = 1; i < n - 1; i += 2)
        {
            temp[i] -= Jp2kConstants.Cdf97.Alpha * (temp[i - 1] + temp[i + 1]);
        }
        if (n % 2 == 0)
        {
            temp[n - 1] -= 2 * Jp2kConstants.Cdf97.Alpha * temp[n - 2];
        }

        temp.CopyTo(data);
    }

    /// <summary>
    /// Forward 1D CDF 5/3 wavelet transform (lossless).
    /// Uses integer lifting for reversibility.
    /// </summary>
    private static void Forward1D_53(Span<float> data)
    {
        int n = data.Length;
        if (n < 2) return;

        int half = (n + 1) / 2;
        Span<float> temp = stackalloc float[n];
        data.CopyTo(temp);

        // Step 1: Predict (high-pass = odd - avg(even neighbors))
        for (int i = 1; i < n - 1; i += 2)
        {
            temp[i] -= MathF.Floor((temp[i - 1] + temp[i + 1]) / 2);
        }
        if (n % 2 == 0)
        {
            temp[n - 1] -= temp[n - 2];
        }

        // Step 2: Update (low-pass = even + avg(high neighbors))
        temp[0] += MathF.Floor((temp[1] + 1) / 2);
        for (int i = 2; i < n - 1; i += 2)
        {
            temp[i] += MathF.Floor((temp[i - 1] + temp[i + 1] + 2) / 4);
        }
        if (n % 2 != 0 && n > 1)
        {
            temp[n - 1] += MathF.Floor((temp[n - 2] + 1) / 2);
        }

        // Deinterleave
        for (int i = 0; i < half; i++)
        {
            data[i] = temp[2 * i];
        }
        for (int i = 0; i < n - half; i++)
        {
            data[half + i] = temp[2 * i + 1];
        }
    }

    /// <summary>
    /// Inverse 1D CDF 5/3 wavelet transform (lossless).
    /// </summary>
    private static void Inverse1D_53(Span<float> data)
    {
        int n = data.Length;
        if (n < 2) return;

        int half = (n + 1) / 2;
        Span<float> temp = stackalloc float[n];

        // Interleave
        for (int i = 0; i < half; i++)
        {
            temp[2 * i] = data[i];
        }
        for (int i = 0; i < n - half; i++)
        {
            temp[2 * i + 1] = data[half + i];
        }

        // Step 1: Undo update
        temp[0] -= MathF.Floor((temp[1] + 1) / 2);
        for (int i = 2; i < n - 1; i += 2)
        {
            temp[i] -= MathF.Floor((temp[i - 1] + temp[i + 1] + 2) / 4);
        }
        if (n % 2 != 0 && n > 1)
        {
            temp[n - 1] -= MathF.Floor((temp[n - 2] + 1) / 2);
        }

        // Step 2: Undo predict
        for (int i = 1; i < n - 1; i += 2)
        {
            temp[i] += MathF.Floor((temp[i - 1] + temp[i + 1]) / 2);
        }
        if (n % 2 == 0)
        {
            temp[n - 1] += temp[n - 2];
        }

        temp.CopyTo(data);
    }

    /// <summary>
    /// Gets the dimensions of a subband at a given level.
    /// </summary>
    /// <param name="imageWidth">Original image width</param>
    /// <param name="imageHeight">Original image height</param>
    /// <param name="level">Decomposition level (0 = highest resolution)</param>
    /// <param name="subband">Subband type (LL, HL, LH, HH)</param>
    /// <returns>Subband dimensions (width, height)</returns>
    public static (int Width, int Height) GetSubbandSize(int imageWidth, int imageHeight, int level, int subband)
    {
        // Calculate dimensions at this level
        int levelWidth = imageWidth;
        int levelHeight = imageHeight;

        for (int i = 0; i <= level; i++)
        {
            levelWidth = (levelWidth + 1) / 2;
            levelHeight = (levelHeight + 1) / 2;
        }

        if (subband == Jp2kConstants.SubbandLL)
        {
            return (levelWidth, levelHeight);
        }

        // For detail subbands at level L, dimensions are based on level L-1 split
        int prevWidth = imageWidth;
        int prevHeight = imageHeight;
        for (int i = 0; i < level; i++)
        {
            prevWidth = (prevWidth + 1) / 2;
            prevHeight = (prevHeight + 1) / 2;
        }

        int lowWidth = (prevWidth + 1) / 2;
        int highWidth = prevWidth / 2;
        int lowHeight = (prevHeight + 1) / 2;
        int highHeight = prevHeight / 2;

        return subband switch
        {
            Jp2kConstants.SubbandHL => (highWidth, lowHeight),
            Jp2kConstants.SubbandLH => (lowWidth, highHeight),
            Jp2kConstants.SubbandHH => (highWidth, highHeight),
            _ => (levelWidth, levelHeight)
        };
    }

    /// <summary>
    /// Gets the offset of a subband in the coefficient array.
    /// </summary>
    public static int GetSubbandOffset(int width, int level, int subband)
    {
        int levelWidth = width;
        for (int i = 0; i < level; i++)
        {
            levelWidth = (levelWidth + 1) / 2;
        }

        int lowWidth = (levelWidth + 1) / 2;

        return subband switch
        {
            Jp2kConstants.SubbandLL => 0,
            Jp2kConstants.SubbandHL => lowWidth,
            Jp2kConstants.SubbandLH => 0,  // But different row offset
            Jp2kConstants.SubbandHH => lowWidth,
            _ => 0
        };
    }
}
