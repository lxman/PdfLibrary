using Xunit;

namespace BmpCodec.Tests;

/// <summary>
/// Covers the BITFIELDS decode path, which was previously a stub that fed the data through the fixed
/// 5-5-5 RGB decoder — wrong for the common 5-6-5 (16-bit) and masked 32-bit layouts. Also covers the
/// dimension guard.
/// </summary>
public class BmpBitFieldsTests
{
    [Fact]
    public void Bitfields_565_decodes_green_correctly()
    {
        // 5-6-5 pure green = 0x07E0. The old 5-5-5 stub misreads this as roughly (R=8, G=248);
        // a correct 5-6-5 decode gives pure green (0, 255, 0).
        byte[] pixels = [0xE0, 0x07, 0x00, 0x00]; // 0x07E0 little-endian + row padding to 4 bytes
        byte[] bmp = BmpBuilder.BitFields(1, 1, 16, 0xF800, 0x07E0, 0x001F, pixels);

        BmpImage img = BmpDecoder.Decode(bmp);

        (byte R, byte G, byte B, byte A) p = img.GetPixel(0, 0);
        Assert.Equal(((byte)0, (byte)255, (byte)0), (p.R, p.G, p.B));
    }

    [Fact]
    public void Bitfields_8888_decodes_channels_by_mask()
    {
        // 32-bit with R/G/B masks; pixel 0x00FF8040 → R=0xFF, G=0x80, B=0x40.
        byte[] pixels = [0x40, 0x80, 0xFF, 0x00]; // little-endian 0x00FF8040
        byte[] bmp = BmpBuilder.BitFields(1, 1, 32, 0x00FF0000, 0x0000FF00, 0x000000FF, pixels);

        BmpImage img = BmpDecoder.Decode(bmp);

        (byte R, byte G, byte B, byte A) p = img.GetPixel(0, 0);
        Assert.Equal(((byte)255, (byte)128, (byte)64), (p.R, p.G, p.B));
        Assert.Equal((byte)255, p.A); // no alpha mask → opaque
    }

    [Fact]
    public void Oversize_dimensions_throw_instead_of_overflowing()
    {
        // 20000*20000*4 = 1.6 GB > the 1 GiB cap; the old check only bounded each axis at 32768 and
        // then overflowed the int allocation at 32768x32768.
        byte[] bmp = BmpBuilder.HeaderOnly(20000, 20000);
        Assert.Throws<BmpException>(() => BmpDecoder.Decode(bmp));
    }
}
