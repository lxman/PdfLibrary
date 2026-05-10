namespace Jbig2Decoder.Tests;

/// <summary>
/// Tiny BMP reader sufficient for the power-jbig2-tests reference bitmaps
/// (1-bit-per-pixel uncompressed, BI_RGB). Returns a packed 1-bit row buffer
/// (MSB-first) and dimensions, so we can compare against decoder output bit-exactly.
/// </summary>
internal static class BmpReference
{
    public sealed record Bitmap(int Width, int Height, byte[] PackedRows);

    public static Bitmap Load(string path)
    {
        byte[] file = File.ReadAllBytes(path);

        // BMP file header: 14 bytes
        if (file.Length < 54 || file[0] != 'B' || file[1] != 'M')
            throw new InvalidDataException($"Not a BMP file: {path}");

        var dataOffset = BitConverter.ToInt32(file, 10);

        // DIB header (BITMAPINFOHEADER): width @18, height @22, bitsPerPixel @28
        var width = BitConverter.ToInt32(file, 18);
        var rawHeight = BitConverter.ToInt32(file, 22);
        int bitsPerPixel = BitConverter.ToInt16(file, 28);
        var compression = BitConverter.ToInt32(file, 30);

        if (bitsPerPixel != 1)
            throw new NotSupportedException($"BMP at {path} is {bitsPerPixel}bpp; expected 1bpp.");
        if (compression != 0)
            throw new NotSupportedException($"BMP at {path} is compressed; expected BI_RGB.");

        // Negative height = top-down rows; positive = bottom-up.
        int height = Math.Abs(rawHeight);
        bool bottomUp = rawHeight > 0;

        // BMP rows are padded to 4-byte boundaries.
        int rowBytes = (width + 7) / 8;
        int paddedRowBytes = (rowBytes + 3) & ~3;

        // Read the colour table (2 entries × 4 bytes) to figure out which palette index = black.
        // Standard mono BMP: index 0 = black (0,0,0,0), index 1 = white (255,255,255,0).
        // Our packed-row output stores 1 = foreground (black), 0 = background (white).
        var palette0 = new byte[4];
        var palette1 = new byte[4];
        Array.Copy(file, 54, palette0, 0, 4);
        Array.Copy(file, 58, palette1, 0, 4);
        bool zeroIsBlack = palette0[0] + palette0[1] + palette0[2] < palette1[0] + palette1[1] + palette1[2];

        var packed = new byte[rowBytes * height];

        for (var y = 0; y < height; y++)
        {
            int srcY = bottomUp ? height - 1 - y : y;
            int srcOffset = dataOffset + srcY * paddedRowBytes;
            for (var b = 0; b < rowBytes; b++)
            {
                byte raw = file[srcOffset + b];
                packed[y * rowBytes + b] = zeroIsBlack ? (byte)~raw : raw;
            }
        }

        return new Bitmap(width, height, packed);
    }
}
