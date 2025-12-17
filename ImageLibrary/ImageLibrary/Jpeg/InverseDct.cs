using System;

namespace ImageLibrary.Jpeg;

/// <summary>
/// Performs Inverse Discrete Cosine Transform (IDCT) on 8x8 blocks.
/// This is Stage 5 of the decoder.
/// </summary>
internal static class InverseDct
{
    // Precomputed cosine values for IDCT
    // cos[(2*x + 1) * u * PI / 16] for x,u = 0..7
    private static readonly double[,] CosineTable = BuildCosineTable();

    // Scaling factors: C(0) = 1/sqrt(2), C(k) = 1 for k > 0
    private static readonly double[] ScaleFactors = BuildScaleFactors();

    private static double[,] BuildCosineTable()
    {
        var table = new double[8, 8];
        for (var x = 0; x < 8; x++)
        {
            for (var u = 0; u < 8; u++)
            {
                table[x, u] = Math.Cos((2 * x + 1) * u * Math.PI / 16.0);
            }
        }
        return table;
    }

    private static double[] BuildScaleFactors()
    {
        var factors = new double[8];
        factors[0] = 1.0 / Math.Sqrt(2);
        for (var i = 1; i < 8; i++)
        {
            factors[i] = 1.0;
        }
        return factors;
    }

    /// <summary>
    /// Performs IDCT on all blocks for all components.
    /// </summary>
    /// <param name="dequantized">Dequantized DCT coefficients [component][block][coefficient]</param>
    /// <returns>Pixel values [component][block][pixel] in range 0-255</returns>
    public static byte[][][] TransformAll(int[][][] dequantized)
    {
        var result = new byte[dequantized.Length][][];

        for (var c = 0; c < dequantized.Length; c++)
        {
            result[c] = new byte[dequantized[c].Length][];

            for (var b = 0; b < dequantized[c].Length; b++)
            {
                result[c][b] = Transform(dequantized[c][b]);
            }
        }

        return result;
    }

    /// <summary>
    /// Performs 2D IDCT on a single 8x8 block.
    /// Uses separable property: 2D IDCT = 1D IDCT on rows, then on columns.
    /// </summary>
    /// <param name="coefficients">64 dequantized DCT coefficients in row-major order</param>
    /// <returns>64 pixel values in range 0-255</returns>
    public static byte[] Transform(int[] coefficients)
    {
        // Intermediate results after row-wise IDCT
        var temp = new double[64];

        // Step 1: 1D IDCT on each row
        for (var y = 0; y < 8; y++)
        {
            int rowOffset = y * 8;

            for (var x = 0; x < 8; x++)
            {
                double sum = 0;

                for (var u = 0; u < 8; u++)
                {
                    sum += ScaleFactors[u] * coefficients[rowOffset + u] * CosineTable[x, u];
                }

                temp[rowOffset + x] = sum * 0.5; // 1/2 factor for 1D IDCT
            }
        }

        // Step 2: 1D IDCT on each column
        var result = new byte[64];

        for (var x = 0; x < 8; x++)
        {
            for (var y = 0; y < 8; y++)
            {
                double sum = 0;

                for (var v = 0; v < 8; v++)
                {
                    sum += ScaleFactors[v] * temp[v * 8 + x] * CosineTable[y, v];
                }

                // Apply final 1/2 factor and level shift (+128)
                double value = sum * 0.5 + 128.0;

                // Clamp to 0-255 range
                result[y * 8 + x] = (byte)Math.Clamp((int)Math.Round(value), 0, 255);
            }
        }

        return result;
    }

    /// <summary>
    /// Performs 2D IDCT using the reference (slow) implementation.
    /// Useful for verification against the optimized version.
    /// </summary>
    public static byte[] TransformReference(int[] coefficients)
    {
        var result = new byte[64];

        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                double sum = 0;

                for (var v = 0; v < 8; v++)
                {
                    for (var u = 0; u < 8; u++)
                    {
                        double cu = (u == 0) ? 1.0 / Math.Sqrt(2) : 1.0;
                        double cv = (v == 0) ? 1.0 / Math.Sqrt(2) : 1.0;

                        int coefIndex = v * 8 + u;
                        sum += cu * cv * coefficients[coefIndex]
                               * Math.Cos((2 * x + 1) * u * Math.PI / 16.0)
                               * Math.Cos((2 * y + 1) * v * Math.PI / 16.0);
                    }
                }

                // Apply 1/4 factor and level shift
                double value = sum * 0.25 + 128.0;
                result[y * 8 + x] = (byte)Math.Clamp((int)Math.Round(value), 0, 255);
            }
        }

        return result;
    }

    /// <summary>
    /// Transforms without level shift (returns signed values).
    /// Useful for testing intermediate results.
    /// </summary>
    public static int[] TransformNoLevelShift(int[] coefficients)
    {
        var temp = new double[64];

        // Step 1: 1D IDCT on each row
        for (var y = 0; y < 8; y++)
        {
            int rowOffset = y * 8;

            for (var x = 0; x < 8; x++)
            {
                double sum = 0;

                for (var u = 0; u < 8; u++)
                {
                    sum += ScaleFactors[u] * coefficients[rowOffset + u] * CosineTable[x, u];
                }

                temp[rowOffset + x] = sum * 0.5;
            }
        }

        // Step 2: 1D IDCT on each column
        var result = new int[64];

        for (var x = 0; x < 8; x++)
        {
            for (var y = 0; y < 8; y++)
            {
                double sum = 0;

                for (var v = 0; v < 8; v++)
                {
                    sum += ScaleFactors[v] * temp[v * 8 + x] * CosineTable[y, v];
                }

                result[y * 8 + x] = (int)Math.Round(sum * 0.5);
            }
        }

        return result;
    }
}
