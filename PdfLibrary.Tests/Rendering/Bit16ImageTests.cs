using System;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// 16-bit-per-component images decode by taking each sample's big-endian high byte (the low byte is
/// sub-perceptual), so the existing 8-bit colour pipeline handles every space. Before this support,
/// <see cref="PdfImageToRgba.ToRgba"/> returned null for 16 bpc and the image was dropped (GWG180-184).
/// </summary>
public class Bit16ImageTests
{
    private static PdfImage Image16(string colorSpace, byte[] data)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(1),
            [new PdfName("ColorSpace")] = new PdfName(colorSpace),
            [new PdfName("BitsPerComponent")] = new PdfInteger(16),
        };
        return new PdfImage(new PdfStream(dict, data));
    }

    [Fact]
    public void DeviceRgb_16bit_decodes_via_big_endian_high_byte()
    {
        // Big-endian samples R=0x12.., G=0x56.., B=0x9A.. → high bytes drive the pixel (low bytes ignored).
        var result = PdfImageToRgba.ToRgba(Image16("DeviceRGB", [0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC]), null);
        Assert.NotNull(result);   // was null before 16 bpc support
        byte[] px = result!.Value.Rgba;
        Assert.True(Math.Abs(px[0] - 0x12) <= 1, $"R={px[0]:X2}");
        Assert.True(Math.Abs(px[1] - 0x56) <= 1, $"G={px[1]:X2}");
        Assert.True(Math.Abs(px[2] - 0x9A) <= 1, $"B={px[2]:X2}");
    }

    [Fact]
    public void DeviceGray_16bit_decodes_via_high_byte()
    {
        var result = PdfImageToRgba.ToRgba(Image16("DeviceGray", [0xAB, 0xCD]), null);
        Assert.NotNull(result);
        byte[] px = result!.Value.Rgba;
        Assert.True(Math.Abs(px[0] - 0xAB) <= 1 && Math.Abs(px[1] - 0xAB) <= 1 && Math.Abs(px[2] - 0xAB) <= 1,
            $"gray=({px[0]:X2},{px[1]:X2},{px[2]:X2})");
    }
}
