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
