using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// When UseIccForDeviceCmyk is on (the app's on-screen path), a DeviceCMYK image must resolve to the
/// SAME RGB as a DeviceCMYK fill/shading of that colour — both through the SWOP ICC profile — so a
/// shading and its raster reference render identically (GWG060/061 colour match). Off, it stays naive.
/// Serialized with the dormancy tests: this toggles the process-wide flag.
/// </summary>
[Collection("PdfColorToRgbStatic")]
public class CmykImageIccParityTests
{
    private static PdfImage CmykPixel(byte[] cmyk)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(1),
            [new PdfName("ColorSpace")] = new PdfName("DeviceCMYK"),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
        };
        return new PdfImage(new PdfStream(dict, cmyk));
    }

    [Theory]
    [InlineData(255, 0, 0, 0)]   // cyan
    [InlineData(0, 255, 0, 0)]   // magenta
    [InlineData(0, 0, 255, 0)]   // yellow
    [InlineData(0, 0, 0, 255)]   // black
    public void DeviceCmykImage_MatchesFillPath_UnderIcc(byte c, byte m, byte y, byte k)
    {
        bool prev = PdfColorToRgb.UseIccForDeviceCmyk;
        try
        {
            PdfColorToRgb.UseIccForDeviceCmyk = true;
            byte[] px = PdfImageToRgba.ToRgba(CmykPixel([c, m, y, k]), null)!.Value.Rgba;
            (byte fr, byte fg, byte fb) = PdfColorToRgb.ToRgb([c / 255.0, m / 255.0, y / 255.0, k / 255.0], "DeviceCMYK");
            Assert.Equal(fr, px[0]);
            Assert.Equal(fg, px[1]);
            Assert.Equal(fb, px[2]);
        }
        finally { PdfColorToRgb.UseIccForDeviceCmyk = prev; }
    }

    [Fact]
    public void DeviceCmykImage_Magenta_IsSwopNotNaive_UnderIcc()
    {
        bool prev = PdfColorToRgb.UseIccForDeviceCmyk;
        try
        {
            PdfColorToRgb.UseIccForDeviceCmyk = true;
            byte[] px = PdfImageToRgba.ToRgba(CmykPixel([0, 255, 0, 0]), null)!.Value.Rgba;   // C0 M1 Y0 K0
            Assert.False(px[0] == 255 && px[1] == 0 && px[2] == 255, $"expected SWOP magenta, got naive ({px[0]},{px[1]},{px[2]})");
        }
        finally { PdfColorToRgb.UseIccForDeviceCmyk = prev; }
    }

    [Fact]
    public void DeviceCmykImage_FlagOff_StaysNaive()
    {
        bool prev = PdfColorToRgb.UseIccForDeviceCmyk;
        try
        {
            PdfColorToRgb.UseIccForDeviceCmyk = false;
            byte[] px = PdfImageToRgba.ToRgba(CmykPixel([0, 255, 0, 0]), null)!.Value.Rgba;
            Assert.True(px[0] == 255 && px[1] == 0 && px[2] == 255, $"naive magenta expected, got ({px[0]},{px[1]},{px[2]})");
        }
        finally { PdfColorToRgb.UseIccForDeviceCmyk = prev; }
    }
}
