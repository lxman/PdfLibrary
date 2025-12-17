using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ImageLibrary.Png;

/// <summary>
/// Encodes images to PNG format.
/// </summary>
public static class PngEncoder
{
    /// <summary>
    /// Encode an image to PNG format.
    /// </summary>
    /// <param name="image">The image to encode.</param>
    /// <param name="colorType">Output color type. Default is RGBA.</param>
    /// <returns>The encoded PNG data.</returns>
    public static byte[] Encode(PngImage image, PngColorType colorType = PngColorType.Rgba)
    {
        using var ms = new MemoryStream();

        // Write signature
        ms.Write(PngSignature.Bytes, 0, PngSignature.Length);

        // Determine actual color type based on image content
        bool hasAlpha = ImageHasAlpha(image);
        bool isGrayscale = ImageIsGrayscale(image);

        PngColorType actualColorType = colorType;
        if (colorType == PngColorType.Rgba && !hasAlpha)
            actualColorType = PngColorType.Rgb;
        if (colorType == PngColorType.Rgb && isGrayscale)
            actualColorType = hasAlpha ? PngColorType.GrayscaleAlpha : PngColorType.Grayscale;

        // Write IHDR
        WriteIhdr(ms, image, actualColorType);

        // Convert to raw format and filter
        byte[] rawData = ConvertAndFilter(image, actualColorType);

        // Compress with zlib
        byte[] compressedData = CompressZlib(rawData);

        // Write IDAT chunk(s)
        WriteIdatChunks(ms, compressedData);

        // Write IEND
        WriteIend(ms);

        return ms.ToArray();
    }

    /// <summary>
    /// Encode a PNG image to a stream.
    /// </summary>
    public static void Encode(PngImage image, Stream stream, PngColorType colorType = PngColorType.Rgba)
    {
        byte[] data = Encode(image, colorType);
        stream.Write(data, 0, data.Length);
    }

    /// <summary>
    /// Encode a PNG image to a file.
    /// </summary>
    public static void Encode(PngImage image, string path, PngColorType colorType = PngColorType.Rgba)
    {
        File.WriteAllBytes(path, Encode(image, colorType));
    }

    private static bool ImageHasAlpha(PngImage image)
    {
        for (var i = 3; i < image.PixelData.Length; i += 4)
        {
            if (image.PixelData[i] != 255)
                return true;
        }
        return false;
    }

    private static bool ImageIsGrayscale(PngImage image)
    {
        for (var i = 0; i < image.PixelData.Length; i += 4)
        {
            byte b = image.PixelData[i];
            byte g = image.PixelData[i + 1];
            byte r = image.PixelData[i + 2];
            if (r != g || g != b)
                return false;
        }
        return true;
    }

    private static void WriteIhdr(Stream stream, PngImage image, PngColorType colorType)
    {
        var data = new byte[IhdrChunk.Size];

        WriteUInt32BE(data, 0, (uint)image.Width);
        WriteUInt32BE(data, 4, (uint)image.Height);
        data[8] = 8; // Bit depth
        data[9] = (byte)colorType;
        data[10] = 0; // Compression method
        data[11] = 0; // Filter method
        data[12] = 0; // Interlace method (none)

        WriteChunk(stream, PngChunkTypes.IHDR, data);
    }

    private static byte[] ConvertAndFilter(PngImage image, PngColorType colorType)
    {
        int bytesPerPixel = colorType switch
        {
            PngColorType.Grayscale => 1,
            PngColorType.Rgb => 3,
            PngColorType.GrayscaleAlpha => 2,
            PngColorType.Rgba => 4,
            _ => throw new PngException($"Unsupported color type for encoding: {colorType}")
        };

        int scanlineBytes = image.Width * bytesPerPixel;
        int stride = scanlineBytes + 1; // +1 for filter byte

        var rawData = new byte[stride * image.Height];
        var currRow = new byte[scanlineBytes];
        var prevRow = new byte[scanlineBytes];

        for (var y = 0; y < image.Height; y++)
        {
            // Convert row to target format
            ConvertRow(image, y, currRow, colorType);

            // Choose best filter for this row
            (PngFilterType filterType, byte[] filteredRow) = ChooseBestFilter(currRow, prevRow, bytesPerPixel);

            // Write filter byte and filtered row
            int rowOffset = y * stride;
            rawData[rowOffset] = (byte)filterType;
            Array.Copy(filteredRow, 0, rawData, rowOffset + 1, scanlineBytes);

            // Swap buffers
            (prevRow, currRow) = (currRow, prevRow);
        }

        return rawData;
    }

    private static void ConvertRow(PngImage image, int y, byte[] dest, PngColorType colorType)
    {
        int srcOffset = y * image.Width * 4;
        var destOffset = 0;

        for (var x = 0; x < image.Width; x++)
        {
            byte b = image.PixelData[srcOffset++];
            byte g = image.PixelData[srcOffset++];
            byte r = image.PixelData[srcOffset++];
            byte a = image.PixelData[srcOffset++];

            switch (colorType)
            {
                case PngColorType.Grayscale:
                    dest[destOffset++] = r; // Assume already grayscale (r=g=b)
                    break;

                case PngColorType.Rgb:
                    dest[destOffset++] = r;
                    dest[destOffset++] = g;
                    dest[destOffset++] = b;
                    break;

                case PngColorType.GrayscaleAlpha:
                    dest[destOffset++] = r;
                    dest[destOffset++] = a;
                    break;

                case PngColorType.Rgba:
                    dest[destOffset++] = r;
                    dest[destOffset++] = g;
                    dest[destOffset++] = b;
                    dest[destOffset++] = a;
                    break;
            }
        }
    }

