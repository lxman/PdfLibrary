using System;
using ImageLibrary.Jp2.Pipeline;

namespace ImageLibrary.Jp2.PostProcess;

/// <summary>
/// Post-processing: inverse color transform, level shift, and clamping.
/// </summary>
internal class PostProcessor : IPostProcessor
{
    private readonly Jp2Codestream _codestream;
    private readonly bool _hasColorTransform;
    private readonly bool _isReversible;
    private readonly int _jp2ColorSpace;
    private readonly int[]? _channelMapping; // Maps output position to codestream component index

    // JP2 color space constants
    private const int ColorSpaceSRGB = 16;
    private const int ColorSpaceGreyscale = 17;
    private const int ColorSpaceYCC = 18;

    public PostProcessor(Jp2Codestream codestream, int jp2ColorSpace = 0, ChannelDefinition[]? channelDefinitions = null)
    {
        _codestream = codestream;
        _jp2ColorSpace = jp2ColorSpace;

        // Build channel mapping from channel definitions
        // Maps output position (by Association) to codestream component index
        if (channelDefinitions != null && channelDefinitions.Length > 0)
        {
            var maxAssoc = 0;
            foreach (ChannelDefinition def in channelDefinitions)
            {
                if (def.Association > 0 && def.Association > maxAssoc)
                    maxAssoc = def.Association;
            }
            if (maxAssoc > 0)
            {
                _channelMapping = new int[maxAssoc];
                for (var i = 0; i < _channelMapping.Length; i++)
                    _channelMapping[i] = i; // Default: identity mapping
                foreach (ChannelDefinition def in channelDefinitions)
                {
                    if (def.Association > 0 && def.Association <= maxAssoc && def.Type == 0)
                    {
                        // Association is 1-based, channel mapping is 0-based
                        _channelMapping[def.Association - 1] = def.Channel;
                    }
                }
            }
        }

        // Apply inverse color transform when:
        // 1. MCT flag is set in codestream (data was transformed during encoding), OR
        // 2. ColorSpace is sYCC (18) - data needs YCbCr to RGB conversion
        _hasColorTransform = codestream.CodingParameters.MultipleComponentTransform != 0
                             || jp2ColorSpace == ColorSpaceYCC;

        // Use reversible transform (RCT) when wavelet is reversible 5/3, otherwise ICT
        _isReversible = codestream.CodingParameters.WaveletType == WaveletTransform.Reversible_5_3;
    }

