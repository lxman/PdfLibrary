using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using JpegLibrary;

namespace PdfLibrary.Filters;

/// <summary>
/// Adapter to use JpegLibrary for JPEG decoding in PDF filters.
/// </summary>
internal static class JpegLibraryAdapter
{
    /// <summary>
    /// Decodes a JPEG image and returns detailed decode information.
    /// </summary>
    public static JpegDecodeResult DecodeWithInfo(byte[] jpegData, bool convertToRgb = true)
    {
        var decoder = new JpegDecoder();
        decoder.SetInput(jpegData);
        decoder.Identify();

        int width = decoder.Width;
        int height = decoder.Height;
        int componentCount = decoder.NumberOfComponents;
        int precision = decoder.Precision;

        // Allocate output buffer (always output as raw components, not RGB-converted)
        byte[] outputBuffer = new byte[width * height * componentCount];

        // Decode
        decoder.SetOutputWriter(new JpegBufferOutputWriter8Bit(width, height, componentCount, outputBuffer));
        decoder.Decode();

        // JpegLibrary doesn't expose Adobe marker info
        // For PDF decoding, we don't need it since we handle color conversion separately
        bool hasAdobeMarker = false;
        byte adobeColorTransform = 0;

        return new JpegDecodeResult
        {
            Data = outputBuffer,
            Width = width,
            Height = height,
            ComponentCount = componentCount,
            HasAdobeMarker = hasAdobeMarker,
            AdobeColorTransform = adobeColorTransform
        };
    }
}

/// <summary>
/// Result of decoding a JPEG image with metadata.
/// </summary>
internal class JpegDecodeResult
{
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public int Width { get; set; }
    public int Height { get; set; }
    public int ComponentCount { get; set; }
    public bool HasAdobeMarker { get; set; }
    public byte AdobeColorTransform { get; set; }
}

/// <summary>
/// Output writer for JpegLibrary that writes 8-bit decoded blocks to a byte array.
/// Based on JpegBufferOutputWriter8Bit from JpegLibrary samples.
/// </summary>
internal class JpegBufferOutputWriter8Bit : JpegBlockOutputWriter
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _componentCount;
    private readonly Memory<byte> _output;

    public JpegBufferOutputWriter8Bit(int width, int height, int componentCount, Memory<byte> output)
    {
        if (output.Length < (width * height * componentCount))
        {
            throw new ArgumentException("Destination buffer is too small.");
        }

        _width = width;
        _height = height;
        _componentCount = componentCount;
        _output = output;
    }

    public override void WriteBlock(ref short blockRef, int componentIndex, int x, int y)
    {
        int componentCount = _componentCount;
        int width = _width;
        int height = _height;

        if (x > width || y > height)
        {
            return;
        }

        int writeWidth = Math.Min(width - x, 8);
        int writeHeight = Math.Min(height - y, 8);

        ref byte destinationRef = ref MemoryMarshal.GetReference(_output.Span);
        destinationRef = ref Unsafe.Add(ref destinationRef, y * width * componentCount + x * componentCount + componentIndex);

        for (int destY = 0; destY < writeHeight; destY++)
        {
            ref byte destinationRowRef = ref Unsafe.Add(ref destinationRef, destY * width * componentCount);
            for (int destX = 0; destX < writeWidth; destX++)
            {
                Unsafe.Add(ref destinationRowRef, destX * componentCount) = ClampTo8Bit(Unsafe.Add(ref blockRef, destX));
            }
            blockRef = ref Unsafe.Add(ref blockRef, 8);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampTo8Bit(short input)
    {
        return (byte)Math.Clamp(input, (short)0, (short)255);
    }
}
