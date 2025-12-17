using System;
using System.IO;

namespace ImageLibrary.Tga;

/// <summary>
/// Decodes TGA image files.
/// </summary>
public static class TgaDecoder
{
    /// <summary>
    /// Decode a TGA image from a byte array.
    /// </summary>
    public static TgaImage Decode(byte[] data)
    {
        return Decode(data.AsSpan());
    }

    /// <summary>
    /// Decode a TGA image from a span.
    /// </summary>
    public static TgaImage Decode(ReadOnlySpan<byte> data)
    {
        try
        {
            byte[] dataArray = data.ToArray();
            if (dataArray.Length < TgaHeader.Size)
                throw new TgaException("Data too small for TGA header");

            var offset = 0;
            TgaHeader header = ReadHeader(dataArray, ref offset);

            if (header.Width == 0 || header.Height == 0)
                throw new TgaException("Invalid image dimensions");

            // Validate dimensions won't cause overflow (limit to 32K x 32K)
            if (header.Width > 32768 || header.Height > 32768)
                throw new TgaException($"Image dimensions too large: {header.Width}x{header.Height}");

            // Skip image ID
            offset += header.IdLength;

            // Read color map if present
            byte[]? colorMap = null;
            var colorMapBytesPerEntry = 0;
            if (header.HasColorMap && header.ColorMapLength > 0)
            {
                colorMapBytesPerEntry = (header.ColorMapEntrySize + 7) / 8;
                int colorMapSize = header.ColorMapLength * colorMapBytesPerEntry;
                if (offset + colorMapSize > dataArray.Length)
                    throw new TgaException("Unexpected end of data reading color map");
                colorMap = new byte[colorMapSize];
                Array.Copy(dataArray, offset, colorMap, 0, colorMapSize);
                offset += colorMapSize;
            }

            // Decode pixel data
            int width = header.Width;
            int height = header.Height;
            var pixelData = new byte[width * height * 4];

            switch (header.ImageType)
            {
                case TgaImageType.NoImage:
                    throw new TgaException("TGA file contains no image data");

                case TgaImageType.ColorMapped:
                    DecodeColorMapped(dataArray, ref offset, header, colorMap!, colorMapBytesPerEntry, pixelData);
                    break;

                case TgaImageType.TrueColor:
                    DecodeTrueColor(dataArray, ref offset, header, pixelData);
                    break;

                case TgaImageType.Grayscale:
                    DecodeGrayscale(dataArray, ref offset, header, pixelData);
                    break;

                case TgaImageType.RleColorMapped:
                    DecodeRleColorMapped(dataArray, ref offset, header, colorMap!, colorMapBytesPerEntry, pixelData);
                    break;

                case TgaImageType.RleTrueColor:
                    DecodeRleTrueColor(dataArray, ref offset, header, pixelData);
                    break;

                case TgaImageType.RleGrayscale:
                    DecodeRleGrayscale(dataArray, ref offset, header, pixelData);
                    break;

                default:
                    throw new TgaException($"Unsupported TGA image type: {header.ImageType}");
            }

            // Handle image orientation
            ApplyOrientation(pixelData, width, height, header.IsTopToBottom, header.IsRightToLeft);

            return new TgaImage(width, height, header.PixelDepth, pixelData);
        }
        catch (TgaException)
        {
            throw;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new TgaException($"Invalid data: {ex.Message}", ex);
        }
        catch (OverflowException ex)
        {
            throw new TgaException($"Numeric overflow (image too large?): {ex.Message}", ex);
        }
        catch (IndexOutOfRangeException ex)
        {
            throw new TgaException($"Data truncated or corrupted: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new TgaException($"Failed to decode TGA: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Decode a TGA image from a stream.
    /// </summary>
    public static TgaImage Decode(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Decode(ms.ToArray());
    }

    /// <summary>
    /// Decode a TGA image from a file.
    /// </summary>
    public static TgaImage Decode(string path)
    {
        return Decode(File.ReadAllBytes(path));
    }

    private static TgaHeader ReadHeader(byte[] data, ref int offset)
    {
        byte idLength = data[offset++];
        byte colorMapType = data[offset++];
        var imageType = (TgaImageType)data[offset++];
        ushort colorMapFirstEntry = ReadUInt16(data, ref offset);
        ushort colorMapLength = ReadUInt16(data, ref offset);
        byte colorMapEntrySize = data[offset++];
        ushort xOrigin = ReadUInt16(data, ref offset);
        ushort yOrigin = ReadUInt16(data, ref offset);
        ushort width = ReadUInt16(data, ref offset);
        ushort height = ReadUInt16(data, ref offset);
        byte pixelDepth = data[offset++];
        byte imageDescriptor = data[offset++];

        return new TgaHeader(
            idLength, colorMapType, imageType,
            colorMapFirstEntry, colorMapLength, colorMapEntrySize,
            xOrigin, yOrigin, width, height,
            pixelDepth, imageDescriptor);
    }

    private static void DecodeColorMapped(byte[] data, ref int offset, TgaHeader header,
        byte[] colorMap, int bytesPerEntry, byte[] pixelData)
    {
        int width = header.Width;
        int height = header.Height;
        int bytesPerIndex = (header.PixelDepth + 7) / 8;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (offset + bytesPerIndex > data.Length)
                    throw new TgaException("Unexpected end of pixel data");

                int index = ReadIndex(data, ref offset, bytesPerIndex) - header.ColorMapFirstEntry;
                if (index < 0 || index >= header.ColorMapLength)
                    throw new TgaException($"Color map index out of range: {index}");

                int destOffset = (y * width + x) * 4;
                ReadColorMapEntry(colorMap, index * bytesPerEntry, bytesPerEntry, pixelData, destOffset);
            }
        }
    }

    private static void DecodeTrueColor(byte[] data, ref int offset, TgaHeader header, byte[] pixelData)
    {
        int width = header.Width;
        int height = header.Height;
        int bytesPerPixel = header.PixelDepth / 8;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (offset + bytesPerPixel > data.Length)
                    throw new TgaException("Unexpected end of pixel data");

                int destOffset = (y * width + x) * 4;
                ReadPixel(data, ref offset, bytesPerPixel, header.AlphaBits, pixelData, destOffset);
            }
        }
    }

    private static void DecodeGrayscale(byte[] data, ref int offset, TgaHeader header, byte[] pixelData)
    {
        int width = header.Width;
        int height = header.Height;
        int bytesPerPixel = header.PixelDepth / 8;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (offset + bytesPerPixel > data.Length)
                    throw new TgaException("Unexpected end of pixel data");

                int destOffset = (y * width + x) * 4;
                byte gray = data[offset++];
                byte alpha = bytesPerPixel == 2 ? data[offset++] : (byte)255;

                pixelData[destOffset] = gray;     // B
                pixelData[destOffset + 1] = gray; // G
                pixelData[destOffset + 2] = gray; // R
                pixelData[destOffset + 3] = alpha; // A
            }
        }
    }

    private static void DecodeRleColorMapped(byte[] data, ref int offset, TgaHeader header,
        byte[] colorMap, int bytesPerEntry, byte[] pixelData)
    {
        int width = header.Width;
        int height = header.Height;
        int totalPixels = width * height;
        int bytesPerIndex = (header.PixelDepth + 7) / 8;
        var pixelIndex = 0;
        var iterations = 0;
        const int maxIterations = 100_000_000;

        while (pixelIndex < totalPixels)
        {
            if (++iterations > maxIterations)
                throw new TgaException("RLE decode exceeded maximum iterations");

            if (offset >= data.Length)
                throw new TgaException("Unexpected end of RLE data");

            byte packetHeader = data[offset++];
            int count = (packetHeader & 0x7F) + 1;

            if (pixelIndex + count > totalPixels)
                throw new TgaException("RLE packet exceeds image bounds");

            if ((packetHeader & 0x80) != 0)
            {
                // Run-length packet
                if (offset + bytesPerIndex > data.Length)
                    throw new TgaException("Unexpected end of RLE data");

                int index = ReadIndex(data, ref offset, bytesPerIndex) - header.ColorMapFirstEntry;
                if (index < 0 || index >= header.ColorMapLength)
                    throw new TgaException($"Color map index out of range: {index}");

                var pixel = new byte[4];
                ReadColorMapEntry(colorMap, index * bytesPerEntry, bytesPerEntry, pixel, 0);

                for (var i = 0; i < count; i++)
                {
                    int destOffset = pixelIndex * 4;
                    Array.Copy(pixel, 0, pixelData, destOffset, 4);
                    pixelIndex++;
                }
            }
            else
            {
                // Raw packet
                for (var i = 0; i < count; i++)
                {
                    if (offset + bytesPerIndex > data.Length)
                        throw new TgaException("Unexpected end of RLE data");

                    int index = ReadIndex(data, ref offset, bytesPerIndex) - header.ColorMapFirstEntry;
                    if (index < 0 || index >= header.ColorMapLength)
                        throw new TgaException($"Color map index out of range: {index}");

                    int destOffset = pixelIndex * 4;
                    ReadColorMapEntry(colorMap, index * bytesPerEntry, bytesPerEntry, pixelData, destOffset);
                    pixelIndex++;
                }
            }
        }
    }

    private static void DecodeRleTrueColor(byte[] data, ref int offset, TgaHeader header, byte[] pixelData)
    {
        int width = header.Width;
        int height = header.Height;
        int totalPixels = width * height;
        int bytesPerPixel = header.PixelDepth / 8;
        var pixelIndex = 0;
        var iterations = 0;
        const int maxIterations = 100_000_000;

        while (pixelIndex < totalPixels)
        {
            if (++iterations > maxIterations)
                throw new TgaException("RLE decode exceeded maximum iterations");

            if (offset >= data.Length)
                throw new TgaException("Unexpected end of RLE data");

            byte packetHeader = data[offset++];
            int count = (packetHeader & 0x7F) + 1;

            if (pixelIndex + count > totalPixels)
                throw new TgaException("RLE packet exceeds image bounds");

            if ((packetHeader & 0x80) != 0)
            {
                // Run-length packet
                if (offset + bytesPerPixel > data.Length)
                    throw new TgaException("Unexpected end of RLE data");

                var pixel = new byte[4];
                ReadPixel(data, ref offset, bytesPerPixel, header.AlphaBits, pixel, 0);

                for (var i = 0; i < count; i++)
                {
                    int destOffset = pixelIndex * 4;
                    Array.Copy(pixel, 0, pixelData, destOffset, 4);
                    pixelIndex++;
                }
            }
            else
            {
                // Raw packet
                for (var i = 0; i < count; i++)
                {
                    if (offset + bytesPerPixel > data.Length)
                        throw new TgaException("Unexpected end of RLE data");

                    int destOffset = pixelIndex * 4;
                    ReadPixel(data, ref offset, bytesPerPixel, header.AlphaBits, pixelData, destOffset);
                    pixelIndex++;
                }
            }
        }
    }

    private static void DecodeRleGrayscale(byte[] data, ref int offset, TgaHeader header, byte[] pixelData)
    {
        int width = header.Width;
        int height = header.Height;
        int totalPixels = width * height;
        int bytesPerPixel = header.PixelDepth / 8;
        var pixelIndex = 0;
        var iterations = 0;
        const int maxIterations = 100_000_000;

        while (pixelIndex < totalPixels)
        {
            if (++iterations > maxIterations)
                throw new TgaException("RLE decode exceeded maximum iterations");

            if (offset >= data.Length)
                throw new TgaException("Unexpected end of RLE data");

            byte packetHeader = data[offset++];
            int count = (packetHeader & 0x7F) + 1;

            if (pixelIndex + count > totalPixels)
                throw new TgaException("RLE packet exceeds image bounds");

            if ((packetHeader & 0x80) != 0)
            {
                // Run-length packet
                if (offset + bytesPerPixel > data.Length)
                    throw new TgaException("Unexpected end of RLE data");

                byte gray = data[offset++];
                byte alpha = bytesPerPixel == 2 ? data[offset++] : (byte)255;

                for (var i = 0; i < count; i++)
                {
                    int destOffset = pixelIndex * 4;
                    pixelData[destOffset] = gray;
                    pixelData[destOffset + 1] = gray;
                    pixelData[destOffset + 2] = gray;
                    pixelData[destOffset + 3] = alpha;
                    pixelIndex++;
                }
            }
            else
            {
                // Raw packet
                for (var i = 0; i < count; i++)
                {
                    if (offset + bytesPerPixel > data.Length)
                        throw new TgaException("Unexpected end of RLE data");

                    byte gray = data[offset++];
                    byte alpha = bytesPerPixel == 2 ? data[offset++] : (byte)255;

                    int destOffset = pixelIndex * 4;
                    pixelData[destOffset] = gray;
                    pixelData[destOffset + 1] = gray;
                    pixelData[destOffset + 2] = gray;
                    pixelData[destOffset + 3] = alpha;
                    pixelIndex++;
                }
            }
        }
    }

    private static void ReadPixel(byte[] data, ref int offset, int bytesPerPixel, int alphaBits,
        byte[] dest, int destOffset)
    {
        switch (bytesPerPixel)
        {
            case 2: // 16-bit (5-5-5 or 5-5-5-1)
                ushort pixel16 = ReadUInt16(data, ref offset);
                dest[destOffset + 2] = (byte)((pixel16 & 0x7C00) >> 7); // R (5 bits -> 8 bits)
                dest[destOffset + 1] = (byte)((pixel16 & 0x03E0) >> 2); // G
                dest[destOffset] = (byte)((pixel16 & 0x001F) << 3);     // B
                dest[destOffset + 3] = alphaBits > 0 && (pixel16 & 0x8000) == 0 ? (byte)0 : (byte)255; // A
                break;

            case 3: // 24-bit BGR
                dest[destOffset] = data[offset++];     // B
                dest[destOffset + 1] = data[offset++]; // G
                dest[destOffset + 2] = data[offset++]; // R
                dest[destOffset + 3] = 255;            // A
                break;

            case 4: // 32-bit BGRA
                dest[destOffset] = data[offset++];     // B
                dest[destOffset + 1] = data[offset++]; // G
                dest[destOffset + 2] = data[offset++]; // R
                dest[destOffset + 3] = data[offset++]; // A
                break;

            default:
                throw new TgaException($"Unsupported pixel depth: {bytesPerPixel * 8} bits");
        }
    }

    private static void ReadColorMapEntry(byte[] colorMap, int entryOffset, int bytesPerEntry,
        byte[] dest, int destOffset)
    {
        switch (bytesPerEntry)
        {
            case 2: // 16-bit
                var pixel16 = (ushort)(colorMap[entryOffset] | (colorMap[entryOffset + 1] << 8));
                dest[destOffset + 2] = (byte)((pixel16 & 0x7C00) >> 7);
                dest[destOffset + 1] = (byte)((pixel16 & 0x03E0) >> 2);
                dest[destOffset] = (byte)((pixel16 & 0x001F) << 3);
                dest[destOffset + 3] = (pixel16 & 0x8000) == 0 ? (byte)255 : (byte)0;
                break;

            case 3: // 24-bit BGR
                dest[destOffset] = colorMap[entryOffset];
                dest[destOffset + 1] = colorMap[entryOffset + 1];
                dest[destOffset + 2] = colorMap[entryOffset + 2];
                dest[destOffset + 3] = 255;
                break;

            case 4: // 32-bit BGRA
                dest[destOffset] = colorMap[entryOffset];
                dest[destOffset + 1] = colorMap[entryOffset + 1];
                dest[destOffset + 2] = colorMap[entryOffset + 2];
                dest[destOffset + 3] = colorMap[entryOffset + 3];
                break;

            default:
                throw new TgaException($"Unsupported color map entry size: {bytesPerEntry * 8} bits");
        }
    }

    private static int ReadIndex(byte[] data, ref int offset, int bytesPerIndex)
    {
        switch (bytesPerIndex)
        {
            case 1:
                return data[offset++];
            case 2:
                return ReadUInt16(data, ref offset);
            default:
                throw new TgaException($"Unsupported index size: {bytesPerIndex} bytes");
        }
    }

    private static void ApplyOrientation(byte[] pixelData, int width, int height, bool isTopToBottom, bool isRightToLeft)
    {
        // TGA default is bottom-to-top, we want top-to-bottom
        if (!isTopToBottom)
        {
            FlipVertical(pixelData, width, height);
        }

        if (isRightToLeft)
        {
            FlipHorizontal(pixelData, width, height);
        }
    }

    private static void FlipVertical(byte[] pixelData, int width, int height)
    {
        int stride = width * 4;
        var temp = new byte[stride];

        for (var y = 0; y < height / 2; y++)
        {
            int topOffset = y * stride;
            int bottomOffset = (height - 1 - y) * stride;

            Array.Copy(pixelData, topOffset, temp, 0, stride);
            Array.Copy(pixelData, bottomOffset, pixelData, topOffset, stride);
            Array.Copy(temp, 0, pixelData, bottomOffset, stride);
        }
    }

    private static void FlipHorizontal(byte[] pixelData, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            int rowOffset = y * width * 4;
            for (var x = 0; x < width / 2; x++)
            {
                int leftOffset = rowOffset + x * 4;
                int rightOffset = rowOffset + (width - 1 - x) * 4;

                // Swap pixels
                for (var i = 0; i < 4; i++)
                {
                    byte temp = pixelData[leftOffset + i];
                    pixelData[leftOffset + i] = pixelData[rightOffset + i];
                    pixelData[rightOffset + i] = temp;
                }
            }
        }
    }

    private static ushort ReadUInt16(byte[] data, ref int offset)
    {
        var value = (ushort)(data[offset] | (data[offset + 1] << 8));
        offset += 2;
        return value;
    }
}
