using System;

namespace Compressors.Jpeg2k;

/// <summary>
/// Represents a subband in the wavelet decomposition.
/// </summary>
public class Subband
{
    /// <summary>
    /// Subband type (LL, HL, LH, HH).
    /// </summary>
    public int Type { get; }

    /// <summary>
    /// Decomposition level (0 = deepest/lowest resolution).
    /// </summary>
    public int Level { get; }

    /// <summary>
    /// Subband width in samples.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Subband height in samples.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Offset X in the coefficient array.
    /// </summary>
    public int OffsetX { get; }

    /// <summary>
    /// Offset Y in the coefficient array.
    /// </summary>
    public int OffsetY { get; }

    /// <summary>
    /// Quantization step size.
    /// </summary>
    public float QuantStep { get; set; }

    /// <summary>
    /// Component index (0-based).
    /// </summary>
    public int ComponentIndex { get; set; }

    /// <summary>
    /// Code-blocks in this subband.
    /// </summary>
    public CodeBlock[,]? CodeBlocks { get; private set; }

    public Subband(int type, int level, int width, int height, int offsetX, int offsetY)
    {
        Type = type;
        Level = level;
        Width = width;
        Height = height;
        OffsetX = offsetX;
        OffsetY = offsetY;
        QuantStep = 1.0f;
    }

    /// <summary>
    /// Creates code-blocks for this subband.
    /// </summary>
    public void CreateCodeBlocks(int codeBlockWidth, int codeBlockHeight)
    {
        int numBlocksX = (Width + codeBlockWidth - 1) / codeBlockWidth;
        int numBlocksY = (Height + codeBlockHeight - 1) / codeBlockHeight;

        CodeBlocks = new CodeBlock[numBlocksY, numBlocksX];

        for (int by = 0; by < numBlocksY; by++)
        {
            for (int bx = 0; bx < numBlocksX; bx++)
            {
                int x0 = bx * codeBlockWidth;
                int y0 = by * codeBlockHeight;
                int x1 = Math.Min(x0 + codeBlockWidth, Width);
                int y1 = Math.Min(y0 + codeBlockHeight, Height);

                CodeBlocks[by, bx] = new CodeBlock(
                    OffsetX + x0,
                    OffsetY + y0,
                    x1 - x0,
                    y1 - y0,
                    this);
            }
        }
    }

    /// <summary>
    /// Gets the context label base for this subband type.
    /// </summary>
    public int GetContextBase()
    {
        return Type switch
        {
            Jp2kConstants.SubbandHL => Jp2kConstants.Contexts.SigPropHL,
            Jp2kConstants.SubbandHH => Jp2kConstants.Contexts.SigPropHH,
            _ => Jp2kConstants.Contexts.SigPropLL_LH
        };
    }
}

/// <summary>
/// Represents a code-block within a subband.
/// Code-blocks are independently coded units in EBCOT.
/// </summary>
public class CodeBlock
{
    /// <summary>
    /// X offset in the full coefficient array.
    /// </summary>
    public int X { get; }

    /// <summary>
    /// Y offset in the full coefficient array.
    /// </summary>
    public int Y { get; }

    /// <summary>
    /// Block width.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Block height.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Parent subband.
    /// </summary>
    public Subband Subband { get; }

    /// <summary>
    /// Quantized coefficients (magnitude, stored as positive integers).
    /// </summary>
    public int[,]? Coefficients { get; set; }

    /// <summary>
    /// Sign array (true = negative).
    /// </summary>
    public bool[,]? Signs { get; set; }

    /// <summary>
    /// Significance state (true = coefficient has become significant).
    /// </summary>
    public bool[,]? Significance { get; set; }

    /// <summary>
    /// Number of magnitude bit-planes.
    /// </summary>
    public int NumBitPlanes { get; set; }

    /// <summary>
    /// Encoded data for this code-block.
    /// </summary>
    public byte[]? EncodedData { get; set; }

    /// <summary>
    /// Number of coding passes included.
    /// </summary>
    public int NumPasses { get; set; }

    /// <summary>
    /// Lengths of each coding pass (for rate allocation).
    /// </summary>
    public int[]? PassLengths { get; set; }

    public CodeBlock(int x, int y, int width, int height, Subband subband)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Subband = subband;
    }

    /// <summary>
    /// Initializes the coefficient arrays.
    /// </summary>
    public void Initialize()
    {
        Coefficients = new int[Height, Width];
        Signs = new bool[Height, Width];
        Significance = new bool[Height, Width];
    }

    /// <summary>
    /// Calculates the number of bit-planes needed.
    /// </summary>
    public void CalculateBitPlanes()
    {
        if (Coefficients == null) return;

        int maxMag = 0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                maxMag = Math.Max(maxMag, Coefficients[y, x]);
            }
        }

        NumBitPlanes = maxMag > 0 ? BitCount(maxMag) : 0;
    }

    private static int BitCount(int value)
    {
        int bits = 0;
        while (value > 0)
        {
            bits++;
            value >>= 1;
        }
        return bits;
    }
}

/// <summary>
/// Handles quantization for JPEG2000.
/// </summary>
public static class Jp2kQuantization
{
    /// <summary>
    /// Calculates quantization step sizes for all subbands.
    /// </summary>
    /// <param name="baseStep">Base quantization step (derived from quality)</param>
    /// <param name="numLevels">Number of DWT levels</param>
    /// <param name="lossy">True for lossy (9/7), false for lossless (5/3)</param>
    /// <returns>Step sizes indexed by [level, subband]</returns>
    public static float[,] CalculateStepSizes(float baseStep, int numLevels, bool lossy)
    {
        // +1 for LL at deepest level
        var steps = new float[numLevels + 1, 4];

        if (!lossy)
        {
            // Lossless - all steps are 1.0
            for (int level = 0; level <= numLevels; level++)
            {
                for (int band = 0; band < 4; band++)
                {
                    steps[level, band] = 1.0f;
                }
            }
            return steps;
        }

        // For lossy, scale step size based on subband energy
        // Lower frequency subbands get smaller steps (finer quantization)
        for (int level = 0; level < numLevels; level++)
        {
            // Energy weighting based on level and subband type
            float levelScale = MathF.Pow(2, level);

            steps[level, Jp2kConstants.SubbandHL] = baseStep * levelScale;
            steps[level, Jp2kConstants.SubbandLH] = baseStep * levelScale;
            steps[level, Jp2kConstants.SubbandHH] = baseStep * levelScale * 1.414f;
        }

        // LL subband at deepest level
        steps[numLevels, Jp2kConstants.SubbandLL] = baseStep * MathF.Pow(2, numLevels - 1) * 0.5f;

        return steps;
    }

    /// <summary>
    /// Quantizes a coefficient value.
    /// </summary>
    public static int Quantize(float value, float stepSize)
    {
        if (stepSize <= 0) return 0;
        return (int)MathF.Floor(MathF.Abs(value) / stepSize);
    }

    /// <summary>
    /// Dequantizes a coefficient value.
    /// </summary>
    public static float Dequantize(int magnitude, bool negative, float stepSize)
    {
        float value = (magnitude + 0.5f) * stepSize;  // Mid-point reconstruction
        return negative ? -value : value;
    }

    /// <summary>
    /// Converts a quality value (1-100) to a base quantization step.
    /// </summary>
    public static float QualityToStep(int quality)
    {
        quality = Math.Clamp(quality, 1, 100);

        // Similar to JPEG quality scaling
        if (quality < 50)
            return (50.0f / quality) * 0.1f;
        else
            return ((100 - quality) / 50.0f) * 0.1f + 0.001f;
    }
}
