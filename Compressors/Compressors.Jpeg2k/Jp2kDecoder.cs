using System;
using System.Collections.Generic;
using System.IO;

namespace Compressors.Jpeg2k;

/// <summary>
/// JPEG2000 codestream decoder.
/// </summary>
public class Jp2kDecoder
{
    private int _width;
    private int _height;
    private int _numLevels;
    private bool _lossy;
    private int _numComponents;
    private bool _useMct;
    private float[,]? _stepSizes;

    public int Width => _width;
    public int Height => _height;
    public int NumComponents => _numComponents;

    /// <summary>
    /// Decodes a JPEG2000 codestream to image data.
    /// </summary>
    public byte[] Decode(byte[] data)
    {
        using var stream = new MemoryStream(data);
        return Decode(stream);
    }

    /// <summary>
    /// Decodes a JPEG2000 codestream to image data.
    /// Returns component-interleaved data (R,G,B,R,G,B,... or C,M,Y,K,...).
    /// </summary>
    public byte[] Decode(Stream stream)
    {
        using var reader = new BinaryReader(stream);

        // Parse main header
        ParseMainHeader(reader);

        int pixelCount = _width * _height;

        // Create subbands for each component
        var allSubbands = new List<Subband[]>();
        for (int c = 0; c < _numComponents; c++)
        {
            var subbands = CreateSubbands(c);
            allSubbands.Add(subbands);
        }

        // Parse tile-parts and decode all components
        ParseTileData(reader, allSubbands);

        // Reconstruct and inverse DWT for each component
        var components = new float[_numComponents][];
        for (int c = 0; c < _numComponents; c++)
        {
            components[c] = ReconstructFromSubbands(allSubbands[c]);
            Wavelet.Inverse2D(components[c], _width, _height, _numLevels, _lossy);
        }

        // Apply inverse color transform if needed
        if (_useMct && _numComponents >= 3)
        {
            if (_lossy)
            {
                ApplyInverseICT(components[0], components[1], components[2], pixelCount);
            }
            else
            {
                ApplyInverseRCT(components[0], components[1], components[2], pixelCount);
            }
        }

        // Convert to interleaved bytes with level shift
        var output = new byte[pixelCount * _numComponents];
        for (int i = 0; i < pixelCount; i++)
        {
            for (int c = 0; c < _numComponents; c++)
            {
                int value = (int)MathF.Round(components[c][i]) + 128;
                output[i * _numComponents + c] = (byte)Math.Clamp(value, 0, 255);
            }
        }

        return output;
    }