    private static (PngFilterType, byte[]) ChooseBestFilter(byte[] currRow, byte[] prevRow, int bytesPerPixel)
    {
        // Try all filters and pick the one with lowest sum of absolute values
        // (heuristic for best compression)

        var filtered = new byte[5][];
        var sums = new long[5];

        for (var f = 0; f < 5; f++)
        {
            filtered[f] = new byte[currRow.Length];
            var filterType = (PngFilterType)f;

            for (var i = 0; i < currRow.Length; i++)
            {
                byte filtered_byte = filterType switch
                {
                    PngFilterType.None => currRow[i],
                    PngFilterType.Sub => (byte)(currRow[i] - (i >= bytesPerPixel ? currRow[i - bytesPerPixel] : 0)),
                    PngFilterType.Up => (byte)(currRow[i] - prevRow[i]),
                    PngFilterType.Average => (byte)(currRow[i] - ((i >= bytesPerPixel ? currRow[i - bytesPerPixel] : 0) + prevRow[i]) / 2),
                    PngFilterType.Paeth => (byte)(currRow[i] - PaethPredictor(
                        i >= bytesPerPixel ? currRow[i - bytesPerPixel] : 0,
                        prevRow[i],
                        i >= bytesPerPixel ? prevRow[i - bytesPerPixel] : 0)),
                    _ => currRow[i]
                };

                filtered[f][i] = filtered_byte;
                // Calculate absolute value treating byte as signed, avoiding overflow on 128
                sums[f] += filtered_byte <= 127 ? filtered_byte : 256 - filtered_byte;
            }
        }

        // Find minimum sum
        var bestFilter = 0;
        long minSum = sums[0];
        for (var f = 1; f < 5; f++)
        {
            if (sums[f] < minSum)
            {
                minSum = sums[f];
                bestFilter = f;
            }
        }

        return ((PngFilterType)bestFilter, filtered[bestFilter]);
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

    private static byte[] CompressZlib(byte[] data)
    {
        using var outputStream = new MemoryStream();

        // Write zlib header (deflate, default compression)
        outputStream.WriteByte(0x78); // CMF: deflate, 32K window
        outputStream.WriteByte(0x9C); // FLG: default compression, no dict, check bits

        // Compress with deflate
        using (var deflateStream = new DeflateStream(outputStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflateStream.Write(data, 0, data.Length);
        }

        // Calculate and write Adler-32 checksum
        uint adler = CalculateAdler32(data);
        outputStream.WriteByte((byte)((adler >> 24) & 0xFF));
        outputStream.WriteByte((byte)((adler >> 16) & 0xFF));
        outputStream.WriteByte((byte)((adler >> 8) & 0xFF));
        outputStream.WriteByte((byte)(adler & 0xFF));

        return outputStream.ToArray();
    }

    private static uint CalculateAdler32(byte[] data)
    {
        const uint ModAdler = 65521;
        uint a = 1, b = 0;

        foreach (byte d in data)
        {
            a = (a + d) % ModAdler;
            b = (b + a) % ModAdler;
        }

        return (b << 16) | a;
    }

    private static void WriteIdatChunks(Stream stream, byte[] compressedData)
    {
        // Write in chunks of up to 32KB
        const int maxChunkSize = 32768;
        var offset = 0;

        while (offset < compressedData.Length)
        {
            int chunkSize = Math.Min(maxChunkSize, compressedData.Length - offset);
            var chunkData = new byte[chunkSize];
            Array.Copy(compressedData, offset, chunkData, 0, chunkSize);
            WriteChunk(stream, PngChunkTypes.IDAT, chunkData);
            offset += chunkSize;
        }
    }

    private static void WriteIend(Stream stream)
    {
        WriteChunk(stream, PngChunkTypes.IEND, []);
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        // Length (big-endian)
        WriteUInt32BE(stream, (uint)data.Length);

        // Type
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes, 0, 4);

        // Data
        if (data.Length > 0)
            stream.Write(data, 0, data.Length);

        // CRC
        uint crc = Crc32.CalculateChunkCrc(type, data, 0, data.Length);
        WriteUInt32BE(stream, crc);
    }

    private static void WriteUInt32BE(Stream stream, uint value)
    {
        stream.WriteByte((byte)((value >> 24) & 0xFF));
        stream.WriteByte((byte)((value >> 16) & 0xFF));
        stream.WriteByte((byte)((value >> 8) & 0xFF));
        stream.WriteByte((byte)(value & 0xFF));
    }

    private static void WriteUInt32BE(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)((value >> 24) & 0xFF);
        data[offset + 1] = (byte)((value >> 16) & 0xFF);
        data[offset + 2] = (byte)((value >> 8) & 0xFF);
        data[offset + 3] = (byte)(value & 0xFF);
    }
}