    public byte[] Process(ReconstructedTile input)
    {
        int width = input.Width;
        int height = input.Height;
        int numComponents = input.Components.Length;

        // Apply channel remapping if needed (from cdef box)
        double[][,] remappedComponents;
        if (_channelMapping != null && _channelMapping.Length <= numComponents)
        {
            remappedComponents = new double[_channelMapping.Length][,];
            for (var i = 0; i < _channelMapping.Length; i++)
            {
                int srcIndex = _channelMapping[i];
                if (srcIndex < numComponents)
                {
                    remappedComponents[i] = input.Components[srcIndex];
                }
                else
                {
                    remappedComponents[i] = input.Components[i];
                }
            }
            numComponents = _channelMapping.Length;
        }
        else
        {
            remappedComponents = input.Components;
        }

        // Apply inverse color transform if needed
        double[][,] components;
        if (_hasColorTransform && numComponents >= 3)
        {
            components = InverseColorTransform(remappedComponents);
        }
        else
        {
            components = remappedComponents;
        }

        // Apply level shift and clamping, then interleave
        var result = new byte[width * height * numComponents];
        var idx = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                for (var c = 0; c < numComponents; c++)
                {
                    double val = components[c][y, x];

                    // Apply level shift (add 2^(bitdepth-1) for signed components)
                    Jp2Component comp = _codestream.Frame.Components[c];
                    if (!comp.IsSigned)
                    {
                        val += 1 << (comp.Precision - 1);
                    }

                    // Clamp to a valid range for this component's precision
                    int maxVal = (1 << comp.Precision) - 1;
                    var intVal = (int)Math.Round(val);
                    intVal = Math.Max(0, Math.Min(maxVal, intVal));

                    // Scale down to 8-bit if precision > 8
                    if (comp.Precision > 8)
                    {
                        intVal >>= (comp.Precision - 8);
                    }

                    result[idx++] = (byte)intVal;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Applies inverse RCT (Reversible Color Transform) or ICT (Irreversible Color Transform).
    /// Converts YCbCr back to RGB.
    /// </summary>
    private double[][,] InverseColorTransform(double[][,] ycc)
    {
        int height = ycc[0].GetLength(0);
        int width = ycc[0].GetLength(1);

        double[,] y = ycc[0];
        double[,] cb = ycc[1];
        double[,] cr = ycc[2];

        // Handle subsampled chroma components (e.g., 4:2:0, 4:2:2)
        // Upsample Cb and Cr to match Y dimensions if needed
        if (cb.GetLength(0) != height || cb.GetLength(1) != width)
        {
            cb = UpsampleComponent(cb, height, width);
        }

        if (cr.GetLength(0) != height || cr.GetLength(1) != width)
        {
            cr = UpsampleComponent(cr, height, width);
        }

        // For sYCC color space (18), always use ICT matrix regardless of wavelet type
        // because sYCC is defined using the ICT/YCbCr coefficients.
        // RCT is only used when MCT was applied during encoding with reversible wavelet.
        bool useRct = _isReversible && _jp2ColorSpace != ColorSpaceYCC;

        var r = new double[height, width];
        var g = new double[height, width];
        var b = new double[height, width];

        if (useRct)
        {
            // Inverse RCT (Reversible Component Transform)
            // G = Y - floor((Cb + Cr) / 4)
            // R = Cr + G
            // B = Cb + G
            for (var py = 0; py < height; py++)
            {
                for (var px = 0; px < width; px++)
                {
                    double yVal = y[py, px];
                    double cbVal = cb[py, px];
                    double crVal = cr[py, px];

                    g[py, px] = yVal - Math.Floor((cbVal + crVal) / 4.0);
                    r[py, px] = crVal + g[py, px];
                    b[py, px] = cbVal + g[py, px];
                }
            }
        }
        else
        {
            // Inverse ICT (Irreversible Component Transform)
            // Standard sYCC / YCbCr to RGB conversion (Rec. 601 coefficients)
            // R = Y + 1.402 * Cr
            // G = Y - 0.34413 * Cb - 0.71414 * Cr
            // B = Y + 1.772 * Cb
            for (var py = 0; py < height; py++)
            {
                for (var px = 0; px < width; px++)
                {
                    double yVal = y[py, px];
                    double cbVal = cb[py, px];
                    double crVal = cr[py, px];

                    r[py, px] = yVal + 1.402 * crVal;
                    g[py, px] = yVal - 0.34413 * cbVal - 0.71414 * crVal;
                    b[py, px] = yVal + 1.772 * cbVal;
                }
            }
        }

        return [r, g, b];
    }

    /// <summary>
    /// Upsamples a component to the target dimensions using bilinear interpolation.
    /// </summary>
    private static double[,] UpsampleComponent(double[,] src, int targetHeight, int targetWidth)
    {
        int srcHeight = src.GetLength(0);
        int srcWidth = src.GetLength(1);

        var result = new double[targetHeight, targetWidth];

        // Calculate scaling factors
        double scaleY = (double)srcHeight / targetHeight;
        double scaleX = (double)srcWidth / targetWidth;

        for (var y = 0; y < targetHeight; y++)
        {
            double srcY = y * scaleY;
            int y0 = Math.Min((int)srcY, srcHeight - 1);
            int y1 = Math.Min(y0 + 1, srcHeight - 1);
            double fy = srcY - y0;

            for (var x = 0; x < targetWidth; x++)
            {
                double srcX = x * scaleX;
                int x0 = Math.Min((int)srcX, srcWidth - 1);
                int x1 = Math.Min(x0 + 1, srcWidth - 1);
                double fx = srcX - x0;

                // Bilinear interpolation
                double v00 = src[y0, x0];
                double v01 = src[y0, x1];
                double v10 = src[y1, x0];
                double v11 = src[y1, x1];

                double v0 = v00 + fx * (v01 - v00);
                double v1 = v10 + fx * (v11 - v10);
                result[y, x] = v0 + fy * (v1 - v0);
            }
        }

        return result;
    }
}

/// <summary>
/// Convenience class to process grayscale images directly.
/// </summary>
internal class GrayscalePostProcessor
{
    private readonly Jp2Codestream _codestream;

    public GrayscalePostProcessor(Jp2Codestream codestream)
    {
        _codestream = codestream;
    }

    /// <summary>
    /// Converts reconstructed double values to byte array.
    /// </summary>
    public byte[] Process(double[,] reconstructed)
    {
        int height = reconstructed.GetLength(0);
        int width = reconstructed.GetLength(1);
        Jp2Component comp = _codestream.Frame.Components[0];

        var result = new byte[width * height];
        var idx = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                double val = reconstructed[y, x];

                // Apply level shift
                if (!comp.IsSigned)
                {
                    val += 1 << (comp.Precision - 1);
                }

                // Clamp
                int maxVal = (1 << comp.Precision) - 1;
                var intVal = (int)Math.Round(val);
                intVal = Math.Max(0, Math.Min(maxVal, intVal));

                result[idx++] = (byte)intVal;
            }
        }

        return result;
    }
}