using System;

namespace BmpCodec.Tests;

/// <summary>
/// Minimal BMP writer for tests: a 14-byte file header + 40-byte BITMAPINFOHEADER, with the optional
/// three BITFIELDS channel masks placed at the standard offset (54).
/// </summary>
internal static class BmpBuilder
{
    public static byte[] BitFields(int width, int height, ushort bpp, uint maskR, uint maskG, uint maskB, byte[] pixelData)
    {
        const int headerSize = 14 + 40 + 12; // file header + info header + 3 masks
        var file = new byte[headerSize + pixelData.Length];

        file[0] = (byte)'B'; file[1] = (byte)'M';
        WriteU32(file, 2, (uint)file.Length);
        WriteU32(file, 10, headerSize); // pixel data offset

        WriteU32(file, 14, 40);          // info header size
        WriteI32(file, 18, width);
        WriteI32(file, 22, height);
        WriteU16(file, 26, 1);           // planes
        WriteU16(file, 28, bpp);
        WriteU32(file, 30, 3);           // compression = BITFIELDS
        WriteU32(file, 54, maskR);
        WriteU32(file, 58, maskG);
        WriteU32(file, 62, maskB);

        Array.Copy(pixelData, 0, file, headerSize, pixelData.Length);
        return file;
    }

    /// <summary>A 54-byte headers-only BMP (uncompressed 24-bit) for dimension-validation tests.</summary>
    public static byte[] HeaderOnly(int width, int height)
    {
        var file = new byte[54];
        file[0] = (byte)'B'; file[1] = (byte)'M';
        WriteU32(file, 2, 54);
        WriteU32(file, 10, 54);
        WriteU32(file, 14, 40);
        WriteI32(file, 18, width);
        WriteI32(file, 22, height);
        WriteU16(file, 26, 1);
        WriteU16(file, 28, 24);
        WriteU32(file, 30, 0); // RGB
        return file;
    }

    private static void WriteU16(byte[] b, int o, ushort v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
    private static void WriteU32(byte[] b, int o, uint v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }
    private static void WriteI32(byte[] b, int o, int v) => WriteU32(b, o, (uint)v);
}
