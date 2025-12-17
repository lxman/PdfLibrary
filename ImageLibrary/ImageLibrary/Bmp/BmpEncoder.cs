using System;
using System.Buffers.Binary;
using System.IO;

namespace ImageLibrary.Bmp;

/// <summary>
/// Encodes BMP image files.
/// </summary>
public static class BmpEncoder
{
    /// <summary>
    /// Encode a BMP image to a byte array.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="bitsPerPixel">Output bits per pixel (24 or 32).</param>
    public static byte[] Encode(BmpImage image, int bitsPerPixel = 24)
    {
        if (bitsPerPixel != 24 && bitsPerPixel != 32)
            throw new ArgumentException("Only 24-bit and 32-bit output supported", nameof(bitsPerPixel));

        int bytesPerPixel = bitsPerPixel / 8;
        int stride = ((image.Width * bitsPerPixel + 31) / 32) * 4;
        int pixelDataSize = stride * image.Height;

        int fileSize = BitmapFileHeader.Size + BitmapInfoHeader.Size + pixelDataSize;
        var output = new byte[fileSize];

        // Write file header
        WriteFileHeader(output, (uint)fileSize, BitmapFileHeader.Size + BitmapInfoHeader.Size);

        // Write info header (bottom-up format)
        WriteInfoHeader(output.AsSpan(BitmapFileHeader.Size), image, bitsPerPixel, (uint)pixelDataSize);

        // Write pixel data (convert from top-down BGRA to bottom-up BGR/BGRA)
        WritePixelData(output.AsSpan(BitmapFileHeader.Size + BitmapInfoHeader.Size), image, bitsPerPixel, stride);

        return output;
    }

    /// <summary>
    /// Encode a BMP image to a stream.
    /// </summary>
    public static void Encode(BmpImage image, Stream stream, int bitsPerPixel = 24)
    {
        byte[] data = Encode(image, bitsPerPixel);
        stream.Write(data, 0, data.Length);
    }

    /// <summary>
    /// Encode a BMP image to a file.
    /// </summary>
    public static void Encode(BmpImage image, string path, int bitsPerPixel = 24)
    {
        File.WriteAllBytes(path, Encode(image, bitsPerPixel));
    }

    private static void WriteFileHeader(Span<byte> output, uint fileSize, uint pixelDataOffset)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(output, BitmapFileHeader.BmpSignature);
        BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(2), fileSize);
        BinaryPrimitives.WriteUInt16LittleEndian(output.Slice(6), 0);  // Reserved1
        BinaryPrimitives.WriteUInt16LittleEndian(output.Slice(8), 0);  // Reserved2
        BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(10), pixelDataOffset);
    }

    private static void WriteInfoHeader(Span<byte> output, BmpImage image, int bitsPerPixel, uint imageSize)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(output, BitmapInfoHeader.Size);           // biSize
        BinaryPrimitives.WriteInt32LittleEndian(output.Slice(4), image.Width);             // biWidth
        BinaryPrimitives.WriteInt32LittleEndian(output.Slice(8), image.Height);            // biHeight (positive = bottom-up)
        BinaryPrimitives.WriteUInt16LittleEndian(output.Slice(12), 1);                     // biPlanes
        BinaryPrimitives.WriteUInt16LittleEndian(output.Slice(14), (ushort)bitsPerPixel);  // biBitCount
        BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(16), (uint)BmpCompression.Rgb); // biCompression
        BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(20), imageSize);             // biSizeImage
        BinaryPrimitives.WriteInt32LittleEndian(output.Slice(24), image.XPixelsPerMeter);  // biXPelsPerMeter
        BinaryPrimitives.WriteInt32LittleEndian(output.Slice(28), image.YPixelsPerMeter);  // biYPelsPerMeter
        BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(32), 0);                     // biClrUsed
        BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(36), 0);                     // biClrImportant
    }

    private static void WritePixelData(Span<byte> output, BmpImage image, int bitsPerPixel, int stride)
    {
        int bytesPerPixel = bitsPerPixel / 8;

        // BMP stores rows bottom-up, our image is top-down
        for (var srcY = 0; srcY < image.Height; srcY++)
        {
            int dstY = image.Height - 1 - srcY;
            int srcRowOffset = srcY * image.Width * 4;
            int dstRowOffset = dstY * stride;

            for (var x = 0; x < image.Width; x++)
            {
                int srcOffset = srcRowOffset + x * 4;
                int dstOffset = dstRowOffset + x * bytesPerPixel;

                // Source is BGRA
                output[dstOffset] = image.PixelData[srcOffset];         // Blue
                output[dstOffset + 1] = image.PixelData[srcOffset + 1]; // Green
                output[dstOffset + 2] = image.PixelData[srcOffset + 2]; // Red

                if (bitsPerPixel == 32)
                {
                    output[dstOffset + 3] = image.PixelData[srcOffset + 3]; // Alpha
                }
            }

            // Padding bytes are already 0 (array initialized to 0)
        }
    }
}