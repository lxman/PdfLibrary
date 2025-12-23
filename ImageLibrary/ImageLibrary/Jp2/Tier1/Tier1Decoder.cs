using System;
using System.Collections.Generic;
using ImageLibrary.Jp2.Pipeline;

namespace ImageLibrary.Jp2.Tier1;

/// <summary>
/// Tier-1 decoder: decodes all code-blocks and assembles quantized subbands.
/// </summary>
internal class Tier1Decoder
{
    private readonly Jp2Codestream _codestream;
    private readonly EbcotDecoder _ebcot;

    public Tier1Decoder(Jp2Codestream codestream)
    {
        _codestream = codestream;
        _ebcot = new EbcotDecoder(codestream.CodingParameters.CodeBlockFlags);
    }

    /// <summary>
    /// Decodes all code-blocks from Tier-2 output and assembles quantized subbands.
    /// </summary>
    public QuantizedSubband[] DecodeToSubbands(Tier2Output tier2Output)
    {
        var subbands = new List<QuantizedSubband>();

        int numResolutions = tier2Output.ResolutionLevels;

        // Calculate subband dimensions based on tile size
        int tileIdx = tier2Output.TileIndex;
        int numTilesX = _codestream.Frame.NumTilesX;
        int tileX = tileIdx % numTilesX;
        int tileY = tileIdx / numTilesX;

        int tileStartX = tileX * _codestream.Frame.TileWidth + _codestream.Frame.TileXOffset;
        int tileStartY = tileY * _codestream.Frame.TileHeight + _codestream.Frame.TileYOffset;
        int tileEndX = Math.Min(tileStartX + _codestream.Frame.TileWidth, _codestream.Frame.Width);
        int tileEndY = Math.Min(tileStartY + _codestream.Frame.TileHeight, _codestream.Frame.Height);
        int tileWidth = tileEndX - tileStartX;
        int tileHeight = tileEndY - tileStartY;

        // Apply component subsampling
        int compIndex = tier2Output.ComponentIndex;
        Jp2Component comp = _codestream.Frame.Components[compIndex];
        int compTileWidth = (tileWidth + comp.XSubsampling - 1) / comp.XSubsampling;
        int compTileHeight = (tileHeight + comp.YSubsampling - 1) / comp.YSubsampling;

        // Get quantization step sizes
        QuantizationParameters qParams = _codestream.QuantizationParameters;
        var stepIdx = 0;

        for (var r = 0; r < numResolutions; r++)
        {
            int startSubband = (r == 0) ? 0 : 1;
            int numSubbands = (r == 0) ? 1 : 4;

            // Calculate resolution dimensions using ceiling division
            int shift = numResolutions - 1 - r;
            int resWidth = (compTileWidth + (1 << shift) - 1) >> shift;
            int resHeight = (compTileHeight + (1 << shift) - 1) >> shift;
            if (resWidth == 0) resWidth = 1;
            if (resHeight == 0) resHeight = 1;

            for (var s = startSubband; s < numSubbands; s++)
            {
                SubbandType type;
                int subbandWidth, subbandHeight;

                if (r == 0)
                {
                    type = SubbandType.LL;
                    subbandWidth = resWidth;
                    subbandHeight = resHeight;
                }
                else
                {
                    // Subband type assignment per JPEG2000 spec:
                    // s=1 → HL, s=2 → LH, s=3 → HH
                    type = s switch { 1 => SubbandType.HL, 2 => SubbandType.LH, _ => SubbandType.HH };
                    switch (s)
                    {
                        case 1: // HL (High horizontal, Low vertical)
                            subbandWidth = resWidth / 2;           // floor (high-pass horizontal)
                            subbandHeight = (resHeight + 1) / 2;   // ceil (low-pass vertical)
                            break;
                        case 2: // LH (Low horizontal, High vertical)
                            subbandWidth = (resWidth + 1) / 2;     // ceil (low-pass horizontal)
                            subbandHeight = resHeight / 2;         // floor (high-pass vertical)
                            break;
                        default: // HH (High both directions, case 3)
                            subbandWidth = resWidth / 2;           // floor
                            subbandHeight = resHeight / 2;         // floor
                            break;
                    }
                }

                // Get quantization step size
                QuantizationStepSize stepSize;
                if (stepIdx < qParams.StepSizes.Length)
                {
                    stepSize = qParams.StepSizes[stepIdx++];
                }
                else if (qParams.Style == QuantizationStyle.ScalarDerived && qParams.StepSizes.Length > 0)
                {
                    // Derive from base step size
                    stepSize = qParams.StepSizes[0];
                }
                else
                {
                    stepSize = new QuantizationStepSize(0, 0);
                }

                // Calculate subband starting coordinates in tile component reference grid
                // Per JPEG2000 spec and OpenJPEG implementation
                int x0, y0;
                if (r == 0)
                {
                    // LL subband at lowest resolution starts at origin
                    x0 = 0;
                    y0 = 0;
                }
                else
                {
                    // For detail subbands, coordinates depend on subband type
                    // HL: low-pass horizontal (left), high-pass vertical (bottom half)
                    // LH: high-pass horizontal (right half), low-pass vertical (top)
                    // HH: high-pass both directions (bottom-right quadrant)
                    switch (s)
                    {
                        case 0: // HL
                            x0 = 0;
                            y0 = resHeight / 2;
                            break;
                        case 1: // LH
                            x0 = resWidth / 2;
                            y0 = 0;
                            break;
                        default: // HH
                            x0 = resWidth / 2;
                            y0 = resHeight / 2;
                            break;
                    }
                }

                // Decode code-blocks and assemble into subband
                var subbandCoefs = new int[subbandHeight, subbandWidth];
                int cbWidth = _codestream.CodingParameters.CodeBlockWidth;
                int cbHeight = _codestream.CodingParameters.CodeBlockHeight;

                // Calculate number of code-blocks in this subband
                int numCbX = (subbandWidth + cbWidth - 1) / cbWidth;
                int numCbY = (subbandHeight + cbHeight - 1) / cbHeight;
                var zeroBitPlanes = new int[numCbY, numCbX];

                if (r < tier2Output.CodeBlocks.Length && s < tier2Output.CodeBlocks[r].Length)
                {
                    CodeBlockBitstream[] codeBlocks = tier2Output.CodeBlocks[r][s];

                    foreach (CodeBlockBitstream cb in codeBlocks)
                    {
                        // Bounds check for debugging
                        if (cb.BlockX >= numCbX || cb.BlockY >= numCbY)
                        {
                            Console.WriteLine($"[ERROR] CB out of bounds: r={r}, s={s}, cb=({cb.BlockX},{cb.BlockY}), grid=({numCbX},{numCbY})");
                            Console.WriteLine($"[ERROR] Subband: {subbandWidth}x{subbandHeight}, CB size: {cbWidth}x{cbHeight}");
                            throw new InvalidOperationException($"Code-block ({cb.BlockX},{cb.BlockY}) exceeds grid bounds ({numCbX},{numCbY}) at resolution {r}, subband {s}");
                        }

                        // Store zero bitplanes for this code-block
                        zeroBitPlanes[cb.BlockY, cb.BlockX] = cb.ZeroBitPlanes;

                        // Decode the code-block (pass resolution and subband for debug logging)
                        int[,] decoded = _ebcot.Process(cb, r, s);

                        // Log decoded coefficients (skip empty code-blocks with zero passes)
                        if (cb.CodingPasses > 0)
                        {
                            LogCodeBlockCoefficients(r, s, cb, decoded, cbWidth, cbHeight);
                        }

                        // Copy into subband array
                        int startX = cb.BlockX * cbWidth;
                        int startY = cb.BlockY * cbHeight;

                        for (var y = 0; y < decoded.GetLength(0) && startY + y < subbandHeight; y++)
                        {
                            for (var x = 0; x < decoded.GetLength(1) && startX + x < subbandWidth; x++)
                            {
                                subbandCoefs[startY + y, startX + x] = decoded[y, x];
                            }
                        }
                    }
                }

                subbands.Add(new QuantizedSubband
                {
                    Type = type,
                    ResolutionLevel = r,
                    Width = subbandWidth,
                    Height = subbandHeight,
                    X0 = x0,
                    Y0 = y0,
                    StepSize = stepSize,
                    Coefficients = subbandCoefs,
                    CodeBlockWidth = cbWidth,
                    CodeBlockHeight = cbHeight,
                    CodeBlockZeroBitPlanes = zeroBitPlanes,
                });
            }
        }

        return subbands.ToArray();
    }

