using System;
using System.IO;

namespace ImageLibrary.Jpeg;

/// <summary>
/// Complete JPEG decoder that processes a JPEG file through all stages:
/// 1. Marker parsing (JpegReader)
/// 2. Huffman table building (HuffmanTable)
/// 3. Entropy decoding (EntropyDecoder)
/// 4. Dequantization (Dequantizer)
/// 5. Inverse DCT (InverseDct)
/// 6. Color conversion (ColorConverter)
/// </summary>
public class JpegDecoder
{
    private readonly byte[] _data;

    /// <summary>
    /// Creates a decoder for the given JPEG data.
    /// </summary>
    public JpegDecoder(byte[] data)
    {
        _data = data;
    }

    /// <summary>
    /// Creates a decoder from a stream.
    /// </summary>
    public JpegDecoder(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _data = ms.ToArray();
    }

    /// <summary>
    /// Creates a decoder from a file path.
    /// </summary>
    public JpegDecoder(string path)
    {
        _data = File.ReadAllBytes(path);
    }

    /// <summary>
    /// Decodes the JPEG image and returns the RGB pixel data.
    /// </summary>
    /// <returns>A DecodedImage containing the width, height, and RGB pixel data</returns>
    public DecodedImage Decode()
    {
        // Stage 1: Parse markers
        var reader = new JpegReader(_data);
        JpegFrame frame = reader.ReadFrame();

        // Stage 2 & 3: Entropy decode (includes Huffman table building)
        var entropyDecoder = new EntropyDecoder(frame, _data);
        short[][][] dctCoefficients = entropyDecoder.DecodeAllBlocks();

        // Stage 4: Dequantize
        var dequantizer = new Dequantizer(frame);
        int[][][] dequantized = dequantizer.DequantizeAll(dctCoefficients);

        // Stage 5: Inverse DCT
        byte[][][] pixels = InverseDct.TransformAll(dequantized);

        // Stage 6: Color conversion and assembly
        var colorConverter = new ColorConverter(frame);
        byte[] rgb = colorConverter.AssembleImage(pixels);

        return new DecodedImage(frame.Width, frame.Height, rgb);
    }

    /// <summary>
    /// Decodes the JPEG image and returns raw component data before color conversion.
    /// This is useful for applications that need to perform custom color space conversions
    /// (e.g., PDF rendering with CMYK/YCCK support).
    /// </summary>
    /// <returns>A RawJpegData containing raw component pixels and metadata</returns>
    public RawJpegData DecodeRaw()
    {
        // Stage 1: Parse markers
        var reader = new JpegReader(_data);
        JpegFrame frame = reader.ReadFrame();

        // Stage 2 & 3: Entropy decode (includes Huffman table building)
        var entropyDecoder = new EntropyDecoder(frame, _data);
        short[][][] dctCoefficients = entropyDecoder.DecodeAllBlocks();

        // Stage 4: Dequantize
        var dequantizer = new Dequantizer(frame);
        int[][][] dequantized = dequantizer.DequantizeAll(dctCoefficients);

        // Stage 5: Inverse DCT (get raw component pixels)
        byte[][][] componentBlocks = InverseDct.TransformAll(dequantized);

        // Assemble component data into interleaved format
        byte[] interleavedData = AssembleInterleavedComponents(componentBlocks, frame);

        return new RawJpegData(
            frame.Width,
            frame.Height,
            frame.ComponentCount,
            frame.Precision,
            interleavedData,
            frame.HasAdobeMarker,
            frame.AdobeColorTransform
        );
    }

