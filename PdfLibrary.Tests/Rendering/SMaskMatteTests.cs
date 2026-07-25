using System;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// A soft-mask image may carry <c>/Matte</c> (ISO 32000-1 §11.6.5.3), which declares that the PARENT
/// image's colour samples have been <b>pre-multiplied</b> against that matte colour. A renderer that
/// treats such an image as straight alpha renders every partially-transparent pixel pulled toward the
/// matte — with the usual black matte, visibly too dark. The reconstruction is
/// <c>c = m + (c' − m) / α</c>.
///
/// Found by the 4,874-page multi-oracle sweep: ISO 32000-2 errata figures (pages 1005/1017/1018/1019)
/// rendered their soft drop-shadow halo grey-olive where poppler, mutool and Ghostscript all render it
/// pale cream — the worst real-content disagreement in the corpus at 19.6%.
/// </summary>
public class SMaskMatteTests
{
    /// <summary>A 1×2 DeviceRGB image whose SMask supplies <paramref name="alphas"/>, optionally with
    /// a <c>/Matte</c> entry in the base image's colour space.</summary>
    private static PdfImage RgbWithSMask(byte[] rgbData, byte[] alphas, double[]? matte)
    {
        var smaskDict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(alphas.Length),
            [new PdfName("ColorSpace")] = new PdfName("DeviceGray"),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
        };
        if (matte is not null)
        {
            var arr = new PdfArray();
            foreach (double m in matte) arr.Add(new PdfReal(m));
            smaskDict[new PdfName("Matte")] = arr;
        }

        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(alphas.Length),
            [new PdfName("ColorSpace")] = new PdfName("DeviceRGB"),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
            [new PdfName("SMask")] = new PdfStream(smaskDict, alphas),
        };
        return new PdfImage(new PdfStream(dict, rgbData));
    }

    private static void AssertPixel(byte[] rgba, int index, byte r, byte g, byte b, int tolerance = 2)
    {
        int o = index * 4;
        Assert.True(Math.Abs(rgba[o] - r) <= tolerance
                    && Math.Abs(rgba[o + 1] - g) <= tolerance
                    && Math.Abs(rgba[o + 2] - b) <= tolerance,
            $"pixel {index}: expected ~({r},{g},{b}) got ({rgba[o]},{rgba[o + 1]},{rgba[o + 2]})");
    }

    [Fact]
    public void Black_matte_is_un_premultiplied_back_to_the_true_colour()
    {
        // True colour (200,100,60). With a BLACK matte at alpha 0.5 the stored sample is
        // c' = m + a(c - m) = 0.5 * c = (100,50,30). Reconstruction must recover the original.
        byte[] rgb = [200, 100, 60, 100, 50, 30];
        var result = PdfImageToRgba.ToRgba(RgbWithSMask(rgb, [255, 128], [0, 0, 0]), null);

        Assert.NotNull(result);
        byte[] px = result!.Value.Rgba;
        AssertPixel(px, 0, 200, 100, 60);   // alpha 1.0 - untouched by the formula
        AssertPixel(px, 1, 200, 100, 60);   // alpha 0.5 - was (100,50,30) before the fix
        Assert.Equal(128, px[7]);           // alpha channel itself must be preserved
    }

    [Fact]
    public void White_matte_is_un_premultiplied_back_to_the_true_colour()
    {
        // True colour black (0,0,0) over a WHITE matte at alpha 0.5 stores c' = 1 + 0.5(0-1) = 0.5 → 128.
        byte[] rgb = [0, 0, 0, 128, 128, 128];
        var result = PdfImageToRgba.ToRgba(RgbWithSMask(rgb, [255, 128], [1, 1, 1]), null);

        Assert.NotNull(result);
        byte[] px = result!.Value.Rgba;
        AssertPixel(px, 0, 0, 0, 0);
        AssertPixel(px, 1, 0, 0, 0);        // was (128,128,128) before the fix
    }

    [Fact]
    public void SMask_without_Matte_is_straight_alpha_and_must_not_be_touched()
    {
        // Regression guard: the overwhelming majority of soft masks carry no /Matte. Their colour
        // samples are NOT premultiplied, so applying the reconstruction would wrongly brighten them.
        byte[] rgb = [200, 100, 60, 100, 50, 30];
        var result = PdfImageToRgba.ToRgba(RgbWithSMask(rgb, [255, 128], matte: null), null);

        Assert.NotNull(result);
        byte[] px = result!.Value.Rgba;
        AssertPixel(px, 0, 200, 100, 60);
        AssertPixel(px, 1, 100, 50, 30);    // unchanged
    }

    [Fact]
    public void Fully_transparent_pixels_do_not_divide_by_zero()
    {
        byte[] rgb = [200, 100, 60, 0, 0, 0];
        var result = PdfImageToRgba.ToRgba(RgbWithSMask(rgb, [255, 0], [0, 0, 0]), null);

        Assert.NotNull(result);
        byte[] px = result!.Value.Rgba;
        Assert.Equal(0, px[7]);             // alpha 0 - colour is undefined, must not throw or NaN
    }
}