    /// <summary>
    /// Inverse Irreversible Color Transform (ICT) - YCbCr to RGB.
    /// </summary>
    private static void ApplyInverseICT(float[] y, float[] cb, float[] cr, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float yVal = y[i];
            float cbVal = cb[i];
            float crVal = cr[i];

            // YCbCr to RGB (ITU-R BT.601)
            float r = yVal + 1.402f * crVal;
            float g = yVal - 0.34413f * cbVal - 0.71414f * crVal;
            float b = yVal + 1.772f * cbVal;

            y[i] = r;
            cb[i] = g;
            cr[i] = b;
        }
    }

    /// <summary>
    /// Inverse Reversible Color Transform (RCT) - for lossless coding.
    /// </summary>
    private static void ApplyInverseRCT(float[] y, float[] cb, float[] cr, int count)
    {
        for (int i = 0; i < count; i++)
        {
            float yVal = y[i];
            float cbVal = cb[i];
            float crVal = cr[i];

            float g = yVal - MathF.Floor((cbVal + crVal) / 4);
            float r = crVal + g;
            float b = cbVal + g;

            y[i] = r;
            cb[i] = g;
            cr[i] = b;
        }
    }

    /// <summary>
    /// Parses the main header markers.
    /// </summary>
    private void ParseMainHeader(BinaryReader reader)
    {
        // Read SOC
        ushort marker = ReadMarker(reader);
        if (marker != Jp2kConstants.SOC)
            throw new InvalidDataException("Invalid JPEG2000 codestream - missing SOC marker");

        // Parse remaining header markers
        while (true)
        {
            marker = ReadMarker(reader);

            if (marker == Jp2kConstants.SOT)
            {
                // Start of tile - end of main header
                // Read SOT parameters but don't advance past tile data
                ParseSotMarker(reader);
                break;
            }

            switch (marker)
            {
                case Jp2kConstants.SIZ:
                    ParseSizMarker(reader);
                    break;

                case Jp2kConstants.COD:
                    ParseCodMarker(reader);
                    break;

                case Jp2kConstants.QCD:
                    ParseQcdMarker(reader);
                    break;

                case Jp2kConstants.COC:
                case Jp2kConstants.QCC:
                case Jp2kConstants.RGN:
                case Jp2kConstants.POC:
                case Jp2kConstants.TLM:
                case Jp2kConstants.PLM:
                case Jp2kConstants.CRG:
                case Jp2kConstants.COM:
                    // Skip optional markers
                    SkipMarkerSegment(reader);
                    break;

                default:
                    if ((marker & 0xFF00) == 0xFF00)
                    {
                        // Unknown marker - skip
                        SkipMarkerSegment(reader);
                    }
                    break;
            }
        }
    }

    private static ushort ReadMarker(BinaryReader reader)
    {
        byte b1 = reader.ReadByte();
        byte b2 = reader.ReadByte();
        return (ushort)((b1 << 8) | b2);
    }

    private static ushort ReadUInt16BE(BinaryReader reader)
    {
        byte b1 = reader.ReadByte();
        byte b2 = reader.ReadByte();
        return (ushort)((b1 << 8) | b2);
    }

    private static uint ReadUInt32BE(BinaryReader reader)
    {
        byte b1 = reader.ReadByte();
        byte b2 = reader.ReadByte();
        byte b3 = reader.ReadByte();
        byte b4 = reader.ReadByte();
        return (uint)((b1 << 24) | (b2 << 16) | (b3 << 8) | b4);
    }

    private static void SkipMarkerSegment(BinaryReader reader)
    {
        ushort length = ReadUInt16BE(reader);
        if (length > 2)
        {
            reader.BaseStream.Seek(length - 2, SeekOrigin.Current);
        }
    }

    private void ParseSizMarker(BinaryReader reader)
    {
        ushort length = ReadUInt16BE(reader);

        ReadUInt16BE(reader);  // Rsiz

        _width = (int)ReadUInt32BE(reader);   // Xsiz
        _height = (int)ReadUInt32BE(reader);  // Ysiz

        ReadUInt32BE(reader);  // XOsiz
        ReadUInt32BE(reader);  // YOsiz
        ReadUInt32BE(reader);  // XTsiz
        ReadUInt32BE(reader);  // YTsiz
        ReadUInt32BE(reader);  // XTOsiz
        ReadUInt32BE(reader);  // YTOsiz

        _numComponents = ReadUInt16BE(reader);

        // Skip component parameters
        for (int i = 0; i < _numComponents; i++)
        {
            reader.ReadByte();  // Ssiz
            reader.ReadByte();  // XRsiz
            reader.ReadByte();  // YRsiz
        }
    }

    private void ParseCodMarker(BinaryReader reader)
    {
        ushort length = ReadUInt16BE(reader);

        reader.ReadByte();  // Scod

        reader.ReadByte();     // Progression order
        ReadUInt16BE(reader);  // Number of layers
        _useMct = reader.ReadByte() != 0;  // MCT

        _numLevels = reader.ReadByte();  // Number of decomposition levels
        reader.ReadByte();               // Code-block width
        reader.ReadByte();               // Code-block height
        reader.ReadByte();               // Code-block style
        _lossy = reader.ReadByte() == 1; // Wavelet transform

        // Skip precinct sizes
        int remaining = length - 2 - 1 - 4 - 5;
        if (remaining > 0)
        {
            reader.BaseStream.Seek(remaining, SeekOrigin.Current);
        }
    }

    private void ParseQcdMarker(BinaryReader reader)
    {
        ushort length = ReadUInt16BE(reader);

        byte sqcd = reader.ReadByte();  // Quantization style

        int numSubbands = 3 * _numLevels + 1;
        _stepSizes = new float[_numLevels + 1, 4];

        // Read step sizes
        if ((sqcd & 0x1F) == 0)
        {
            // No quantization
            for (int i = 0; i < numSubbands; i++)
            {
                reader.ReadByte();  // SPqcd (exponent only)
            }
            // All step sizes = 1
            for (int level = 0; level <= _numLevels; level++)
            {
                for (int band = 0; band < 4; band++)
                {
                    _stepSizes[level, band] = 1.0f;
                }
            }
        }
        else
        {
            // Scalar quantization
            // LL subband first
            _stepSizes[_numLevels, Jp2kConstants.SubbandLL] = ReadStepSize(reader);

            // Then HL, LH, HH for each level
            for (int level = _numLevels - 1; level >= 0; level--)
            {
                _stepSizes[level, Jp2kConstants.SubbandHL] = ReadStepSize(reader);
                _stepSizes[level, Jp2kConstants.SubbandLH] = ReadStepSize(reader);
                _stepSizes[level, Jp2kConstants.SubbandHH] = ReadStepSize(reader);
            }
        }
    }

    private static float ReadStepSize(BinaryReader reader)
    {
        ushort value = ReadUInt16BE(reader);
        int exponent = (value >> 11) & 0x1F;
        int mantissa = value & 0x7FF;

        float step = (1.0f + mantissa / 2048.0f) * MathF.Pow(2, -exponent);
        return step;
    }

    private void ParseSotMarker(BinaryReader reader)
    {
        ushort length = ReadUInt16BE(reader);

        ReadUInt16BE(reader);  // Isot (tile index)
        ReadUInt32BE(reader);  // Psot (tile-part length)
        reader.ReadByte();     // TPsot
        reader.ReadByte();     // TNsot
    }

    private void ParseTileData(BinaryReader reader, List<Subband[]> allSubbands)
    {
        // Read SOD marker
        ushort marker = ReadMarker(reader);
        if (marker != Jp2kConstants.SOD)
            throw new InvalidDataException("Expected SOD marker");

        // Read packets for each component
        for (int c = 0; c < _numComponents; c++)
        {
            var subbands = allSubbands[c];

            for (int resolution = 0; resolution <= _numLevels; resolution++)
            {
                var packet = EbcotTier2.ReadPacket(reader, subbands, resolution);

                // Decode code-blocks
                foreach (var contrib in packet.CodeBlockContributions)
                {
                    if (contrib.Data != null && contrib.Data.Length > 0)
                    {
                        EbcotTier1.DecodeBlock(contrib.CodeBlock, contrib.Data, contrib.NewPasses);
                    }
                }
            }
        }
    }

    private Subband[] CreateSubbands(int componentIndex)
    {
        var subbands = new List<Subband>();

        int currentWidth = _width;
        int currentHeight = _height;
        int offsetX = 0;
        int offsetY = 0;

        for (int level = 0; level < _numLevels; level++)
        {
            int lowWidth = (currentWidth + 1) / 2;
            int highWidth = currentWidth / 2;
            int lowHeight = (currentHeight + 1) / 2;
            int highHeight = currentHeight / 2;

            var hl = new Subband(
                Jp2kConstants.SubbandHL, level,
                highWidth, lowHeight,
                offsetX + lowWidth, offsetY);
            hl.ComponentIndex = componentIndex;
            subbands.Add(hl);

            var lh = new Subband(
                Jp2kConstants.SubbandLH, level,
                lowWidth, highHeight,
                offsetX, offsetY + lowHeight);
            lh.ComponentIndex = componentIndex;
            subbands.Add(lh);

            var hh = new Subband(
                Jp2kConstants.SubbandHH, level,
                highWidth, highHeight,
                offsetX + lowWidth, offsetY + lowHeight);
            hh.ComponentIndex = componentIndex;
            subbands.Add(hh);

            currentWidth = lowWidth;
            currentHeight = lowHeight;
        }

        var ll = new Subband(
            Jp2kConstants.SubbandLL, _numLevels,
            currentWidth, currentHeight,
            offsetX, offsetY);
        ll.ComponentIndex = componentIndex;
        subbands.Add(ll);

        foreach (var subband in subbands)
        {
            subband.CreateCodeBlocks(64, 64);
            if (_stepSizes != null)
            {
                subband.QuantStep = _stepSizes[subband.Level, subband.Type];
            }
        }

        return subbands.ToArray();
    }

    private float[] ReconstructFromSubbands(Subband[] subbands)
    {
        var coefficients = new float[_width * _height];

        foreach (var subband in subbands)
        {
            if (subband.CodeBlocks == null) continue;

            float stepSize = subband.QuantStep;

            int blocksY = subband.CodeBlocks.GetLength(0);
            int blocksX = subband.CodeBlocks.GetLength(1);

            for (int by = 0; by < blocksY; by++)
            {
                for (int bx = 0; bx < blocksX; bx++)
                {
                    var block = subband.CodeBlocks[by, bx];
                    if (block.Coefficients == null) continue;

                    for (int y = 0; y < block.Height; y++)
                    {
                        for (int x = 0; x < block.Width; x++)
                        {
                            int globalX = block.X + x;
                            int globalY = block.Y + y;

                            if (globalX < _width && globalY < _height)
                            {
                                int magnitude = block.Coefficients[y, x];
                                bool negative = block.Signs != null && block.Signs[y, x];

                                float value = Jp2kQuantization.Dequantize(magnitude, negative, stepSize);
                                coefficients[globalY * _width + globalX] = value;
                            }
                        }
                    }
                }
            }
        }

        return coefficients;
    }
}
