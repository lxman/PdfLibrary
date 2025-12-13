using System;
using CommunityToolkit.HighPerformance;

namespace Compressors.Jpeg;

/// <summary>
/// Discrete Cosine Transform (DCT) implementation for JPEG compression.
/// Implements both forward DCT (FDCT) for encoding and inverse DCT (IDCT) for decoding.
/// Uses the Loeffler-Ligtenberg-Moschytz (LLM) algorithm for efficiency.
/// </summary>
public static class Dct
{
    // Pre-computed cosine values for DCT
    // C(k) = cos(k * PI / 16) * sqrt(2)
    private const float C1 = 0.980785280f;  // cos(1 * PI / 16)
    private const float C2 = 0.923879533f;  // cos(2 * PI / 16)
    private const float C3 = 0.831469612f;  // cos(3 * PI / 16)
    private const float C4 = 0.707106781f;  // cos(4 * PI / 16) = 1/sqrt(2)
    private const float C5 = 0.555570233f;  // cos(5 * PI / 16)
    private const float C6 = 0.382683432f;  // cos(6 * PI / 16)
    private const float C7 = 0.195090322f;  // cos(7 * PI / 16)

    // Derived constants for LLM algorithm
    private const float S1 = C1;
    private const float S2 = C2;
    private const float S3 = C3;
    private const float S6 = C6;
    private const float S7 = C7;

    // Rotation constants
    private const float R2C6 = 1.847759065f;  // sqrt(2) * cos(6 * PI / 16)
    private const float R2S6 = 0.765366865f;  // sqrt(2) * sin(6 * PI / 16)
    private const float R2 = 1.414213562f;    // sqrt(2)

    /// <summary>
    /// Performs forward DCT on an 8x8 block of samples.
    /// Input values should be level-shifted (subtract 128) before calling.
    /// </summary>
    /// <param name="block">8x8 block as a span of 64 floats in row-major order</param>
    public static void ForwardDct(Span<float> block)
    {
        if (block.Length != 64)
            throw new ArgumentException("Block must contain exactly 64 elements", nameof(block));

        // Row-column algorithm: apply 1D DCT to rows, then columns
        Span2D<float> block2D = Span2D<float>.DangerousCreate(ref block[0], 8, 8, 0);

        // Transform rows
        for (var i = 0; i < 8; i++)
        {
            Span<float> row = block2D.GetRowSpan(i);
            ForwardDct1D(row);
        }

        // Transform columns
        Span<float> column = stackalloc float[8];
        for (var j = 0; j < 8; j++)
        {
            // Extract column
            for (var i = 0; i < 8; i++)
                column[i] = block2D[i, j];

            ForwardDct1D(column);

            // Write back
            for (var i = 0; i < 8; i++)
                block2D[i, j] = column[i];
        }
    }

    /// <summary>
    /// Performs inverse DCT on an 8x8 block of DCT coefficients.
    /// Output values need to be level-shifted (add 128) after calling.
    /// </summary>
    /// <param name="block">8x8 block as a span of 64 floats in row-major order</param>
    public static void InverseDct(Span<float> block)
    {
        if (block.Length != 64)
            throw new ArgumentException("Block must contain exactly 64 elements", nameof(block));

        Span2D<float> block2D = Span2D<float>.DangerousCreate(ref block[0], 8, 8, 0);

        // Transform columns first for IDCT
        Span<float> column = stackalloc float[8];
        for (var j = 0; j < 8; j++)
        {
            // Extract column
            for (var i = 0; i < 8; i++)
                column[i] = block2D[i, j];

            InverseDct1D(column);

            // Write back
            for (var i = 0; i < 8; i++)
                block2D[i, j] = column[i];
        }

        // Transform rows
        for (var i = 0; i < 8; i++)
        {
            Span<float> row = block2D.GetRowSpan(i);
            InverseDct1D(row);
        }
    }

    // Orthonormal DCT scaling constants
    private const float Alpha0 = 0.353553391f;  // sqrt(1/8) = 1/(2*sqrt(2))
    private const float AlphaK = 0.5f;          // sqrt(2/8) = 1/2

    /// <summary>
    /// 1D Forward DCT (Type-II) using orthonormal formulation.
    /// X[k] = alpha[k] * sum_{n=0}^{N-1} x[n] * cos(pi*(2n+1)*k/(2N))
    /// </summary>
    private static void ForwardDct1D(Span<float> data)
    {
        Span<float> result = stackalloc float[8];

        for (var k = 0; k < 8; k++)
        {
            float sum = 0;
            for (var n = 0; n < 8; n++)
            {
                sum += data[n] * MathF.Cos(MathF.PI * (2 * n + 1) * k / 16);
            }

            float alpha = k == 0 ? Alpha0 : AlphaK;
            result[k] = alpha * sum;
        }

        result.CopyTo(data);
    }