    /// <summary>
    /// Assembles component blocks into interleaved component data.
    /// For 1 component: Y, Y, Y, ...
    /// For 3 components: Y, Cb, Cr, Y, Cb, Cr, ...
    /// For 4 components: C, M, Y, K, C, M, Y, K, ... (or Y, Cb, Cr, K for YCCK)
    /// </summary>
    private static byte[] AssembleInterleavedComponents(byte[][][] componentBlocks, JpegFrame frame)
    {
        int width = frame.Width;
        int height = frame.Height;
        int componentCount = frame.ComponentCount;
        var result = new byte[width * height * componentCount];

        // Calculate MCU (Minimum Coded Unit) dimensions
        // Each MCU is maxH×maxV blocks, where each block is 8×8 pixels
        int mcuWidth = frame.MaxHorizontalSamplingFactor * 8;
        int mcuHeight = frame.MaxVerticalSamplingFactor * 8;
        int mcusHorizontal = (width + mcuWidth - 1) / mcuWidth;
        int mcusVertical = (height + mcuHeight - 1) / mcuHeight;

        // Calculate blocks per row for each component based on MCU layout
        // For interleaved JPEGs, blocks are organized in MCU scan order
        int[] blocksPerRow = new int[componentCount];
        for (var c = 0; c < componentCount; c++)
        {
            JpegComponent comp = frame.Components[c];
            blocksPerRow[c] = mcusHorizontal * comp.HorizontalSamplingFactor;
        }

        // Get sampling ratios (for chroma subsampling)
        int[] hRatios = new int[componentCount];
        int[] vRatios = new int[componentCount];
        for (var c = 0; c < componentCount; c++)
        {
            hRatios[c] = frame.Components[0].HorizontalSamplingFactor / frame.Components[c].HorizontalSamplingFactor;
            vRatios[c] = frame.Components[0].VerticalSamplingFactor / frame.Components[c].VerticalSamplingFactor;
        }

        // Assemble pixels in interleaved format
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int pixelOffset = (y * width + x) * componentCount;

                for (var c = 0; c < componentCount; c++)
                {
                    // Calculate which block this pixel belongs to for this component
                    int compX = x / hRatios[c];
                    int compY = y / vRatios[c];
                    int blockX = compX / 8;
                    int blockY = compY / 8;
                    int pixelX = compX % 8;
                    int pixelY = compY % 8;

                    int blockIndex = blockY * blocksPerRow[c] + blockX;
                    byte componentValue = (blockIndex < componentBlocks[c].Length)
                        ? componentBlocks[c][blockIndex][pixelY * 8 + pixelX]
                        : (byte)128; // Default to neutral value if out of bounds

                    result[pixelOffset + c] = componentValue;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Decodes a JPEG file from disk.
    /// </summary>
    public static DecodedImage DecodeFile(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        var decoder = new JpegDecoder(data);
        return decoder.Decode();
    }
}

/// <summary>
/// Represents a decoded JPEG image.
/// </summary>
public class DecodedImage
{
    /// <summary>
    /// Image width in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Image height in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// RGB pixel data (R, G, B, R, G, B, ...).
    /// Length is Width * Height * 3.
    /// </summary>
    public byte[] RgbData { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DecodedImage"/> class with the specified dimensions and RGB pixel data.
    /// </summary>
    /// <param name="width">The width of the image in pixels.</param>
    /// <param name="height">The height of the image in pixels.</param>
    /// <param name="rgbData">The RGB pixel data (R, G, B, R, G, B, ...).</param>
    public DecodedImage(int width, int height, byte[] rgbData)
    {
        Width = width;
        Height = height;
        RgbData = rgbData;
    }

    /// <summary>
    /// Gets the RGB values at the specified pixel position.
    /// </summary>
    public (byte R, byte G, byte B) GetPixel(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            throw new ArgumentOutOfRangeException($"Pixel position ({x}, {y}) is outside image bounds ({Width}x{Height})");
        }

        int offset = (y * Width + x) * 3;
        return (RgbData[offset], RgbData[offset + 1], RgbData[offset + 2]);
    }

    /// <summary>
    /// Gets the grayscale value at the specified pixel position.
    /// For grayscale images, all RGB values are equal.
    /// </summary>
    public byte GetGrayscale(int x, int y)
    {
        (byte r, _, _) = GetPixel(x, y);
        return r;
    }
}

/// <summary>
/// Represents raw JPEG component data before color conversion.
/// Used for custom color space handling (e.g., CMYK/YCCK in PDF).
/// </summary>
public class RawJpegData
{
    /// <summary>
    /// Image width in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Image height in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Number of components (1=grayscale, 3=YCbCr, 4=CMYK/YCCK).
    /// </summary>
    public int ComponentCount { get; }

    /// <summary>
    /// Sample precision in bits (typically 8).
    /// </summary>
    public int Precision { get; }

    /// <summary>
    /// Raw component data in interleaved format.
    /// For 1 component: Y, Y, Y, ...
    /// For 3 components: Y, Cb, Cr, Y, Cb, Cr, ...
    /// For 4 components: C, M, Y, K, C, M, Y, K, ... (or Y, Cb, Cr, K for YCCK)
    /// Length is Width * Height * ComponentCount.
    /// </summary>
    public byte[] ComponentData { get; }

    /// <summary>
    /// True if an Adobe APP14 marker was found in the file.
    /// </summary>
    public bool HasAdobeMarker { get; }

    /// <summary>
    /// Adobe color transform value (0=unknown/CMYK, 1=YCbCr, 2=YCCK).
    /// Only valid if HasAdobeMarker is true.
    /// </summary>
    public byte AdobeColorTransform { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RawJpegData"/> class.
    /// </summary>
    public RawJpegData(
        int width,
        int height,
        int componentCount,
        int precision,
        byte[] componentData,
        bool hasAdobeMarker,
        byte adobeColorTransform)
    {
        Width = width;
        Height = height;
        ComponentCount = componentCount;
        Precision = precision;
        ComponentData = componentData;
        HasAdobeMarker = hasAdobeMarker;
        AdobeColorTransform = adobeColorTransform;
    }
}
