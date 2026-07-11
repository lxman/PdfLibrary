using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// A Separation/DeviceN image must honour its /Decode array (e.g. [1 0] to invert a spot ramp) before
/// running the tint transform — the same way the DeviceCMYK path does. Regression for GWG061 reference
/// image b: a grayscale Separation JPEG with /Decode [1 0] rendered as a colour negative (which, on a
/// smooth gradient, read as an upside-down gradient) because the Separation branch ignored /Decode.
/// </summary>
public class SeparationDecodeArrayTests
{
    private static PdfArray Reals(params double[] v)
    {
        var items = new PdfObject[v.Length];
        for (var i = 0; i < v.Length; i++) items[i] = new PdfReal(v[i]);
        return new PdfArray(items);
    }

    // Type 2 exponential (N=1): linear ramp C0..C1.
    private static PdfDictionary Type2(double[] c0, double[] c1)
    {
        var d = new PdfDictionary();
        d.Add(new PdfName("FunctionType"), new PdfInteger(2));
        d.Add(new PdfName("Domain"), Reals(0, 1));
        d.Add(new PdfName("C0"), Reals(c0));
        d.Add(new PdfName("C1"), Reals(c1));
        d.Add(new PdfName("N"), new PdfReal(1));
        return d;
    }

    // [/Separation /Black /DeviceCMYK {t -> [0 0 0 t]}] : tint 0 -> white, tint 1 -> black.
    private static PdfArray SeparationBlackToCmyk() => new(
        new PdfName("Separation"), new PdfName("Black"), new PdfName("DeviceCMYK"),
        Type2([0, 0, 0, 0], [0, 0, 0, 1]));

    private static PdfImage SepPixel(byte sample, PdfObject? decode)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(1),
            [new PdfName("ColorSpace")] = SeparationBlackToCmyk(),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
        };
        if (decode is not null) dict[new PdfName("Decode")] = decode;
        return new PdfImage(new PdfStream(dict, [sample]));
    }

    [Fact]
    public void Separation_DecodeArray_InvertsTint()
    {
        // Sample 0x00 with no /Decode -> tint 0 -> white.
        byte[] plain = PdfImageToRgba.ToRgba(SepPixel(0x00, null), null)!.Value.Rgba;
        Assert.True(plain[0] > 250 && plain[1] > 250 && plain[2] > 250, $"plain: white expected, got ({plain[0]},{plain[1]},{plain[2]})");

        // Same sample 0x00 with /Decode [1 0] -> tint 1 -> black (the GWG061 case).
        byte[] inv = PdfImageToRgba.ToRgba(
            SepPixel(0x00, new PdfArray(new PdfInteger(1), new PdfInteger(0))), null)!.Value.Rgba;
        Assert.True(inv[0] < 5 && inv[1] < 5 && inv[2] < 5, $"decode[1 0]: white->black expected, got ({inv[0]},{inv[1]},{inv[2]})");
    }

    [Fact]
    public void Separation_MaxSample_DecodeArray_InvertsToWhite()
    {
        // Sample 0xFF with /Decode [1 0] -> tint 0 -> white (the opposite end of the ramp).
        byte[] px = PdfImageToRgba.ToRgba(
            SepPixel(0xFF, new PdfArray(new PdfInteger(1), new PdfInteger(0))), null)!.Value.Rgba;
        Assert.True(px[0] > 250 && px[1] > 250 && px[2] > 250, $"decode[1 0] max sample: white expected, got ({px[0]},{px[1]},{px[2]})");
    }

    // The CMYK native-ink path (PdfImageToCmyk) must honour /Decode on Separation/DeviceN images too —
    // bd5823c fixed only the RGBA path, so the CMYK-proof render of GWG061/6.0/6.1 reference image b (the
    // grayscale Separation JPEG) still came out a colour negative (light↔dark flipped). The Separation
    // "Black" tint maps t -> [0 0 0 t], so the K byte (index 3) tracks the tint directly.

    [Fact]
    public void Cmyk_Separation_DecodeArray_InvertsTint()
    {
        // Sample 0x00, no /Decode -> tint 0 -> white -> CMYK (0,0,0,0): K low.
        byte[]? plain = PdfImageToCmyk.TryToCmyk(SepPixel(0x00, null), null, out _, out _);
        Assert.NotNull(plain);
        Assert.True(plain![3] < 5, $"plain K expected ~0 (white), got {plain[3]}");

        // Same sample with /Decode [1 0] -> tint 1 -> black -> CMYK (0,0,0,1): K high.
        byte[]? inv = PdfImageToCmyk.TryToCmyk(
            SepPixel(0x00, new PdfArray(new PdfInteger(1), new PdfInteger(0))), null, out _, out _);
        Assert.NotNull(inv);
        Assert.True(inv![3] > 250, $"decode[1 0] K expected ~255 (black), got {inv[3]}");
    }

    [Fact]
    public void Cmyk_Separation_MaxSample_DecodeArray_InvertsToWhite()
    {
        // Sample 0xFF with /Decode [1 0] -> tint 0 -> white -> CMYK (0,0,0,0): K low (opposite ramp end).
        byte[]? px = PdfImageToCmyk.TryToCmyk(
            SepPixel(0xFF, new PdfArray(new PdfInteger(1), new PdfInteger(0))), null, out _, out _);
        Assert.NotNull(px);
        Assert.True(px![3] < 5, $"decode[1 0] max sample: K expected ~0 (white), got {px[3]}");
    }
}
