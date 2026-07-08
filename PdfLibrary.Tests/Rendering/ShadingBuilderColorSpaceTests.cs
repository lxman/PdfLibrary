using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// ShadingBuilder must map a shading's sampled /Function output through its /ColorSpace — a
/// Separation/DeviceN ramp runs through the tint transform, not the naive by-component-count guess
/// (which read a 1-output Separation tint as DeviceGray and inverted the ramp).
/// </summary>
public class ShadingBuilderColorSpaceTests
{
    private static PdfArray Reals(params double[] v)
    {
        var items = new PdfObject[v.Length];
        for (var i = 0; i < v.Length; i++) items[i] = new PdfReal(v[i]);
        return new PdfArray(items);
    }

    // Type 2 exponential: t -> C0 + t^N (C1 - C0). N=1 is a linear ramp C0..C1.
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

    private static PdfDictionary AxialShading(PdfObject colorSpace, PdfDictionary function)
    {
        var d = new PdfDictionary();
        d.Add(new PdfName("ShadingType"), new PdfInteger(2));
        d.Add(new PdfName("ColorSpace"), colorSpace);
        d.Add(new PdfName("Coords"), Reals(0, 0, 1, 0));
        d.Add(new PdfName("Domain"), Reals(0, 1));
        d.Add(new PdfName("Function"), function);
        d.Add(new PdfName("Extend"), new PdfArray(PdfBoolean.True, PdfBoolean.True));
        return d;
    }

    private static (byte R, byte G, byte B) Rgb(uint argb) =>
        ((byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));

    [Fact]
    public void Build_SeparationBlack_RunsRampThroughTintTransform()
    {
        // ColorSpace [/Separation /Black /DeviceCMYK {t -> [0 0 0 t]}]; shading /Function t -> [t].
        // Correct: tint 0 -> CMYK 0 -> white; tint 1 -> CMYK[0,0,0,1] -> black.
        // Old bug: 1 output read as DeviceGray -> tint 0 = black, tint 1 = white (inverted).
        var separation = new PdfArray(
            new PdfName("Separation"), new PdfName("Black"), new PdfName("DeviceCMYK"),
            Type2([0, 0, 0, 0], [0, 0, 0, 1]));
        PdfDictionary shading = AxialShading(separation, Type2([0], [1]));

        ShadingDescriptor? d = ShadingBuilder.Build(shading, null);

        Assert.NotNull(d);
        (byte r0, byte g0, byte b0) = Rgb(d!.Colors[0]);
        Assert.True(r0 > 250 && g0 > 250 && b0 > 250, $"stop 0 -> ({r0},{g0},{b0}), expected white");
        (byte r1, byte g1, byte b1) = Rgb(d.Colors[^1]);
        Assert.True(r1 < 5 && g1 < 5 && b1 < 5, $"last stop -> ({r1},{g1},{b1}), expected black");
    }

    [Fact]
    public void Build_DeviceCmyk_SamplesRampCorrectly()
    {
        // Sanity: an explicit DeviceCMYK shading still ramps white (K=0) -> black (K=1).
        PdfDictionary shading = AxialShading(new PdfName("DeviceCMYK"), Type2([0, 0, 0, 0], [0, 0, 0, 1]));

        ShadingDescriptor? d = ShadingBuilder.Build(shading, null);

        Assert.NotNull(d);
        (byte r0, byte g0, byte b0) = Rgb(d!.Colors[0]);
        Assert.True(r0 > 250 && g0 > 250 && b0 > 250, $"stop 0 -> ({r0},{g0},{b0}), expected white");
        (byte r1, byte g1, byte b1) = Rgb(d.Colors[^1]);
        Assert.True(r1 < 5 && g1 < 5 && b1 < 5, $"last stop -> ({r1},{g1},{b1}), expected black");
    }
}
