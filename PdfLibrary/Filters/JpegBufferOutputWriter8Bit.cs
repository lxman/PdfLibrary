using System.Runtime.CompilerServices;
using JpegLibrary;

namespace PdfLibrary.Filters;

/// <summary>
/// Outputs decoded 8-bit JPEG blocks to a byte buffer.
/// Based on JpegLibrary example implementation.
/// </summary>
internal sealed class JpegBufferOutputWriter8Bit : JpegBlockOutputWriter
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _componentCount;
    private readonly Memory<byte> _output;

    public JpegBufferOutputWriter8Bit(int width, int height, int componentCount, Memory<byte> output)
    {
        if (output.Length < width * height * componentCount)
        {
            throw new ArgumentException("Output buffer is too small.", nameof(output));
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

        if (x >= width || y >= height)
        {
            return;
        }

        int writeWidth = Math.Min(8, width - x);
        int writeHeight = Math.Min(8, height - y);

        ref byte destinationRef = ref _output.Span[y * width * componentCount + x * componentCount + componentIndex];

        for (int destY = 0; destY < writeHeight; destY++)
        {
            ref byte destinationRowRef = ref Unsafe.Add(ref destinationRef, destY * width * componentCount);
            for (int destX = 0; destX < writeWidth; destX++)
            {
                Unsafe.Add(ref destinationRowRef, destX * componentCount) = ClampTo8Bit(Unsafe.Add(ref blockRef, destY * 8 + destX));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampTo8Bit(short value)
    {
        return (byte)Math.Clamp(value, (short)0, (short)255);
    }
}
