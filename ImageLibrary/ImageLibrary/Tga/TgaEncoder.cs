using System;
using System.IO;

namespace ImageLibrary.Tga;

/// <summary>
/// Encodes images to TGA format.
/// </summary>
public static class TgaEncoder
{
    /// <summary>
    /// Encode an image to TGA format.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="bitsPerPixel">Output bits per pixel (24 or 32). Default is 32.</param>
    /// <param name="useRle">Use RLE compression. Default is false.</param>
    /// <returns>The encoded TGA data.</returns>
    public static byte[] Encode(TgaImage image, int bitsPerPixel = 32, bool useRle = false)
    {
        if (bitsPerPixel != 24 && bitsPerPixel != 32)
            throw new ArgumentException("Only 24 and 32 bits per pixel are supported", nameof(bitsPerPixel));

        int bytesPerPixel = bitsPerPixel / 8;
        byte[] pixelData;

        if (useRle)
        {
            pixelData = EncodeRle(image, bytesPerPixel);
        }
        else
        {
            pixelData = EncodeUncompressed(image, bytesPerPixel);
        }

        // Header (18 bytes) + pixel data
        var result = new byte[TgaHeader.Size + pixelData.Length];

        var offset = 0;

        // ID Length
        result[offset++] = 0;

        // Color Map Type
        result[offset++] = 0;

        // Image Type
        result[offset++] = useRle ? (byte)TgaImageType.RleTrueColor : (byte)TgaImageType.TrueColor;

        // Color Map Specification (5 bytes, all zero)
        result[offset++] = 0; // First entry index (low)
        result[offset++] = 0; // First entry index (high)
        result[offset++] = 0; // Length (low)
        result[offset++] = 0; // Length (high)
        result[offset++] = 0; // Entry size

        // X Origin
        result[offset++] = 0;
        result[offset++] = 0;

        // Y Origin
        result[offset++] = 0;
        result[offset++] = 0;

        // Width
        result[offset++] = (byte)(image.Width & 0xFF);
        result[offset++] = (byte)((image.Width >> 8) & 0xFF);

        // Height
        result[offset++] = (byte)(image.Height & 0xFF);
        result[offset++] = (byte)((image.Height >> 8) & 0xFF);

        // Pixel Depth
        result[offset++] = (byte)bitsPerPixel;

        // Image Descriptor: top-to-bottom, alpha bits
        byte descriptor = 0x20; // Bit 5 = top-to-bottom
        if (bitsPerPixel == 32)
            descriptor |= 8; // 8 alpha bits
        result[offset++] = descriptor;

        // Copy pixel data
        Array.Copy(pixelData, 0, result, offset, pixelData.Length);

        return result;
    }

    /// <summary>
    /// Encode a TGA image to a stream.
    /// </summary>
    public static void Encode(TgaImage image, Stream stream, int bitsPerPixel = 32, bool useRle = false)
    {
        byte[] data = Encode(image, bitsPerPixel, useRle);
        stream.Write(data, 0, data.Length);
    }

    /// <summary>
    /// Encode a TGA image to a file.
    /// </summary>
    public static void Encode(TgaImage image, string path, int bitsPerPixel = 32, bool useRle = false)
    {
        File.WriteAllBytes(path, Encode(image, bitsPerPixel, useRle));
    }

    private static byte[] EncodeUncompressed(TgaImage image, int bytesPerPixel)
    {
        var result = new byte[image.Width * image.Height * bytesPerPixel];
        var destOffset = 0;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                int srcOffset = (y * image.Width + x) * 4;

                // TGA stores BGR(A)
                result[destOffset++] = image.PixelData[srcOffset];     // B
                result[destOffset++] = image.PixelData[srcOffset + 1]; // G
                result[destOffset++] = image.PixelData[srcOffset + 2]; // R
                if (bytesPerPixel == 4)
                    result[destOffset++] = image.PixelData[srcOffset + 3]; // A
            }
        }

        return result;
    }

    private static byte[] EncodeRle(TgaImage image, int bytesPerPixel)
    {
        // Worst case: each pixel needs its own packet header
        using var ms = new MemoryStream(image.Width * image.Height * (bytesPerPixel + 1));

        int totalPixels = image.Width * image.Height;
        var pixelIndex = 0;

        while (pixelIndex < totalPixels)
        {
            // Look ahead to determine if we should use run-length or raw packet
            var runLength = 1;
            int srcOffset = pixelIndex * 4;
            byte b = image.PixelData[srcOffset];
            byte g = image.PixelData[srcOffset + 1];
            byte r = image.PixelData[srcOffset + 2];
            byte a = image.PixelData[srcOffset + 3];

            // Check for run of identical pixels
            while (runLength < 128 && pixelIndex + runLength < totalPixels)
            {
                int nextOffset = (pixelIndex + runLength) * 4;
                if (image.PixelData[nextOffset] == b &&
                    image.PixelData[nextOffset + 1] == g &&
                    image.PixelData[nextOffset + 2] == r &&
                    (bytesPerPixel == 3 || image.PixelData[nextOffset + 3] == a))
                {
                    runLength++;
                }
                else
                {
                    break;
                }
            }

            if (runLength > 1)
            {
                // Run-length packet
                ms.WriteByte((byte)(0x80 | (runLength - 1)));
                ms.WriteByte(b);
                ms.WriteByte(g);
                ms.WriteByte(r);
                if (bytesPerPixel == 4)
                    ms.WriteByte(a);
                pixelIndex += runLength;
            }
            else
            {
                // Raw packet - find how many non-repeating pixels
                var rawCount = 1;
                while (rawCount < 128 && pixelIndex + rawCount < totalPixels)
                {
                    int currOffset = (pixelIndex + rawCount) * 4;
                    int prevOffset = (pixelIndex + rawCount - 1) * 4;

                    // Check if current pixel is different from previous
                    bool isDifferent =
                        image.PixelData[currOffset] != image.PixelData[prevOffset] ||
                        image.PixelData[currOffset + 1] != image.PixelData[prevOffset + 1] ||
                        image.PixelData[currOffset + 2] != image.PixelData[prevOffset + 2] ||
                        (bytesPerPixel == 4 && image.PixelData[currOffset + 3] != image.PixelData[prevOffset + 3]);

                    if (!isDifferent)
                    {
                        // Found a potential run, back up one and stop raw packet
                        rawCount--;
                        break;
                    }

                    rawCount++;
                }

                if (rawCount == 0) rawCount = 1;

                ms.WriteByte((byte)(rawCount - 1));
                for (var i = 0; i < rawCount; i++)
                {
                    int off = (pixelIndex + i) * 4;
                    ms.WriteByte(image.PixelData[off]);     // B
                    ms.WriteByte(image.PixelData[off + 1]); // G
                    ms.WriteByte(image.PixelData[off + 2]); // R
                    if (bytesPerPixel == 4)
                        ms.WriteByte(image.PixelData[off + 3]); // A
                }
                pixelIndex += rawCount;
            }
        }

        return ms.ToArray();
    }
}
