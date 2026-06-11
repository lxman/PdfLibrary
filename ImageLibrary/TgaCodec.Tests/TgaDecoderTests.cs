using System;
using Xunit;

namespace TgaCodec.Tests;

/// <summary>
/// Regression coverage for the TGA decoder: 16-bit 5-to-8-bit channel expansion, 15-bit pixel depth
/// (which the old PixelDepth/8 truncated to 1 byte/pixel), the colour-mapped path, and the dimension
/// guard. TGA image types: 1 = colour-mapped, 2 = true-colour.
/// </summary>
public class TgaDecoderTests
{
    // Builds an 18-byte TGA header.
    private static byte[] Header(byte imageType, ushort width, ushort height, byte pixelDepth,
        byte imageDescriptor, byte colorMapType = 0, ushort colorMapLength = 0, byte colorMapEntrySize = 0)
    {
        var h = new byte[18];
        h[1] = colorMapType;
        h[2] = imageType;
        // 3..4 first entry = 0
        h[5] = (byte)colorMapLength; h[6] = (byte)(colorMapLength >> 8);
        h[7] = colorMapEntrySize;
        h[12] = (byte)width; h[13] = (byte)(width >> 8);
        h[14] = (byte)height; h[15] = (byte)(height >> 8);
        h[16] = pixelDepth;
        h[17] = imageDescriptor;
        return h;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var total = 0;
        foreach (byte[] p in parts) total += p.Length;
        var result = new byte[total];
        var o = 0;
        foreach (byte[] p in parts) { Array.Copy(p, 0, result, o, p.Length); o += p.Length; }
        return result;
    }

    [Fact]
    public void Bit16_white_expands_to_full_255()
    {
        // 0x7FFF = R=G=B=31, attribute bit clear. Correct 5->8 expansion gives 255; the old v<<3 gave
        // 248. imageDescriptor 0x20 = top-to-bottom, no alpha bits.
        byte[] tga = Concat(Header(2, 1, 1, 16, 0x20), [0xFF, 0x7F]);

        TgaImage img = TgaDecoder.Decode(tga);

        (byte R, byte G, byte B, byte A) p = img.GetPixel(0, 0);
        Assert.Equal(((byte)255, (byte)255, (byte)255), (p.R, p.G, p.B));
    }

    [Fact]
    public void Bit15_pixel_depth_reads_two_bytes_per_pixel()
    {
        // 15-bit is 2 bytes/pixel (same layout as 16-bit). The old PixelDepth/8 == 1 read one byte and
        // then threw "unsupported pixel depth: 8 bits". 0x7C00 = pure red.
        byte[] tga = Concat(Header(2, 1, 1, 15, 0x20), [0x00, 0x7C]);

        TgaImage img = TgaDecoder.Decode(tga);

        (byte R, byte G, byte B, byte A) p = img.GetPixel(0, 0);
        Assert.Equal(((byte)255, (byte)0, (byte)0), (p.R, p.G, p.B));
    }

    [Fact]
    public void Colour_mapped_expands_indices_through_the_map()
    {
        // 2 pixels indexing a 2-entry 24-bit colour map. TGA stores map entries B,G,R: entry 0 = blue,
        // entry 1 = red. Pixels: index 0, index 1.
        byte[] header = Header(1, 2, 1, 8, 0x20, colorMapType: 1, colorMapLength: 2, colorMapEntrySize: 24);
        byte[] colorMap = [0xFF, 0x00, 0x00, /* blue */ 0x00, 0x00, 0xFF /* red */];
        byte[] pixels = [0x00, 0x01];
        byte[] tga = Concat(header, colorMap, pixels);

        TgaImage img = TgaDecoder.Decode(tga);

        Assert.Equal(((byte)0, (byte)0, (byte)255), Rgb(img.GetPixel(0, 0)));  // blue
        Assert.Equal(((byte)255, (byte)0, (byte)0), Rgb(img.GetPixel(1, 0)));  // red
    }

    [Fact]
    public void Oversize_dimensions_throw()
    {
        // 60000x60000 true-colour → width*height*4 overflows int; the guard throws cleanly.
        byte[] tga = Header(2, 60000, 60000, 24, 0x20);
        Assert.Throws<TgaException>(() => TgaDecoder.Decode(tga));
    }

    private static (byte, byte, byte) Rgb((byte R, byte G, byte B, byte A) p) => (p.R, p.G, p.B);
}