    /// <summary>
    /// Decodes a single code-block.
    /// </summary>
    public int[,] DecodeCodeBlock(CodeBlockBitstream codeBlock)
    {
        return _ebcot.Process(codeBlock);
    }

    private static void LogCodeBlockCoefficients(int resolution, int subband, CodeBlockBitstream cb, int[,] coefficients, int nominalCbWidth, int nominalCbHeight)
    {
        using var writer = new System.IO.StreamWriter(@"C:\temp\our-tier1-coefficients.txt", append: true);

        writer.WriteLine("=== DECODED CODE-BLOCK COEFFICIENTS ===");
        writer.WriteLine($"Resolution={resolution}, Subband={subband}, CB({cb.BlockX},{cb.BlockY})");
        writer.WriteLine($"Dimensions: {cb.Width}x{cb.Height}");
        writer.WriteLine($"Total Passes: {cb.CodingPasses}");
        writer.WriteLine($"Zero Bit-Planes: {cb.ZeroBitPlanes}");

        // Log first row of coefficients (up to 32 values)
        writer.Write("First row coefficients: ");
        int maxCols = Math.Min(32, coefficients.GetLength(1));
        for (var x = 0; x < maxCols; x++)
        {
            writer.Write($"{coefficients[0, x]:X8} ");
        }
        writer.WriteLine();

        // Log coefficient statistics
        // Use nominal code-block size (to match CoreJ2K) instead of actual dimensions
        int nominalSize = nominalCbWidth * nominalCbHeight;
        int actualSize = coefficients.GetLength(0) * coefficients.GetLength(1);

        int nonZeroCount = 0;
        int minCoef = 0; // Start with 0 to account for padding (like CoreJ2K)
        int maxCoef = 0;

        for (var y = 0; y < coefficients.GetLength(0); y++)
        {
            for (var x = 0; x < coefficients.GetLength(1); x++)
            {
                int coef = coefficients[y, x];
                if (coef != 0) nonZeroCount++;

                // Convert from sign-magnitude to two's complement for min/max
                int value;
                if ((coef & unchecked((int)0x80000000)) != 0)
                {
                    // Negative: extract magnitude and negate
                    value = -(coef & 0x7FFFFFFF);
                }
                else
                {
                    value = coef;
                }

                if (value < minCoef) minCoef = value;
                if (value > maxCoef) maxCoef = value;
            }
        }

        writer.WriteLine($"Non-zero count: {nonZeroCount}/{nominalSize}");
        writer.WriteLine($"Min coefficient: {minCoef}, Max coefficient: {maxCoef}");
        writer.WriteLine();
    }
}