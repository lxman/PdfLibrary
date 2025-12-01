using System;
using System.Collections.Generic;
using System.IO;

namespace Compressors.Jpeg2k;

/// <summary>
/// JPEG2000 codestream encoder with multi-component support.
/// </summary>
public class Jp2kEncoder
{
    private readonly int _quality;
    private readonly int _numLevels;
    private readonly bool _lossy;
    private readonly int _codeBlockWidth;
    private readonly int _codeBlockHeight;
    private readonly bool _useColorTransform;

    public Jp2kEncoder(
        int quality = 75,
        int numLevels = 5,
        bool lossy = true,
        int codeBlockWidth = 64,
        int codeBlockHeight = 64,
        bool useColorTransform = true)
    {
        _quality = Math.Clamp(quality, 1, 100);
        _numLevels = Math.Clamp(numLevels, 1, 10);
        _lossy = lossy;
        _codeBlockWidth = codeBlockWidth;
        _codeBlockHeight = codeBlockHeight;
        _useColorTransform = useColorTransform;
    }

    /// <summary>
    /// Encodes image data to JPEG2000 codestream.
    /// </summary>
    /// <param name="imageData">Image data (component-interleaved: R,G,B,R,G,B,... or C,M,Y,K,...)</param>
    /// <param name="width">Image width</param>
    /// <param name="height">Image height</param>
    /// <param name="numComponents">Number of components (1=grayscale, 3=RGB, 4=CMYK)</param>
    public byte[] Encode(ReadOnlySpan<byte> imageData, int width, int height, int numComponents = 1)
    {
        using var output = new MemoryStream();
        Encode(imageData, width, height, numComponents, output);
        return output.ToArray();
    }

