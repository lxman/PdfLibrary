using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Indexed images whose base is Separation/DeviceN must run their palette entries through the tint
/// transform → alternate → RGB, not paint the raw colorant bytes as RGB (which produced noise).
/// </summary>
public class SeparationDeviceNPaletteTests
{
    private static PdfArray Reals(params double[] v)
    {
        var items = new PdfObject[v.Length];
        for (var i = 0; i < v.Length; i++) items[i] = new PdfReal(v[i]);
        return new PdfArray(items);
    }

    // Type 2 exponential: t -> C0 + t^N (C1 - C0). With N=1 this is a linear ramp C0..C1.
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

    // [/Separation /Black /DeviceCMYK {t -> [0 0 0 t]}]
    private static PdfArray SeparationBlackToCmyk() => new(
        new PdfName("Separation"), new PdfName("Black"), new PdfName("DeviceCMYK"),
        Type2([0, 0, 0, 0], [0, 0, 0, 1]));

    [Fact]
    public void BuildTintToRgb_Separation_MapsTintThroughAlternateToRgb()
    {
        var f = ColorSpaceResolver.BuildTintToRgb(SeparationBlackToCmyk(), null, out int inputs);

        Assert.NotNull(f);
        Assert.Equal(1, inputs);

        (byte r0, byte g0, byte b0) = f!([0.0]);   // tint 0 -> CMYK 0 -> white
        Assert.True(r0 > 250 && g0 > 250 && b0 > 250, $"tint 0 -> ({r0},{g0},{b0}), expected white");

        (byte r1, byte g1, byte b1) = f([1.0]);     // tint 1 -> CMYK [0,0,0,1] -> black
        Assert.True(r1 < 5 && g1 < 5 && b1 < 5, $"tint 1 -> ({r1},{g1},{b1}), expected black");
    }

    [Fact]
    public void GetIndexedPalette_SeparationBase_TransformsToDeviceRgb()
    {
        // [/Indexed [/Separation /Black /DeviceCMYK <tint>] 1 <00 FF>] — 2 one-byte entries: tint 0, tint 1.
        var indexed = new PdfArray(
            new PdfName("Indexed"), SeparationBlackToCmyk(), new PdfInteger(1),
            new PdfString([0x00, 0xFF]));

        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(1),
            [new PdfName("ColorSpace")] = indexed,
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
        };
        var image = new PdfImage(new PdfStream(dict, [0]));

        byte[]? palette = image.GetIndexedPalette(out string? baseColorSpace, out int hival);

        Assert.Equal(1, hival);
        Assert.Equal("DeviceRGB", baseColorSpace);       // was "Separation" (raw)
        Assert.NotNull(palette);
        Assert.Equal(6, palette!.Length);                // 2 entries x 3 RGB (was 2 raw bytes)
        Assert.True(palette[0] > 250 && palette[1] > 250 && palette[2] > 250, "entry 0 should be white");
        Assert.True(palette[3] < 5 && palette[4] < 5 && palette[5] < 5, "entry 1 should be black");
    }
}
