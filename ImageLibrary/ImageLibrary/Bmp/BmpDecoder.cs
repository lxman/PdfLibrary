using System;
using System.Buffers.Binary;
using System.IO;

namespace ImageLibrary.Bmp;

/// <summary>
/// Decodes BMP image files.
/// </summary>
public static class BmpDecoder
{
    /// <summary>
    /// Decode a BMP image from a byte array.
    /// </summary>
    public static BmpImage Decode(byte[] data)
    {
        return Decode(data.AsSpan());
    }

    /// <summary>
    /// Decode a BMP image from a span.
    /// </summary>
    public static BmpImage Decode(ReadOnlySpan<byte> data)
    {
        try
        {
            if (data.Length < BitmapFileHeader.Size + BitmapInfoHeader.Size)
                throw new BmpException("Data too small to contain a valid BMP file");

            // Read the file header
            BitmapFileHeader fileHeader = ReadFileHeader(data);

            if (fileHeader.Type != BitmapFileHeader.BmpSignature)
                throw new BmpException($"Invalid BMP signature: 0x{fileHeader.Type:X4}");

            // Read info header
            BitmapInfoHeader infoHeader = ReadInfoHeader(data.Slice(BitmapFileHeader.Size));

            if (infoHeader.Planes != 1)
                throw new BmpException($"Invalid plane count: {infoHeader.Planes}");

            if (infoHeader.Width <= 0)
                throw new BmpException($"Invalid width: {infoHeader.Width}");

            if (infoHeader.AbsoluteHeight == 0)
                throw new BmpException("Invalid height: 0");

            // Validate dimensions won't cause overflow (limit to 32K x 32K)
            if (infoHeader.Width > 32768 || infoHeader.AbsoluteHeight > 32768)
                throw new BmpException($"Image dimensions too large: {infoHeader.Width}x{infoHeader.AbsoluteHeight}");

            // Validate pixel data offset
            if (fileHeader.PixelDataOffset > data.Length)
                throw new BmpException("Pixel data offset beyond end of file");

            // Read color palette if present
            RgbQuad[]? palette = null;
            if (infoHeader.BitsPerPixel <= 8)
            {
                int paletteSize = infoHeader.ColorsUsed > 0
                    ? (int)infoHeader.ColorsUsed
                    : 1 << infoHeader.BitsPerPixel;

                // Validate palette size
                if (paletteSize > 256)
                    throw new BmpException($"Invalid palette size: {paletteSize}");

                int paletteOffset = BitmapFileHeader.Size + (int)infoHeader.HeaderSize;
                int paletteBytes = paletteSize * RgbQuad.Size;
                if (paletteOffset + paletteBytes > data.Length)
                    throw new BmpException("Palette extends beyond end of file");

                palette = ReadPalette(data.Slice(paletteOffset), paletteSize);
            }

            // Read pixel data
            ReadOnlySpan<byte> pixelData = data.Slice((int)fileHeader.PixelDataOffset);

            return DecodePixels(infoHeader, palette, pixelData);
        }
        catch (BmpException)
        {
            throw;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new BmpException($"Invalid data: {ex.Message}", ex);
        }
        catch (OverflowException ex)
        {
            throw new BmpException($"Numeric overflow (image too large?): {ex.Message}", ex);
        }
        catch (IndexOutOfRangeException ex)
        {
            throw new BmpException($"Data truncated or corrupted: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new BmpException($"Failed to decode BMP: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Decode a BMP image from a stream.
    /// </summary>
    public static BmpImage Decode(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Decode(ms.ToArray());
    }

    /// <summary>
    /// Decode a BMP image from a file.
    /// </summary>
    public static BmpImage Decode(string path)
    {
        return Decode(File.ReadAllBytes(path));
    }

    private static BitmapFileHeader ReadFileHeader(ReadOnlySpan<byte> data)
    {
        return new BitmapFileHeader(
            BinaryPrimitives.ReadUInt16LittleEndian(data),
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(2)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(6)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(10))
        );
    }

    private static BitmapInfoHeader ReadInfoHeader(ReadOnlySpan<byte> data)
    {
        uint headerSize = BinaryPrimitives.ReadUInt32LittleEndian(data);

        // Support BITMAPINFOHEADER (40), BITMAPV4HEADER (108), BITMAPV5HEADER (124)
        if (headerSize < BitmapInfoHeader.Size)
            throw new BmpException($"Unsupported header size: {headerSize}");

        return new BitmapInfoHeader(
            headerSize,
            BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4)),
            BinaryPrimitives.ReadInt32LittleEndian(data.Slice(8)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(12)),
            BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(14)),
            (BmpCompression)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(16)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(20)),
            BinaryPrimitives.ReadInt32LittleEndian(data.Slice(24)),
            BinaryPrimitives.ReadInt32LittleEndian(data.Slice(28)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(32)),
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(36))
        );
    }

    private static RgbQuad[] ReadPalette(ReadOnlySpan<byte> data, int count)
    {
        var palette = new RgbQuad[count];
        for (var i = 0; i < count; i++)
        {
            int offset = i * RgbQuad.Size;
            palette[i] = new RgbQuad(
                data[offset],
                data[offset + 1],
                data[offset + 2],
                data[offset + 3]
            );
        }
        return palette;
    }

    private static BmpImage DecodePixels(BitmapInfoHeader header, RgbQuad[]? palette, ReadOnlySpan<byte> pixelData)
    {
        int width = header.Width;
        int height = header.AbsoluteHeight;
        bool bottomUp = !header.IsTopDown;

        // Output is always 32-bit BGRA, top-down
        var output = new byte[width * height * 4];

        switch (header.Compression)
        {
            case BmpCompression.Rgb:
                DecodeRgb(header, palette, pixelData, output, bottomUp);
                break;

            case BmpCompression.BitFields:
                DecodeBitFields(header, pixelData, output, bottomUp);
                break;

            case BmpCompression.Rle8:
                if (header.BitsPerPixel != 8 || palette == null)
                    throw new BmpException("RLE8 compression requires 8-bit palette image");
                DecodeRle8(header, palette, pixelData, output, bottomUp);
                break;

            case BmpCompression.Rle4:
                if (header.BitsPerPixel != 4 || palette == null)
                    throw new BmpException("RLE4 compression requires 4-bit palette image");
                DecodeRle4(header, palette, pixelData, output, bottomUp);
                break;

            default:
                throw new BmpException($"Unsupported compression: {header.Compression}");
        }

        var image = new BmpImage(width, height, header.BitsPerPixel, output)
        {
            XPixelsPerMeter = header.XPixelsPerMeter,
            YPixelsPerMeter = header.YPixelsPerMeter
        };

        return image;
    }

    private static void DecodeRgb(BitmapInfoHeader header, RgbQuad[]? palette, ReadOnlySpan<byte> pixelData, byte[] output, bool bottomUp)
    {
        int width = header.Width;
        int height = header.AbsoluteHeight;
        int stride = header.Stride;

        for (var srcY = 0; srcY < height; srcY++)
        {
            int dstY = bottomUp ? (height - 1 - srcY) : srcY;
            int srcRowOffset = srcY * stride;
            int dstRowOffset = dstY * width * 4;

            switch (header.BitsPerPixel)
            {
                case 1:
                    Decode1Bpp(pixelData.Slice(srcRowOffset), palette!, output.AsSpan(dstRowOffset), width);
                    break;
                case 4:
                    Decode4Bpp(pixelData.Slice(srcRowOffset), palette!, output.AsSpan(dstRowOffset), width);
                    break;
                case 8:
                    Decode8Bpp(pixelData.Slice(srcRowOffset), palette!, output.AsSpan(dstRowOffset), width);
                    break;
                case 16:
                    Decode16Bpp(pixelData.Slice(srcRowOffset), output.AsSpan(dstRowOffset), width);
                    break;
                case 24:
                    Decode24Bpp(pixelData.Slice(srcRowOffset), output.AsSpan(dstRowOffset), width);
                    break;
                case 32:
                    Decode32Bpp(pixelData.Slice(srcRowOffset), output.AsSpan(dstRowOffset), width);
                    break;
                default:
                    throw new BmpException($"Unsupported bit depth: {header.BitsPerPixel}");
            }
        }
    }

    private static void Decode1Bpp(ReadOnlySpan<byte> src, RgbQuad[] palette, Span<byte> dst, int width)
    {
        for (var x = 0; x < width; x++)
        {
            int byteIndex = x / 8;
            if (byteIndex >= src.Length) break;
            int bitIndex = 7 - (x % 8);
            int colorIndex = (src[byteIndex] >> bitIndex) & 1;

            RgbQuad color = colorIndex < palette.Length ? palette[colorIndex] : default;
            int dstOffset = x * 4;
            dst[dstOffset] = color.Blue;
            dst[dstOffset + 1] = color.Green;
            dst[dstOffset + 2] = color.Red;
            dst[dstOffset + 3] = 255;
        }
    }

    private static void Decode4Bpp(ReadOnlySpan<byte> src, RgbQuad[] palette, Span<byte> dst, int width)
    {
        for (var x = 0; x < width; x++)
        {
            int byteIndex = x / 2;
            if (byteIndex >= src.Length) break;
            int colorIndex = (x % 2 == 0)
                ? (src[byteIndex] >> 4) & 0x0F
                : src[byteIndex] & 0x0F;

            RgbQuad color = colorIndex < palette.Length ? palette[colorIndex] : default;
            int dstOffset = x * 4;
            dst[dstOffset] = color.Blue;
            dst[dstOffset + 1] = color.Green;
            dst[dstOffset + 2] = color.Red;
            dst[dstOffset + 3] = 255;
        }
    }

    private static void Decode8Bpp(ReadOnlySpan<byte> src, RgbQuad[] palette, Span<byte> dst, int width)
    {
        for (var x = 0; x < width; x++)
        {
            if (x >= src.Length) break;
            int colorIndex = src[x];
            RgbQuad color = colorIndex < palette.Length ? palette[colorIndex] : default;
            int dstOffset = x * 4;
            dst[dstOffset] = color.Blue;
            dst[dstOffset + 1] = color.Green;
            dst[dstOffset + 2] = color.Red;
            dst[dstOffset + 3] = 255;
        }
    }

    private static void Decode16Bpp(ReadOnlySpan<byte> src, Span<byte> dst, int width)
    {
        // Default 16-bit format: 5-5-5 (X1R5G5B5)
        for (var x = 0; x < width; x++)
        {
            ushort pixel = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(x * 2));

            var b = (byte)((pixel & 0x001F) << 3);
            var g = (byte)(((pixel >> 5) & 0x001F) << 3);
            var r = (byte)(((pixel >> 10) & 0x001F) << 3);

            int dstOffset = x * 4;
            dst[dstOffset] = b;
            dst[dstOffset + 1] = g;
            dst[dstOffset + 2] = r;
            dst[dstOffset + 3] = 255;
        }
    }

    private static void Decode24Bpp(ReadOnlySpan<byte> src, Span<byte> dst, int width)
    {
        for (var x = 0; x < width; x++)
        {
            int srcOffset = x * 3;
            int dstOffset = x * 4;

            dst[dstOffset] = src[srcOffset];         // Blue
            dst[dstOffset + 1] = src[srcOffset + 1]; // Green
            dst[dstOffset + 2] = src[srcOffset + 2]; // Red
            dst[dstOffset + 3] = 255;                // Alpha
        }
    }

    private static void Decode32Bpp(ReadOnlySpan<byte> src, Span<byte> dst, int width)
    {
        for (var x = 0; x < width; x++)
        {
            int srcOffset = x * 4;
            int dstOffset = x * 4;

            dst[dstOffset] = src[srcOffset];         // Blue
            dst[dstOffset + 1] = src[srcOffset + 1]; // Green
            dst[dstOffset + 2] = src[srcOffset + 2]; // Red
            dst[dstOffset + 3] = src[srcOffset + 3]; // Alpha (or ignored)
        }
    }

    private static void DecodeBitFields(BitmapInfoHeader header, ReadOnlySpan<byte> pixelData, byte[] output, bool bottomUp)
    {
        // For simplicity, assume standard masks for 16-bit (5-6-5) or 32-bit (8-8-8-8)
        // A full implementation would read the masks from the header
        DecodeRgb(header, null, pixelData, output, bottomUp);
    }

    private static void DecodeRle8(BitmapInfoHeader header, RgbQuad[] palette, ReadOnlySpan<byte> data, byte[] output, bool bottomUp)
    {
        int width = header.Width;
        int height = header.AbsoluteHeight;
        int x = 0, y = 0;
        var i = 0;

        while (i < data.Length && y < height)
        {
            byte first = data[i++];
            if (i >= data.Length) break;
            byte second = data[i++];

            if (first > 0)
            {
                // Encoded run: first pixels of color second
                RgbQuad color = palette[second];
                for (var j = 0; j < first && x < width; j++, x++)
                {
                    int dstY = bottomUp ? (height - 1 - y) : y;
                    int offset = (dstY * width + x) * 4;
                    output[offset] = color.Blue;
                    output[offset + 1] = color.Green;
                    output[offset + 2] = color.Red;
                    output[offset + 3] = 255;
                }
            }
            else
            {
                // Escape sequence
                switch (second)
                {
                    case 0: // End of line
                        x = 0;
                        y++;
                        break;
                    case 1: // End of bitmap
                        return;
                    case 2: // Delta
                        if (i + 1 >= data.Length) return;
                        x += data[i++];
                        y += data[i++];
                        break;
                    default: // Absolute mode: second literal pixels follow
                        for (var j = 0; j < second && x < width; j++, x++)
                        {
                            if (i >= data.Length) return;
                            RgbQuad color = palette[data[i++]];
                            int dstY = bottomUp ? (height - 1 - y) : y;
                            int offset = (dstY * width + x) * 4;
                            output[offset] = color.Blue;
                            output[offset + 1] = color.Green;
                            output[offset + 2] = color.Red;
                            output[offset + 3] = 255;
                        }
                        // Pad to word boundary
                        if (second % 2 == 1) i++;
                        break;
                }
            }
        }
    }

    private static void DecodeRle4(BitmapInfoHeader header, RgbQuad[] palette, ReadOnlySpan<byte> data, byte[] output, bool bottomUp)
    {
        int width = header.Width;
        int height = header.AbsoluteHeight;
        int x = 0, y = 0;
        var i = 0;

        while (i < data.Length && y < height)
        {
            byte first = data[i++];
            if (i >= data.Length) break;
            byte second = data[i++];

            if (first > 0)
            {
                // Encoded run: first pixels alternating between two colors
                int color1Index = (second >> 4) & 0x0F;
                int color2Index = second & 0x0F;

                for (var j = 0; j < first && x < width; j++, x++)
                {
                    RgbQuad color = palette[j % 2 == 0 ? color1Index : color2Index];
                    int dstY = bottomUp ? (height - 1 - y) : y;
                    int offset = (dstY * width + x) * 4;
                    output[offset] = color.Blue;
                    output[offset + 1] = color.Green;
                    output[offset + 2] = color.Red;
                    output[offset + 3] = 255;
                }
            }
            else
            {
                // Escape sequence
                switch (second)
                {
                    case 0: // End of line
                        x = 0;
                        y++;
                        break;
                    case 1: // End of bitmap
                        return;
                    case 2: // Delta
                        if (i + 1 >= data.Length) return;
                        x += data[i++];
                        y += data[i++];
                        break;
                    default: // Absolute mode
                        int bytesNeeded = (second + 1) / 2;
                        for (var j = 0; j < second && x < width; j++, x++)
                        {
                            int byteIndex = j / 2;
                            if (i + byteIndex >= data.Length) return;
                            byte b = data[i + byteIndex];
                            int colorIndex = (j % 2 == 0) ? (b >> 4) & 0x0F : b & 0x0F;

                            RgbQuad color = palette[colorIndex];
                            int dstY = bottomUp ? (height - 1 - y) : y;
                            int offset = (dstY * width + x) * 4;
                            output[offset] = color.Blue;
                            output[offset + 1] = color.Green;
                            output[offset + 2] = color.Red;
                            output[offset + 3] = 255;
                        }
                        i += bytesNeeded;
                        // Pad to word boundary
                        if (bytesNeeded % 2 == 1) i++;
                        break;
                }
            }
        }
    }
}