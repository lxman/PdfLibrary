using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ImageLibrary.Png;

/// <summary>
/// Decodes PNG image files.
/// </summary>
public static class PngDecoder
{
    /// <summary>
    /// Decode a PNG image from a byte array.
    /// </summary>
    public static PngImage Decode(byte[] data)
    {
        return Decode(data.AsSpan());
    }

    /// <summary>
    /// Decode a PNG image from a span.
    /// </summary>
    public static PngImage Decode(ReadOnlySpan<byte> data)
    {
        try
        {
            return DecodeInternal(data.ToArray());
        }
        catch (PngException)
        {
            throw;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new PngException($"Invalid data: {ex.Message}", ex);
        }
        catch (OverflowException ex)
        {
            throw new PngException($"Numeric overflow (image too large?): {ex.Message}", ex);
        }
        catch (IndexOutOfRangeException ex)
        {
            throw new PngException($"Data truncated or corrupted: {ex.Message}", ex);
        }
        catch (InvalidDataException ex)
        {
            throw new PngException($"Invalid compressed data: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new PngException($"Failed to decode PNG: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Decode a PNG image from a stream.
    /// </summary>
    public static PngImage Decode(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Decode(ms.ToArray());
    }

    /// <summary>
    /// Decode a PNG image from a file.
    /// </summary>
    public static PngImage Decode(string path)
    {
        return Decode(File.ReadAllBytes(path));
    }

    private static PngImage DecodeInternal(byte[] data)
    {
        if (data.Length < PngSignature.Length + 12) // Signature + minimum IHDR chunk
            throw new PngException("Data too small for PNG file");

        if (!PngSignature.IsValid(data))
            throw new PngException("Invalid PNG signature");

        int offset = PngSignature.Length;

        // Read IHDR chunk (must be first)
        PngChunk ihdrChunk = ReadChunk(data, ref offset);
        if (ihdrChunk.Type != PngChunkTypes.IHDR)
            throw new PngException("First chunk must be IHDR");

        IhdrChunk ihdr = ReadIhdr(data, ihdrChunk);

        if (ihdr.Width == 0 || ihdr.Height == 0)
            throw new PngException("Invalid image dimensions");

        // Validate dimensions won't cause overflow (limit to 32K x 32K)
        if (ihdr.Width > 32768 || ihdr.Height > 32768)
            throw new PngException($"Image dimensions too large: {ihdr.Width}x{ihdr.Height}");

        if (ihdr.CompressionMethod != 0)
            throw new PngException($"Unsupported compression method: {ihdr.CompressionMethod}");

        if (ihdr.FilterMethod != 0)
            throw new PngException($"Unsupported filter method: {ihdr.FilterMethod}");

        // Read remaining chunks
        PngColor[]? palette = null;
        byte[]? transparency = null;
        using var idatStream = new MemoryStream();

        while (offset < data.Length)
        {
            PngChunk chunk = ReadChunk(data, ref offset);

            switch (chunk.Type)
            {
                case PngChunkTypes.PLTE:
                    palette = ReadPalette(data, chunk);
                    break;

                case PngChunkTypes.tRNS:
                    transparency = ReadTransparency(data, chunk);
                    break;

                case PngChunkTypes.IDAT:
                    idatStream.Write(data, chunk.DataOffset, (int)chunk.Length);
                    break;

                case PngChunkTypes.IEND:
                    // End of image
                    break;
            }
        }

        // Decompress IDAT data
        byte[] compressedData = idatStream.ToArray();
        byte[] rawData = DecompressZlib(compressedData, ihdr);

        // Unfilter and convert to BGRA
        byte[] pixelData;
        if (ihdr.InterlaceMethod == PngInterlaceMethod.Adam7)
        {
            pixelData = DecodeInterlaced(rawData, ihdr, palette, transparency);
        }
        else
        {
            pixelData = DecodeNonInterlaced(rawData, ihdr, palette, transparency);
        }

        return new PngImage((int)ihdr.Width, (int)ihdr.Height, ihdr.BitDepth, ihdr.ColorType, pixelData);
    }

    private static PngChunk ReadChunk(byte[] data, ref int offset)
    {
        if (offset + 8 > data.Length)
            throw new PngException("Unexpected end of data reading chunk header");

        uint length = ReadUInt32BE(data, offset);
        string type = Encoding.ASCII.GetString(data, offset + 4, 4);
        int dataOffset = offset + 8;

        if (dataOffset + length + 4 > data.Length)
            throw new PngException($"Chunk {type} extends past end of data");

        uint crc = ReadUInt32BE(data, (int)(dataOffset + length));

        // Verify CRC
        uint calculatedCrc = Crc32.CalculateChunkCrc(type, data, dataOffset, (int)length);
        if (crc != calculatedCrc)
            throw new PngException($"CRC mismatch in chunk {type}");

        offset = (int)(dataOffset + length + 4);

        return new PngChunk(length, type, dataOffset, crc);
    }

    private static IhdrChunk ReadIhdr(byte[] data, PngChunk chunk)
    {
        if (chunk.Length != IhdrChunk.Size)
            throw new PngException($"Invalid IHDR chunk size: {chunk.Length}");

        int offset = chunk.DataOffset;
        uint width = ReadUInt32BE(data, offset);
        uint height = ReadUInt32BE(data, offset + 4);
        byte bitDepth = data[offset + 8];
        var colorType = (PngColorType)data[offset + 9];
        byte compression = data[offset + 10];
        byte filter = data[offset + 11];
        var interlace = (PngInterlaceMethod)data[offset + 12];

        // Validate bit depth for color type
        ValidateBitDepth(bitDepth, colorType);

        return new IhdrChunk(width, height, bitDepth, colorType, compression, filter, interlace);
    }

    private static void ValidateBitDepth(byte bitDepth, PngColorType colorType)
    {
        bool valid = colorType switch
        {
            PngColorType.Grayscale => bitDepth is 1 or 2 or 4 or 8 or 16,
            PngColorType.Rgb => bitDepth is 8 or 16,
            PngColorType.Indexed => bitDepth is 1 or 2 or 4 or 8,
            PngColorType.GrayscaleAlpha => bitDepth is 8 or 16,
            PngColorType.Rgba => bitDepth is 8 or 16,
            _ => false
        };

        if (!valid)
            throw new PngException($"Invalid bit depth {bitDepth} for color type {colorType}");
    }

    private static PngColor[] ReadPalette(byte[] data, PngChunk chunk)
    {
        if (chunk.Length % 3 != 0)
            throw new PngException("Invalid PLTE chunk size");

        int count = (int)chunk.Length / 3;
        var palette = new PngColor[count];

        for (var i = 0; i < count; i++)
        {
            int offset = chunk.DataOffset + i * 3;
            palette[i] = new PngColor(data[offset], data[offset + 1], data[offset + 2]);
        }

        return palette;
    }

    private static byte[] ReadTransparency(byte[] data, PngChunk chunk)
    {
        var transparency = new byte[chunk.Length];
        Array.Copy(data, chunk.DataOffset, transparency, 0, (int)chunk.Length);
        return transparency;
    }

    private static byte[] DecompressZlib(byte[] compressedData, IhdrChunk ihdr)
    {
        if (compressedData.Length < 2)
            throw new PngException("Compressed data too short");

        // Skip zlib header (2 bytes)
        using var inputStream = new MemoryStream(compressedData, 2, compressedData.Length - 2);
        using var deflateStream = new DeflateStream(inputStream, CompressionMode.Decompress);
        using var outputStream = new MemoryStream();

        deflateStream.CopyTo(outputStream);
        return outputStream.ToArray();
    }

    private static byte[] DecodeNonInterlaced(byte[] rawData, IhdrChunk ihdr,
        PngColor[]? palette, byte[]? transparency)
    {
        var width = (int)ihdr.Width;
        var height = (int)ihdr.Height;
        int bitsPerPixel = ihdr.BitsPerPixel;
        int bytesPerPixel = ihdr.BytesPerPixel;
        int scanlineBytes = (width * bitsPerPixel + 7) / 8;
        int stride = scanlineBytes + 1; // +1 for filter byte

        var pixelData = new byte[width * height * 4];
        var prevRow = new byte[scanlineBytes];
        var currRow = new byte[scanlineBytes];

        var rawOffset = 0;

        for (var y = 0; y < height; y++)
        {
            if (rawOffset >= rawData.Length)
                throw new PngException("Unexpected end of image data");

            var filterType = (PngFilterType)rawData[rawOffset++];

            // Read filtered row data
            int rowBytes = Math.Min(scanlineBytes, rawData.Length - rawOffset);
            Array.Copy(rawData, rawOffset, currRow, 0, rowBytes);
            rawOffset += scanlineBytes;

            // Unfilter
            Unfilter(currRow, prevRow, filterType, bytesPerPixel);

            // Convert to BGRA
            ConvertRowToBgra(currRow, pixelData, y * width * 4, ihdr, palette, transparency);

            // Swap buffers
            (prevRow, currRow) = (currRow, prevRow);
        }

        return pixelData;
    }

    private static byte[] DecodeInterlaced(byte[] rawData, IhdrChunk ihdr,
        PngColor[]? palette, byte[]? transparency)
    {
        var width = (int)ihdr.Width;
        var height = (int)ihdr.Height;
        var pixelData = new byte[width * height * 4];

        // Adam7 interlace pattern
        int[] startingRow = [0, 0, 4, 0, 2, 0, 1];
        int[] startingCol = [0, 4, 0, 2, 0, 1, 0];
        int[] rowIncrement = [8, 8, 8, 4, 4, 2, 2];
        int[] colIncrement = [8, 8, 4, 4, 2, 2, 1];

        var rawOffset = 0;

        for (var pass = 0; pass < 7; pass++)
        {
            int passWidth = (width - startingCol[pass] + colIncrement[pass] - 1) / colIncrement[pass];
            int passHeight = (height - startingRow[pass] + rowIncrement[pass] - 1) / rowIncrement[pass];

            if (passWidth == 0 || passHeight == 0)
                continue;

            int bitsPerPixel = ihdr.BitsPerPixel;
            int bytesPerPixel = ihdr.BytesPerPixel;
            int scanlineBytes = (passWidth * bitsPerPixel + 7) / 8;

            var prevRow = new byte[scanlineBytes];
            var currRow = new byte[scanlineBytes];
            var rowBgra = new byte[passWidth * 4];

            for (var passY = 0; passY < passHeight; passY++)
            {
                if (rawOffset >= rawData.Length)
                    break;

                var filterType = (PngFilterType)rawData[rawOffset++];

                int rowBytes = Math.Min(scanlineBytes, rawData.Length - rawOffset);
                Array.Copy(rawData, rawOffset, currRow, 0, rowBytes);
                rawOffset += scanlineBytes;

                Unfilter(currRow, prevRow, filterType, bytesPerPixel);

                // Convert row to temporary BGRA buffer
                ConvertRowToBgra(currRow, rowBgra, 0,
                    new IhdrChunk((uint)passWidth, 1, ihdr.BitDepth, ihdr.ColorType,
                        ihdr.CompressionMethod, ihdr.FilterMethod, PngInterlaceMethod.None),
                    palette, transparency);

                // Copy to final image at correct positions
                int destY = startingRow[pass] + passY * rowIncrement[pass];
                for (var passX = 0; passX < passWidth; passX++)
                {
                    int destX = startingCol[pass] + passX * colIncrement[pass];
                    int srcOffset = passX * 4;
                    int destOffset = (destY * width + destX) * 4;

                    pixelData[destOffset] = rowBgra[srcOffset];
                    pixelData[destOffset + 1] = rowBgra[srcOffset + 1];
                    pixelData[destOffset + 2] = rowBgra[srcOffset + 2];
                    pixelData[destOffset + 3] = rowBgra[srcOffset + 3];
                }

                (prevRow, currRow) = (currRow, prevRow);
            }
        }

        return pixelData;
    }

    private static void Unfilter(byte[] currRow, byte[] prevRow, PngFilterType filterType, int bytesPerPixel)
    {
        switch (filterType)
        {
            case PngFilterType.None:
                // No change needed
                break;

            case PngFilterType.Sub:
                for (int i = bytesPerPixel; i < currRow.Length; i++)
                {
                    currRow[i] = (byte)(currRow[i] + currRow[i - bytesPerPixel]);
                }
                break;

            case PngFilterType.Up:
                for (var i = 0; i < currRow.Length; i++)
                {
                    currRow[i] = (byte)(currRow[i] + prevRow[i]);
                }
                break;

            case PngFilterType.Average:
                for (var i = 0; i < currRow.Length; i++)
                {
                    int left = i >= bytesPerPixel ? currRow[i - bytesPerPixel] : 0;
                    int up = prevRow[i];
                    currRow[i] = (byte)(currRow[i] + (left + up) / 2);
                }
                break;

            case PngFilterType.Paeth:
                for (var i = 0; i < currRow.Length; i++)
                {
                    int left = i >= bytesPerPixel ? currRow[i - bytesPerPixel] : 0;
                    int up = prevRow[i];
                    int upLeft = i >= bytesPerPixel ? prevRow[i - bytesPerPixel] : 0;
                    currRow[i] = (byte)(currRow[i] + PaethPredictor(left, up, upLeft));
                }
                break;

            default:
                throw new PngException($"Unknown filter type: {filterType}");
        }
    }

    private static int PaethPredictor(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);

        if (pa <= pb && pa <= pc)
            return a;
        if (pb <= pc)
            return b;
        return c;
    }

    private static void ConvertRowToBgra(byte[] srcRow, byte[] destRow, int destOffset,
        IhdrChunk ihdr, PngColor[]? palette, byte[]? transparency)
    {
        var width = (int)ihdr.Width;

        switch (ihdr.ColorType)
        {
            case PngColorType.Grayscale:
                ConvertGrayscale(srcRow, destRow, destOffset, width, ihdr.BitDepth, transparency);
                break;

            case PngColorType.Rgb:
                ConvertRgb(srcRow, destRow, destOffset, width, ihdr.BitDepth, transparency);
                break;

            case PngColorType.Indexed:
                ConvertIndexed(srcRow, destRow, destOffset, width, ihdr.BitDepth, palette!, transparency);
                break;

            case PngColorType.GrayscaleAlpha:
                ConvertGrayscaleAlpha(srcRow, destRow, destOffset, width, ihdr.BitDepth);
                break;

            case PngColorType.Rgba:
                ConvertRgba(srcRow, destRow, destOffset, width, ihdr.BitDepth);
                break;
        }
    }

    private static void ConvertGrayscale(byte[] src, byte[] dest, int destOffset,
        int width, int bitDepth, byte[]? transparency)
    {
        int transparentValue = -1;
        if (transparency != null && transparency.Length >= 2)
        {
            transparentValue = (transparency[0] << 8) | transparency[1];
        }

        var srcBit = 0;
        var srcByte = 0;

        for (var x = 0; x < width; x++)
        {
            int gray;
            if (bitDepth == 16)
            {
                gray = src[srcByte++];
                int low = src[srcByte++];
                // Use high byte only for 8-bit output
            }
            else if (bitDepth == 8)
            {
                gray = src[srcByte++];
            }
            else
            {
                // Sub-byte bit depths
                int shift = 8 - bitDepth - srcBit;
                int mask = (1 << bitDepth) - 1;
                gray = (src[srcByte] >> shift) & mask;

                // Scale to 0-255
                gray = gray * 255 / ((1 << bitDepth) - 1);

                srcBit += bitDepth;
                if (srcBit >= 8)
                {
                    srcBit = 0;
                    srcByte++;
                }
            }

            byte alpha = (transparentValue >= 0 && gray == transparentValue) ? (byte)0 : (byte)255;

            dest[destOffset++] = (byte)gray; // B
            dest[destOffset++] = (byte)gray; // G
            dest[destOffset++] = (byte)gray; // R
            dest[destOffset++] = alpha;      // A
        }
    }

    private static void ConvertRgb(byte[] src, byte[] dest, int destOffset,
        int width, int bitDepth, byte[]? transparency)
    {
        int transR = -1, transG = -1, transB = -1;
        if (transparency != null && transparency.Length >= 6)
        {
            transR = (transparency[0] << 8) | transparency[1];
            transG = (transparency[2] << 8) | transparency[3];
            transB = (transparency[4] << 8) | transparency[5];
        }

        var srcOffset = 0;
        for (var x = 0; x < width; x++)
        {
            byte r, g, b;
            if (bitDepth == 16)
            {
                r = src[srcOffset++];
                srcOffset++; // Skip low byte
                g = src[srcOffset++];
                srcOffset++;
                b = src[srcOffset++];
                srcOffset++;
            }
            else
            {
                r = src[srcOffset++];
                g = src[srcOffset++];
                b = src[srcOffset++];
            }

            byte alpha = 255;
            if (transR >= 0 && r == transR && g == transG && b == transB)
                alpha = 0;

            dest[destOffset++] = b;
            dest[destOffset++] = g;
            dest[destOffset++] = r;
            dest[destOffset++] = alpha;
        }
    }

    private static void ConvertIndexed(byte[] src, byte[] dest, int destOffset,
        int width, int bitDepth, PngColor[] palette, byte[]? transparency)
    {
        var srcBit = 0;
        var srcByte = 0;

        for (var x = 0; x < width; x++)
        {
            int index;
            if (bitDepth == 8)
            {
                index = src[srcByte++];
            }
            else
            {
                int shift = 8 - bitDepth - srcBit;
                int mask = (1 << bitDepth) - 1;
                index = (src[srcByte] >> shift) & mask;

                srcBit += bitDepth;
                if (srcBit >= 8)
                {
                    srcBit = 0;
                    srcByte++;
                }
            }

            if (index < palette.Length)
            {
                PngColor color = palette[index];
                byte alpha = (transparency != null && index < transparency.Length)
                    ? transparency[index]
                    : (byte)255;

                dest[destOffset++] = color.B;
                dest[destOffset++] = color.G;
                dest[destOffset++] = color.R;
                dest[destOffset++] = alpha;
            }
            else
            {
                dest[destOffset++] = 0;
                dest[destOffset++] = 0;
                dest[destOffset++] = 0;
                dest[destOffset++] = 255;
            }
        }
    }

    private static void ConvertGrayscaleAlpha(byte[] src, byte[] dest, int destOffset,
        int width, int bitDepth)
    {
        var srcOffset = 0;
        for (var x = 0; x < width; x++)
        {
            byte gray, alpha;
            if (bitDepth == 16)
            {
                gray = src[srcOffset++];
                srcOffset++; // Skip low byte
                alpha = src[srcOffset++];
                srcOffset++;
            }
            else
            {
                gray = src[srcOffset++];
                alpha = src[srcOffset++];
            }

            dest[destOffset++] = gray;
            dest[destOffset++] = gray;
            dest[destOffset++] = gray;
            dest[destOffset++] = alpha;
        }
    }

    private static void ConvertRgba(byte[] src, byte[] dest, int destOffset,
        int width, int bitDepth)
    {
        var srcOffset = 0;
        for (var x = 0; x < width; x++)
        {
            byte r, g, b, a;
            if (bitDepth == 16)
            {
                r = src[srcOffset++];
                srcOffset++;
                g = src[srcOffset++];
                srcOffset++;
                b = src[srcOffset++];
                srcOffset++;
                a = src[srcOffset++];
                srcOffset++;
            }
            else
            {
                r = src[srcOffset++];
                g = src[srcOffset++];
                b = src[srcOffset++];
                a = src[srcOffset++];
            }

            dest[destOffset++] = b;
            dest[destOffset++] = g;
            dest[destOffset++] = r;
            dest[destOffset++] = a;
        }
    }

    private static uint ReadUInt32BE(byte[] data, int offset)
    {
        return (uint)((data[offset] << 24) | (data[offset + 1] << 16) |
                      (data[offset + 2] << 8) | data[offset + 3]);
    }
}