    /// <summary>
    /// Encodes image data to JPEG2000 codestream.
    /// </summary>
    public void Encode(ReadOnlySpan<byte> imageData, int width, int height, int numComponents, Stream output)
    {
        int pixelCount = width * height;
        bool applyMct = _useColorTransform && numComponents >= 3;

        // Separate and level-shift components
        var components = new float[numComponents][];
        for (int c = 0; c < numComponents; c++)
        {
            components[c] = new float[pixelCount];
        }

        // De-interleave component data
        for (int i = 0; i < pixelCount && i * numComponents < imageData.Length; i++)
        {
            for (int c = 0; c < numComponents; c++)
            {
                int idx = i * numComponents + c;
                if (idx < imageData.Length)
                {
                    components[c][i] = imageData[idx] - 128;  // Level shift
                }
            }
        }

        // Apply irreversible color transform (ICT) for RGB if enabled
        if (applyMct && _lossy)
        {
            ApplyForwardICT(components[0], components[1], components[2], pixelCount);
        }
        // Apply reversible color transform (RCT) for lossless RGB
        else if (applyMct && !_lossy)
        {
            ApplyForwardRCT(components[0], components[1], components[2], pixelCount);
        }

        // Process each component
        var allSubbands = new List<Subband[]>();
        float baseStep = Jp2kQuantization.QualityToStep(_quality);
        var stepSizes = Jp2kQuantization.CalculateStepSizes(baseStep, _numLevels, _lossy);

        for (int c = 0; c < numComponents; c++)
        {
            // Apply DWT
            Wavelet.Forward2D(components[c], width, height, _numLevels, _lossy);

            // Create subbands for this component
            var subbands = CreateSubbands(width, height, c);

            // Quantize and populate code-blocks
            PopulateCodeBlocks(components[c], width, subbands, stepSizes);

            // Encode code-blocks with EBCOT tier-1
            foreach (var subband in subbands)
            {
                if (subband.CodeBlocks == null) continue;

                int blocksY = subband.CodeBlocks.GetLength(0);
                int blocksX = subband.CodeBlocks.GetLength(1);

                for (int by = 0; by < blocksY; by++)
                {
                    for (int bx = 0; bx < blocksX; bx++)
                    {
                        EbcotTier1.EncodeBlock(subband.CodeBlocks[by, bx]);
                    }
                }
            }

            allSubbands.Add(subbands);
        }

        // Write codestream
        using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, true);
        WriteCodestream(writer, width, height, numComponents, allSubbands, stepSizes, applyMct);
    }

    /// <summary>
    /// Forward Irreversible Color Transform (ICT) - RGB to YCbCr.
    /// </summary>
    private static void ApplyForwardICT(float[] r, float[] g, float[] b, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float rVal = r[i];
            float gVal = g[i];
            float bVal = b[i];

            // RGB to YCbCr (ITU-R BT.601)
            r[i] = 0.299f * rVal + 0.587f * gVal + 0.114f * bVal;           // Y
            g[i] = -0.16875f * rVal - 0.33126f * gVal + 0.5f * bVal;        // Cb
            b[i] = 0.5f * rVal - 0.41869f * gVal - 0.08131f * bVal;         // Cr
        }
    }

    /// <summary>
    /// Forward Reversible Color Transform (RCT) - for lossless coding.
    /// </summary>
    private static void ApplyForwardRCT(float[] r, float[] g, float[] b, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float rVal = r[i];
            float gVal = g[i];
            float bVal = b[i];

            float y = MathF.Floor((rVal + 2 * gVal + bVal) / 4);
            float cb = bVal - gVal;
            float cr = rVal - gVal;

            r[i] = y;
            g[i] = cb;
            b[i] = cr;
        }
    }

    /// <summary>
    /// Creates subband structure for a component.
    /// </summary>
    private Subband[] CreateSubbands(int width, int height, int componentIndex)
    {
        var subbands = new List<Subband>();

        int currentWidth = width;
        int currentHeight = height;
        int offsetX = 0;
        int offsetY = 0;

        for (int level = 0; level < _numLevels; level++)
        {
            int lowWidth = (currentWidth + 1) / 2;
            int highWidth = currentWidth / 2;
            int lowHeight = (currentHeight + 1) / 2;
            int highHeight = currentHeight / 2;

            // HL subband (top-right)
            var hl = new Subband(
                Jp2kConstants.SubbandHL, level,
                highWidth, lowHeight,
                offsetX + lowWidth, offsetY);
            hl.ComponentIndex = componentIndex;
            subbands.Add(hl);

            // LH subband (bottom-left)
            var lh = new Subband(
                Jp2kConstants.SubbandLH, level,
                lowWidth, highHeight,
                offsetX, offsetY + lowHeight);
            lh.ComponentIndex = componentIndex;
            subbands.Add(lh);

            // HH subband (bottom-right)
            var hh = new Subband(
                Jp2kConstants.SubbandHH, level,
                highWidth, highHeight,
                offsetX + lowWidth, offsetY + lowHeight);
            hh.ComponentIndex = componentIndex;
            subbands.Add(hh);

            currentWidth = lowWidth;
            currentHeight = lowHeight;
        }

        // LL subband at deepest level
        var ll = new Subband(
            Jp2kConstants.SubbandLL, _numLevels,
            currentWidth, currentHeight,
            offsetX, offsetY);
        ll.ComponentIndex = componentIndex;
        subbands.Add(ll);

        // Create code-blocks for each subband
        foreach (var subband in subbands)
        {
            subband.CreateCodeBlocks(_codeBlockWidth, _codeBlockHeight);
        }

        return subbands.ToArray();
    }

    /// <summary>
    /// Populates code-blocks with quantized coefficients.
    /// </summary>
    private void PopulateCodeBlocks(float[] coefficients, int stride, Subband[] subbands, float[,] stepSizes)
    {
        foreach (var subband in subbands)
        {
            int level = subband.Level;
            int type = subband.Type;
            float stepSize = stepSizes[level, type];
            subband.QuantStep = stepSize;

            if (subband.CodeBlocks == null) continue;

            int blocksY = subband.CodeBlocks.GetLength(0);
            int blocksX = subband.CodeBlocks.GetLength(1);

            for (int by = 0; by < blocksY; by++)
            {
                for (int bx = 0; bx < blocksX; bx++)
                {
                    var block = subband.CodeBlocks[by, bx];
                    block.Initialize();

                    for (int y = 0; y < block.Height; y++)
                    {
                        for (int x = 0; x < block.Width; x++)
                        {
                            int globalX = block.X + x;
                            int globalY = block.Y + y;

                            if (globalX < stride && globalY * stride + globalX < coefficients.Length)
                            {
                                float coeff = coefficients[globalY * stride + globalX];

                                // Quantize
                                int magnitude = Jp2kQuantization.Quantize(coeff, stepSize);
                                bool negative = coeff < 0;

                                block.Coefficients![y, x] = magnitude;
                                block.Signs![y, x] = negative;
                            }
                        }
                    }

                    block.CalculateBitPlanes();
                }
            }
        }
    }

    /// <summary>
    /// Writes the JPEG2000 codestream.
    /// </summary>
    private void WriteCodestream(BinaryWriter writer, int width, int height, int numComponents,
        List<Subband[]> allSubbands, float[,] stepSizes, bool useMct)
    {
        // SOC marker
        WriteMarker(writer, Jp2kConstants.SOC);

        // SIZ marker
        WriteSizMarker(writer, width, height, numComponents);

        // COD marker
        WriteCodMarker(writer, useMct);

        // QCD marker
        WriteQcdMarker(writer, stepSizes);

        // SOT marker (Start of Tile)
        WriteSotMarker(writer);

        // SOD marker and tile data
        WriteMarker(writer, Jp2kConstants.SOD);

        // Write packet data for each component
        for (int c = 0; c < numComponents; c++)
        {
            var packets = EbcotTier2.AssemblePackets(allSubbands[c], 1);
            foreach (var packet in packets)
            {
                EbcotTier2.WritePacket(writer, packet);
            }
        }

        // EOC marker
        WriteMarker(writer, Jp2kConstants.EOC);
    }

    private static void WriteMarker(BinaryWriter writer, ushort marker)
    {
        writer.Write((byte)(marker >> 8));
        writer.Write((byte)(marker & 0xFF));
    }

    private static void WriteUInt16BE(BinaryWriter writer, ushort value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)(value & 0xFF));
    }

    private static void WriteUInt32BE(BinaryWriter writer, uint value)
    {
        writer.Write((byte)(value >> 24));
        writer.Write((byte)(value >> 16));
        writer.Write((byte)(value >> 8));
        writer.Write((byte)(value & 0xFF));
    }

    private void WriteSizMarker(BinaryWriter writer, int width, int height, int numComponents)
    {
        WriteMarker(writer, Jp2kConstants.SIZ);

        // Length = 38 + 3 * numComponents
        int length = 38 + 3 * numComponents;
        WriteUInt16BE(writer, (ushort)length);

        // Rsiz (capabilities)
        WriteUInt16BE(writer, 0);

        // Image dimensions
        WriteUInt32BE(writer, (uint)width);   // Xsiz
        WriteUInt32BE(writer, (uint)height);  // Ysiz

        // Image offset
        WriteUInt32BE(writer, 0);  // XOsiz
        WriteUInt32BE(writer, 0);  // YOsiz

        // Tile dimensions (single tile = image size)
        WriteUInt32BE(writer, (uint)width);   // XTsiz
        WriteUInt32BE(writer, (uint)height);  // YTsiz

        // Tile offset
        WriteUInt32BE(writer, 0);  // XTOsiz
        WriteUInt32BE(writer, 0);  // YTOsiz

        // Number of components
        WriteUInt16BE(writer, (ushort)numComponents);

        // Component parameters
        for (int c = 0; c < numComponents; c++)
        {
            writer.Write((byte)7);   // Ssiz (8-bit unsigned)
            writer.Write((byte)1);   // XRsiz (horizontal sampling)
            writer.Write((byte)1);   // YRsiz (vertical sampling)
        }
    }

    private void WriteCodMarker(BinaryWriter writer, bool useMct)
    {
        WriteMarker(writer, Jp2kConstants.COD);

        // Length
        WriteUInt16BE(writer, (ushort)(12 + _numLevels + 1));

        // Scod (coding style)
        writer.Write((byte)0);

        // SGcod
        writer.Write((byte)Jp2kConstants.ProgressionCPRL);  // Component-Position-Resolution-Layer
        WriteUInt16BE(writer, 1);  // Number of layers
        writer.Write((byte)(useMct ? 1 : 0));  // Multiple component transform

        // SPcod
        writer.Write((byte)_numLevels);  // Number of decomposition levels
        writer.Write((byte)5);           // Code-block width exponent (32)
        writer.Write((byte)5);           // Code-block height exponent (32)
        writer.Write((byte)0);           // Code-block style
        writer.Write((byte)(_lossy ? 1 : 0));  // Wavelet transform (1 = 9/7, 0 = 5/3)

        // Precinct sizes (default)
        for (int i = 0; i <= _numLevels; i++)
        {
            writer.Write((byte)0xFF);  // PPx = PPy = 15 (32768)
        }
    }

    private void WriteQcdMarker(BinaryWriter writer, float[,] stepSizes)
    {
        WriteMarker(writer, Jp2kConstants.QCD);

        int numSubbands = 3 * _numLevels + 1;
        WriteUInt16BE(writer, (ushort)(3 + numSubbands * 2));

        // Sqcd (quantization style)
        writer.Write((byte)(_lossy ? 2 : 0));  // 2 = scalar derived, 0 = no quantization

        // Step sizes for each subband
        // LL at deepest level first
        WriteStepSize(writer, stepSizes[_numLevels, Jp2kConstants.SubbandLL]);

        // Then HL, LH, HH for each level
        for (int level = _numLevels - 1; level >= 0; level--)
        {
            WriteStepSize(writer, stepSizes[level, Jp2kConstants.SubbandHL]);
            WriteStepSize(writer, stepSizes[level, Jp2kConstants.SubbandLH]);
            WriteStepSize(writer, stepSizes[level, Jp2kConstants.SubbandHH]);
        }
    }

    private void WriteStepSize(BinaryWriter writer, float stepSize)
    {
        // Convert step size to mantissa/exponent format
        int exponent = Math.Max(0, -(int)MathF.Floor(MathF.Log(stepSize) / 0.693147f));
        float mantissa = stepSize * MathF.Pow(2, exponent) - 1;
        int mantissaBits = (int)(mantissa * (1 << 11));

        ushort value = (ushort)((exponent << 11) | (mantissaBits & 0x7FF));
        WriteUInt16BE(writer, value);
    }

    private void WriteSotMarker(BinaryWriter writer)
    {
        WriteMarker(writer, Jp2kConstants.SOT);
        WriteUInt16BE(writer, 10);  // Length

        WriteUInt16BE(writer, 0);   // Isot (tile index)
        WriteUInt32BE(writer, 0);   // Psot (tile length, 0 = until EOC)
        writer.Write((byte)0);      // TPsot (tile-part index)
        writer.Write((byte)1);      // TNsot (number of tile-parts)
    }
}
