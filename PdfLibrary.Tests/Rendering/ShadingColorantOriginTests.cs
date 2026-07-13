using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// ShadingBuilder must carry the shading's Separation/DeviceN colorant identity onto
/// <see cref="ShadingDescriptor.ColorantOrigin"/> (Soft-Proof SP-1), the same way it already carries
/// <see cref="ShadingDescriptor.OverprintPlates"/> — additive, no effect on the sampled colour ramp.
/// </summary>
public class ShadingColorantOriginTests
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

    [Fact]
    public void Build_SeparationAxial_CarriesColorantOrigin()
    {
        // [/Separation /PANTONE 185 C /DeviceCMYK <Type2>]; shading /Function t -> [t].
        var sep = new PdfArray(
            new PdfName("Separation"), new PdfName("PANTONE 185 C"), new PdfName("DeviceCMYK"),
            Type2([0, 0, 0, 0], [0, 1, 0, 0]));
        PdfDictionary shading = AxialShading(sep, Type2([0], [1]));

        ShadingDescriptor? d = ShadingBuilder.Build(shading, null);

        Assert.NotNull(d);
        Assert.NotNull(d!.ColorantOrigin);
        Assert.Equal(new[] { "PANTONE 185 C" }, d.ColorantOrigin!.Names);
    }

    [Fact]
    public void Build_DeviceCmykAxial_HasNoColorantOrigin()
    {
        PdfDictionary shading = AxialShading(new PdfName("DeviceCMYK"), Type2([0, 0, 0, 0], [0, 0, 0, 1]));

        ShadingDescriptor? d = ShadingBuilder.Build(shading, null);

        Assert.NotNull(d);
        Assert.Null(d!.ColorantOrigin);
    }
}
