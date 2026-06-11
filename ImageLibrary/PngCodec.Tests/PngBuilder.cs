using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PngCodec.Tests;

/// <summary>
/// Minimal hand-built PNG writer for tests, since the encoder only emits 8-bit images and can't
/// produce the 16-bit / tRNS / oversized cases these tests need. Writes the signature, IHDR, an
/// optional chunk (e.g. tRNS), a zlib-wrapped IDAT, and IEND — with a correct CRC-32 on each chunk.
/// </summary>
internal static class PngBuilder
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] Build(uint width, uint height, byte bitDepth, byte colorType, byte[] scanlines,
        (string Type, byte[] Data)? extraBeforeIdat = null)
    {
        using var ms = new MemoryStream();
        ms.Write(Signature, 0, Signature.Length);
        WriteChunk(ms, "IHDR", Ihdr(width, height, bitDepth, colorType));
        if (extraBeforeIdat is { } extra)
            WriteChunk(ms, extra.Type, extra.Data);
        WriteChunk(ms, "IDAT", Zlib(scanlines));
        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    /// <summary>Builds a signature + IHDR + IEND PNG (no image data) — for header-validation tests.</summary>
    public static byte[] HeaderOnly(uint width, uint height, byte bitDepth, byte colorType)
    {
        using var ms = new MemoryStream();
        ms.Write(Signature, 0, Signature.Length);
        WriteChunk(ms, "IHDR", Ihdr(width, height, bitDepth, colorType));
        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static byte[] Ihdr(uint width, uint height, byte bitDepth, byte colorType)
    {
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, width);
        WriteBE(ihdr, 4, height);
        ihdr[8] = bitDepth;
        ihdr[9] = colorType;
        ihdr[10] = 0; // compression
        ihdr[11] = 0; // filter
        ihdr[12] = 0; // interlace
        return ihdr;
    }

    private static byte[] Zlib(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, (uint)data.Length);
        s.Write(len, 0, 4);
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes, 0, 4);
        s.Write(data, 0, data.Length);
        var crc = new byte[4];
        WriteBE(crc, 0, Crc32(typeBytes, data));
        s.Write(crc, 0, 4);
    }

    private static void WriteBE(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        crc = Update(crc, type);
        crc = Update(crc, data);
        return crc ^ 0xFFFFFFFF;
    }

    private static uint Update(uint crc, byte[] bytes)
    {
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return crc;
    }
}
