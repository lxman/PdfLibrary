using Xunit;

namespace PngCodec.Tests;

/// <summary>
/// Regression coverage built from hand-assembled PNG bytes (see <see cref="PngBuilder"/>) for the
/// 16-bit tRNS transparency and dimension-guard fixes. Colour types: 0 = grayscale, 2 = RGB.
/// </summary>
public class PngDecoderTests
{
    [Fact]
    public void Grayscale16_trns_marks_matching_pixel_transparent()
    {
        // Two 16-bit grey samples; the tRNS key 0x1234 matches the first pixel. The old code compared
        // the truncated 8-bit high byte (0x12) against the full 16-bit key (0x1234) and never matched.
        byte[] scanlines = [0x00, 0x12, 0x34, 0xAB, 0xCD]; // filter None + two big-endian samples
        byte[] trns = [0x12, 0x34];
        byte[] png = PngBuilder.Build(2, 1, bitDepth: 16, colorType: 0, scanlines, ("tRNS", trns));

        PngImage img = PngDecoder.Decode(png);

        Assert.Equal((byte)0, img.GetPixel(0, 0).A);   // matches tRNS → transparent
        Assert.Equal((byte)255, img.GetPixel(1, 0).A); // opaque
        Assert.Equal((byte)0x12, img.GetPixel(0, 0).R); // 8-bit output = high byte
    }

    [Fact]
    public void Rgb16_trns_marks_matching_pixel_transparent()
    {
        // 16-bit RGB; tRNS (0x1111,0x2222,0x3333) matches pixel 0. The old code compared 8-bit high
        // bytes against the 16-bit keys.
        byte[] scanlines =
        [
            0x00,
            0x11, 0x11, 0x22, 0x22, 0x33, 0x33, // pixel 0
            0x44, 0x44, 0x55, 0x55, 0x66, 0x66  // pixel 1
        ];
        byte[] trns = [0x11, 0x11, 0x22, 0x22, 0x33, 0x33];
        byte[] png = PngBuilder.Build(2, 1, bitDepth: 16, colorType: 2, scanlines, ("tRNS", trns));

        PngImage img = PngDecoder.Decode(png);

        Assert.Equal((byte)0, img.GetPixel(0, 0).A);
        Assert.Equal((byte)255, img.GetPixel(1, 0).A);
    }

    [Fact]
    public void Grayscale8_trns_still_matches_after_refactor()
    {
        // Guards the refactor: 8-bit grayscale tRNS (which already worked) must keep working. The
        // tRNS key for an 8-bit image is the low byte of the 16-bit field.
        byte[] scanlines = [0x00, 0x05, 0x06];
        byte[] trns = [0x00, 0x05]; // value 5
        byte[] png = PngBuilder.Build(2, 1, bitDepth: 8, colorType: 0, scanlines, ("tRNS", trns));

        PngImage img = PngDecoder.Decode(png);

        Assert.Equal((byte)0, img.GetPixel(0, 0).A);   // grey 5 matches
        Assert.Equal((byte)255, img.GetPixel(1, 0).A); // grey 6 opaque
    }

    [Fact]
    public void Oversize_dimensions_throw_instead_of_overflowing()
    {
        // 20000*20000*4 = 1.6 GB > the 1 GiB cap; under the old check (which only bounded each axis
        // at 32768) this passed and then overflowed the int allocation.
        byte[] png = PngBuilder.HeaderOnly(20000, 20000, bitDepth: 8, colorType: 2);
        Assert.Throws<PngException>(() => PngDecoder.Decode(png));
    }
}
