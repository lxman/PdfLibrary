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
        if (x >= _width || y >= _height)
        {
            return;
        }

        var writeWidth = Math.Min(8, _width - x);
        var writeHeight = Math.Min(8, _height - y);

        ref var destinationRef = ref _output.Span[y * _width * _componentCount + x * _componentCount + componentIndex];

        for (var destY = 0; destY < writeHeight; destY++)
        {
            ref var destinationRowRef = ref Unsafe.Add(ref destinationRef, destY * _width * _componentCount);
            for (var destX = 0; destX < writeWidth; destX++)
            {
                Unsafe.Add(ref destinationRowRef, destX * _componentCount) = ClampTo8Bit(Unsafe.Add(ref blockRef, destY * 8 + destX));
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ClampTo8Bit(short value)
    {
        return (byte)Math.Clamp(value, (short)0, (short)255);
    }
}