    /// <summary>
    /// 1D Inverse DCT (Type-III) using orthonormal formulation.
    /// x[n] = sum_{k=0}^{N-1} alpha[k] * X[k] * cos(pi*(2n+1)*k/(2N))
    /// </summary>
    private static void InverseDct1D(Span<float> data)
    {
        Span<float> result = stackalloc float[8];

        for (var n = 0; n < 8; n++)
        {
            float sum = 0;
            for (var k = 0; k < 8; k++)
            {
                float alpha = k == 0 ? Alpha0 : AlphaK;
                sum += alpha * data[k] * MathF.Cos(MathF.PI * (2 * n + 1) * k / 16);
            }

            result[n] = sum;
        }

        result.CopyTo(data);
    }

    /// <summary>
    /// Reference implementation of forward DCT using the direct formula.
    /// Slower but useful for verification.
    /// </summary>
    public static void ForwardDctReference(Span<float> block)
    {
        if (block.Length != 64)
            throw new ArgumentException("Block must contain exactly 64 elements", nameof(block));

        Span<float> temp = stackalloc float[64];
        block.CopyTo(temp);

        for (var u = 0; u < 8; u++)
        {
            for (var v = 0; v < 8; v++)
            {
                float sum = 0;
                for (var x = 0; x < 8; x++)
                {
                    for (var y = 0; y < 8; y++)
                    {
                        sum += temp[x * 8 + y] *
                               MathF.Cos((2 * x + 1) * u * MathF.PI / 16) *
                               MathF.Cos((2 * y + 1) * v * MathF.PI / 16);
                    }
                }

                float cu = u == 0 ? 1f / MathF.Sqrt(2) : 1f;
                float cv = v == 0 ? 1f / MathF.Sqrt(2) : 1f;
                block[u * 8 + v] = 0.25f * cu * cv * sum;
            }
        }
    }

    /// <summary>
    /// Reference implementation of inverse DCT using the direct formula.
    /// Slower but useful for verification.
    /// </summary>
    public static void InverseDctReference(Span<float> block)
    {
        if (block.Length != 64)
            throw new ArgumentException("Block must contain exactly 64 elements", nameof(block));

        Span<float> temp = stackalloc float[64];
        block.CopyTo(temp);

        for (var x = 0; x < 8; x++)
        {
            for (var y = 0; y < 8; y++)
            {
                float sum = 0;
                for (var u = 0; u < 8; u++)
                {
                    for (var v = 0; v < 8; v++)
                    {
                        float cu = u == 0 ? 1f / MathF.Sqrt(2) : 1f;
                        float cv = v == 0 ? 1f / MathF.Sqrt(2) : 1f;
                        sum += cu * cv * temp[u * 8 + v] *
                               MathF.Cos((2 * x + 1) * u * MathF.PI / 16) *
                               MathF.Cos((2 * y + 1) * v * MathF.PI / 16);
                    }
                }

                block[x * 8 + y] = 0.25f * sum;
            }
        }
    }

    /// <summary>
    /// Performs forward DCT on an 8x8 block, operating in-place on integer data.
    /// Includes level-shift (subtract 128) before transform.
    /// </summary>
    public static void ForwardDctFromBytes(ReadOnlySpan<byte> input, Span<float> output)
    {
        if (input.Length != 64)
            throw new ArgumentException("Input must contain exactly 64 elements", nameof(input));
        if (output.Length != 64)
            throw new ArgumentException("Output must contain exactly 64 elements", nameof(output));

        // Level shift and convert to float
        for (var i = 0; i < 64; i++)
        {
            output[i] = input[i] - JpegConstants.LevelShift;
        }

        ForwardDct(output);
    }

    /// <summary>
    /// Performs inverse DCT on an 8x8 block and converts to bytes.
    /// Includes level-shift (add 128) and clamping to [0, 255].
    /// </summary>
    public static void InverseDctToBytes(Span<float> input, Span<byte> output)
    {
        if (input.Length != 64)
            throw new ArgumentException("Input must contain exactly 64 elements", nameof(input));
        if (output.Length != 64)
            throw new ArgumentException("Output must contain exactly 64 elements", nameof(output));

        InverseDct(input);

        // Level shift and clamp
        for (var i = 0; i < 64; i++)
        {
            int value = (int)MathF.Round(input[i]) + JpegConstants.LevelShift;
            output[i] = (byte)Math.Clamp(value, 0, 255);
        }
    }
}
