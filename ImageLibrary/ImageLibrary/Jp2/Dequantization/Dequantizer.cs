using System;
using System.Collections.Generic;
using System.Linq;
using ImageLibrary.Jp2.Pipeline;

namespace ImageLibrary.Jp2.Dequantization;

/// <summary>
/// Dequantizes coefficient values from the Tier-1 decoder.
/// Converts quantized integers back to floating-point coefficients.
/// </summary>
internal class Dequantizer : IDequantizer
{
    private readonly Jp2Codestream _codestream;
    private readonly bool _isReversible;

    public Dequantizer(Jp2Codestream codestream)
    {
        _codestream = codestream;
        _isReversible = codestream.CodingParameters.WaveletType == WaveletTransform.Reversible_5_3;
    }

    // Explicit interface implementation for backward compatibility
    double[,] IPipelineStage<QuantizedSubband, double[,]>.Process(QuantizedSubband input)
    {
        return Process(input, 0);
    }

    public double[,] Process(QuantizedSubband input, int componentIndex)
    {
        var result = new double[input.Height, input.Width];

        if (_isReversible)
        {
            // Reversible (lossless) - coefficients are in sign-magnitude format:
            // Bit 31 = sign (0=positive, 1=negative), Bits 0-30 = magnitude
            //
            // For reversible coding, the formula is:
            // magbits = guardBits + exponent - 1
            // where exponent comes from the QCD marker step size for this subband.
            //
            // shift = 31 - magbits (to extract actual integer value)
            int guardBits = _codestream.QuantizationParameters.GuardBits;
            int exponent = input.StepSize.Exponent;
            int magbits = guardBits + exponent - 1;
            int shift = 31 - magbits;

            for (var y = 0; y < input.Height; y++)
            {
                for (var x = 0; x < input.Width; x++)
                {
                    int signMag = input.Coefficients[y, x];
                    // Convert sign-magnitude to signed integer with proper shift
                    if (signMag >= 0)
                    {
                        // Positive: just right-shift
                        result[y, x] = signMag >> shift;
                    }
                    else
                    {
                        // Negative: mask off sign bit, shift, then negate
                        result[y, x] = -((signMag & 0x7FFFFFFF) >> shift);
                    }
                }
            }
        }
        else
        {
            // Irreversible - coefficients are in sign-magnitude format
            // magbits = guardBits + exponent - 1
            int guardBits = _codestream.QuantizationParameters.GuardBits;
            int exponent = input.StepSize.Exponent;
            int magbits = guardBits + exponent - 1;
            int shift = 31 - magbits;

            // Get component bit depth (Rb in JPEG2000 spec)
            int bitDepth = _codestream.Frame.Components[componentIndex].Precision;

            // Correct step size formula: delta = 2^(Rb - epsilon) * (1 + mantissa/2048)
            // where Rb is the component precision and epsilon is the exponent from QCD marker
            double stepSize = Math.Pow(2, bitDepth - exponent) * (1.0 + input.StepSize.Mantissa / 2048.0);

            for (var y = 0; y < input.Height; y++)
            {
                for (var x = 0; x < input.Width; x++)
                {
                    int signMag = input.Coefficients[y, x];
                    // Extract quantized magnitude by shifting
                    int magnitude = (signMag >= 0) ? (signMag >> shift) : ((signMag & 0x7FFFFFFF) >> shift);
                    bool negative = signMag < 0;

                    // Reconstruct: value = sign * (|q| + 0.5) * stepSize
                    double q = magnitude;
                    if (negative)
                    {
                        result[y, x] = -(q + 0.5) * stepSize;
                    }
                    else
                    {
                        result[y, x] = (q + 0.5) * stepSize;
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Dequantizes all subbands from a tile-component.
    /// </summary>
    public DwtCoefficients DequantizeAll(QuantizedSubband[] subbands, int componentIndex)
    {
        // Organize subbands by resolution level
        int numResolutions = subbands.Max(s => s.ResolutionLevel) + 1;

        // Calculate full resolution dimensions from highest resolution subband
        QuantizedSubband[] highestRes = subbands.Where(s => s.ResolutionLevel == numResolutions - 1).ToArray();
        int width = 0, height = 0;

        if (numResolutions == 1)
        {
            // Only LL subband
            QuantizedSubband ll = subbands.First(s => s.Type == SubbandType.LL);
            width = ll.Width;
            height = ll.Height;
        }
        else
        {
            // Compute from highest resolution detail subbands
            foreach (QuantizedSubband sub in highestRes)
            {
                if (sub.Type == SubbandType.HL || sub.Type == SubbandType.HH)
                {
                    width = Math.Max(width, sub.Width * 2);
                }
                if (sub.Type == SubbandType.LH || sub.Type == SubbandType.HH)
                {
                    height = Math.Max(height, sub.Height * 2);
                }
            }
            // If no detail subbands, use LL dimensions
            if (width == 0 || height == 0)
            {
                QuantizedSubband ll = subbands.First(s => s.Type == SubbandType.LL);
                width = ll.Width;
                height = ll.Height;
            }
        }

        // Create dequantized subbands array
        // Index: level 0 = LL, level n > 0 = [HL, LH, HH] for that decomposition
        int numDecompLevels = numResolutions - 1;
        var dequantized = new List<double[,]>();

        // First add LL subband
        QuantizedSubband llSubband = subbands.First(s => s.Type == SubbandType.LL);
        dequantized.Add(Process(llSubband, componentIndex));

        // Then add detail subbands for each resolution level > 0
        for (var r = 1; r < numResolutions; r++)
        {
            QuantizedSubband? hl = subbands.FirstOrDefault(s => s.ResolutionLevel == r && s.Type == SubbandType.HL);
            QuantizedSubband? lh = subbands.FirstOrDefault(s => s.ResolutionLevel == r && s.Type == SubbandType.LH);
            QuantizedSubband? hh = subbands.FirstOrDefault(s => s.ResolutionLevel == r && s.Type == SubbandType.HH);

            if (hl != null) dequantized.Add(Process(hl, componentIndex));
            if (lh != null) dequantized.Add(Process(lh, componentIndex));
            if (hh != null) dequantized.Add(Process(hh, componentIndex));
        }

        return new DwtCoefficients
        {
            ComponentIndex = componentIndex,
            DecompositionLevels = numDecompLevels,
            Width = width,
            Height = height,
            Subbands = dequantized.ToArray(),
        };
    }
}