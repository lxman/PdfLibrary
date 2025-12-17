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
            int numSubbands = (r == 0) ? 1 : 3;

            // Calculate resolution dimensions using ceiling division
            int shift = numResolutions - 1 - r;
            int resWidth = (compTileWidth + (1 << shift) - 1) >> shift;
            int resHeight = (compTileHeight + (1 << shift) - 1) >> shift;
            if (resWidth == 0) resWidth = 1;
            if (resHeight == 0) resHeight = 1;

            for (var s = 0; s < numSubbands; s++)
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
                    type = s switch { 0 => SubbandType.HL, 1 => SubbandType.LH, _ => SubbandType.HH };
                    // Subband dimensions depend on orientation:
                    // HL: high horizontal (floor), low vertical (ceil)
                    // LH: low horizontal (ceil), high vertical (floor)
                    // HH: high both (floor)
                    switch (s)
                    {
                        case 0: // HL
                            subbandWidth = resWidth / 2;           // floor
                            subbandHeight = (resHeight + 1) / 2;   // ceil
                            break;
                        case 1: // LH
                            subbandWidth = (resWidth + 1) / 2;     // ceil
                            subbandHeight = resHeight / 2;         // floor
                            break;
                        default: // HH
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

                // Decode code-blocks and assemble into subband
                var subbandCoefs = new int[subbandHeight, subbandWidth];

                if (r < tier2Output.CodeBlocks.Length && s < tier2Output.CodeBlocks[r].Length)
                {
                    CodeBlockBitstream[] codeBlocks = tier2Output.CodeBlocks[r][s];
                    int cbWidth = _codestream.CodingParameters.CodeBlockWidth;
                    int cbHeight = _codestream.CodingParameters.CodeBlockHeight;

                    foreach (CodeBlockBitstream cb in codeBlocks)
                    {
                        // Decode the code-block
                        int[,] decoded = _ebcot.Process(cb);

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
                    StepSize = stepSize,
                    Coefficients = subbandCoefs,
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
}